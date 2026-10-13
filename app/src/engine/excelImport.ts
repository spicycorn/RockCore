/**
 * 岩心回次统计 Excel 导入引擎（模板 v10：长表格式，一段一行）。
 *
 * 录入格式（两个数据表，全部长度单位为米，软件内部换算成 cm 判级）：
 *   「回次表」：回次号 | 回次进尺(m)          —— 行序 = 由浅到深的钻进顺序；
 *   「岩心表」：回次号 | 类型(完整|碎屑) | 长度(m) | 块数(仅碎屑)
 *              —— 同一回次的行按由浅到深排列，一段一行，无列数上限。
 * F.0.2 六参数（岩质/结构/坚硬程度/均一性/地下水/洞轴夹角）是室内判级参数，
 * 不在现场表里填，改在软件的分段编辑器中录入。
 *
 * 数据链（三层，顺序不可颠倒）：
 *   回次记录段（每个碎屑行独立成段，连续完整行并成一段）
 *     → 岩体段（评价单元：相邻、平均间距相近的记录段归并，见 segments.ts）
 *       → 完整性分段（对归并段判级后入库）
 *
 * 铁律：
 *   · 全部派生指标由原始数据重算，不信任 Excel 公式列；
 *     n = 完整行数 + Σ碎屑块数；S = 进尺×100÷n (cm)；RQD = ΣL÷进尺×100；
 *   · 判级只按平均间距 S 查表 F.0.4——「结构面发育组数」是地质描述量，回次取芯算不出它；
 *   · 先归并、后判级：归并只用原始量（进尺/段数/段长/岩质），不用判级结果；
 *   · 闭合校验按回次号自动汇总：Σ长度 = 进尺×100（±1cm），当场暴露漏记；
 *   · 旧版模板（v9 及更早的宽表）整表拒收，指引重新下载。
 *
 * 本模块是纯函数（字节 → 预览），不触碰数据库；入库由 db 层完成。
 */

import ExcelJS from 'exceljs';
import { IntegrityLevel } from './enums';
import { judgeIntegrity } from './integrity';
import { buildOnePerRun, buildRockSegments, type RockMassRunInput } from './segments';
import type { RockSpecificationConfig } from './config';

// ---------------------------------------------------------------------------
// 预览数据结构
// ---------------------------------------------------------------------------

export type ImportRowStatus = 'ok' | 'warning' | 'blocked';

/** 预览单行 = 一个记录段（每个碎屑行独立成段；连续完整行并成一段） */
export interface ImportRow {
  /** 「岩心表」中该记录段首行的行号（被阻断的脏行 = 其自身行号） */
  rowNumber: number;
  runNo: number | null;
  /** 本记录段厚度（m） */
  runLengthM: number | null;
  /** 本行所属回次的总进尺（m） */
  runTotalLengthM: number | null;
  /** 碎屑块数；仅碎屑段有值 */
  fragCount: number | null;
  /** 碎屑带总长（cm）；仅碎屑段有值 */
  fragLengthCm: number | null;
  /** ≥10cm 完整段长度列表（cm，按由浅到深） */
  pieces: number[];
  /** 本行所属回次拆出的记录段数（1 = 未拆段） */
  runRowCount: number;
  /** 本行是该回次内由浅到深的第几段（1 起） */
  runPartIndex: number;

  readonly pieceCount: number;
  readonly totalLengthCm: number;

  rqd: number | null;
  depthStart: number;
  depthEnd: number;
  totalPieces: number;
  jointCount: number;
  avgSpacingCm: number;
  level: IntegrityLevel;

  status: ImportRowStatus;
  messages: string[];
  /** 归入的完整性分段编号（1 起；0 = 未归入，如被阻断行） */
  segmentNo: number;
}

/** 完整性分段（岩体段）：由相邻、平均间距相近的记录段归并而成，规范意义上的评价单元 */
export interface ImportSegment {
  no: number;
  depthStart: number;
  depthEnd: number;
  thicknessM: number;
  runCount: number;
  totalPieces: number;
  spacingCm: number;
  rqd: number;
  level: IntegrityLevel;
}

export interface ImportPreview {
  error: string | null;
  rows: ImportRow[];
  /** 归并后的完整性分段（= 实际写入数据库的内容） */
  segments: ImportSegment[];
  /** 归并口径说明，直接展示在导入对话框底部 */
  segmentNote: string;
}

// ---------------------------------------------------------------------------
// 常量（模板 v10 列序）
// ---------------------------------------------------------------------------

const SHEET_RUNS = '回次表';
const SHEET_CORES = '岩心表';
const SHEET_LEGACY = '数据录入';

const COL_RUN_NO = 1;
const COL_RUN_LEN = 2;

const COL_CORE_RUN_NO = 1;
const COL_CORE_TYPE = 2;
const COL_CORE_LEN = 3;
const COL_CORE_CNT = 4;

export const TYPE_COMPLETE = '完整';
export const TYPE_FRAG = '碎屑';

/** 完整段下限（米）：10cm。模板单位为米，解析后统一换算成 cm 判级。 */
const MIN_PIECE_M = 0.1;

/** 深度闭合容差（cm）：1mm 级量测误差。 */
const CLOSURE_TOL_CM = 1.0;

/** 旧版模板拒收文案（v9 及更早的宽表格式） */
const LEGACY_REJECT =
  '这是旧版模板（v9 及更早：一回次三行 × L1..L30 宽表）。当前模板（v10）改为长表格式：' +
  '「回次表」记 回次号/进尺(m)，「岩心表」一段一行记 回次号/类型(完整|碎屑)/长度(m)/块数，单位全部为米；' +
  '岩质、结构等判级参数不再在 Excel 中填写，改在软件内录入。' +
  '请用「下载导入模板」重新下载，把数据按新格式整理后再导入';

// ---------------------------------------------------------------------------
// 数字格式化（对齐 C# 复合格式 {x:0.##} 一类输出）
// ---------------------------------------------------------------------------

/** 最多 maxDigits 位小数，去尾零（C# "0.##"） */
function fmtMax(value: number, maxDigits: number): string {
  return String(Number(value.toFixed(maxDigits)));
}

/** 固定 digits 位小数（C# "0.00"） */
function fmtFix(value: number, digits: number): string {
  return value.toFixed(digits);
}

// ---------------------------------------------------------------------------
// 单元格读取
// ---------------------------------------------------------------------------

/** 与 C# double.TryParse(NumberStyles.Float, InvariantCulture) 对齐的严格数字解析 */
function tryParseDouble(text: string): number | null {
  const s = text.trim();
  if (s.length === 0) return null;
  if (!/^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$/.test(s)) return null;
  const v = Number(s);
  return Number.isFinite(v) ? v : null;
}

function cellRawText(value: ExcelJS.CellValue): string {
  if (value == null) return '';
  if (typeof value === 'string') return value.trim();
  if (typeof value === 'number' || typeof value === 'boolean' || value instanceof Date) return '';
  if (value instanceof Error) return '';
  if ('result' in value) return cellRawText(value.result as ExcelJS.CellValue);
  if ('richText' in value) return value.richText.map((p) => p.text).join('').trim();
  if ('text' in value) return String(value.text).trim();
  if ('error' in value) return '';
  return '';
}

function cellNum(cell: ExcelJS.Cell): number | null {
  const v = cell.value;
  if (v == null) return null;
  if (typeof v === 'number') return v;
  if (typeof v === 'string') return tryParseDouble(v);
  if (typeof v === 'object' && !(v instanceof Date) && !(v instanceof Error) && 'result' in v) {
    const r = v.result;
    if (typeof r === 'number') return r;
    if (typeof r === 'string') return tryParseDouble(r);
  }
  return null;
}

/** 数值格：只能填数字；返回 {num, text}，两者皆空 = 未填。 */
function readNumCell(cell: ExcelJS.Cell): { num: number | null; text: string } {
  const num = cellNum(cell);
  if (num != null) return { num, text: '' };
  return { num: null, text: cellRawText(cell.value) };
}

function readIntCell(cell: ExcelJS.Cell): number | null {
  const v = cellNum(cell);
  return v != null ? Math.round(v) : null;
}

// ---------------------------------------------------------------------------
// 内部结构
// ---------------------------------------------------------------------------

/** 「回次表」的一行 */
interface RunEntry {
  row: number;
  runNo: number | null;
  runLengthM: number | null;
}

/** 「岩心表」的一行（一个岩心区间） */
interface CoreEntry {
  row: number;
  runNo: number | null;
  type: string;
  len: { num: number | null; text: string };
  cnt: { num: number | null; text: string };
}

/** 回次自动拆出的一个记录段：完整段（pieces）或碎屑段（frag*）。 */
interface SubSeg {
  pieces: number[];
  fragCount: number | null;
  fragLengthCm: number | null;
  thicknessCm: number;
}

function newImportRow(rowNumber: number): ImportRow {
  const row: ImportRow = {
    rowNumber,
    runNo: null,
    runLengthM: null,
    runTotalLengthM: null,
    fragCount: null,
    fragLengthCm: null,
    pieces: [],
    runRowCount: 1,
    runPartIndex: 1,
    pieceCount: 0,
    totalLengthCm: 0,
    rqd: null,
    depthStart: 0,
    depthEnd: 0,
    totalPieces: 0,
    jointCount: 0,
    avgSpacingCm: 0,
    level: IntegrityLevel.Unknown,
    status: 'ok',
    messages: [],
    segmentNo: 0,
  };
  // pieceCount / totalLengthCm 由 pieces 派生（对齐 C# 只读属性）
  Object.defineProperty(row, 'pieceCount', { get: () => row.pieces.length, enumerable: true });
  Object.defineProperty(row, 'totalLengthCm', {
    get: () => Math.round(row.pieces.reduce((s, p) => s + p, 0) * 100) / 100,
    enumerable: true,
  });
  return row;
}

// ---------------------------------------------------------------------------
// 预览：解析 + 重算 + 校验 + 判级（不落库）
// ---------------------------------------------------------------------------

/**
 * 解析模板工作簿（字节 → 预览）。config 提供判定表与归并阈值。
 * 返回 error ≠ null 表示整表拒收（版本不符/缺表/打不开）。
 */
export async function previewWorkbook(
  buffer: ArrayBuffer,
  config: RockSpecificationConfig,
): Promise<ImportPreview> {
  const preview: ImportPreview = { error: null, rows: [], segments: [], segmentNote: '' };

  const workbook = new ExcelJS.Workbook();
  try {
    await workbook.xlsx.load(buffer);
  } catch (ex) {
    preview.error = `无法打开 Excel 文件：${ex instanceof Error ? ex.message : String(ex)}`;
    return preview;
  }

  const findSheet = (name: string) => workbook.worksheets.find((ws) => ws.name.trim() === name);
  const wsCore = findSheet(SHEET_CORES);

  // 旧版模板（v9 及更早的宽表）整表拒收：宽表读入长表会静默丢数据，必须挡在解析前。
  // v10 模板绝不含「数据录入」表——出现即旧表（哪怕被塞进了新表壳）。
  if (findSheet(SHEET_LEGACY) != null) {
    preview.error = LEGACY_REJECT;
    return preview;
  }
  const wsRun = findSheet(SHEET_RUNS);
  if (wsRun == null || wsCore == null) {
    const missing = wsRun == null && wsCore == null
      ? `「${SHEET_RUNS}」「${SHEET_CORES}」`
      : wsRun == null ? `「${SHEET_RUNS}」` : `「${SHEET_CORES}」`;
    preview.error = `未找到${missing}工作表，请使用模板文件填写（旧版模板请先在软件中重新下载）`;
    return preview;
  }

  // 表头识别：缺关键表头 = 版本不符/表被改坏，拒收而不是逐行报错。
  const runHeader = [
    cellRawText(wsRun.getCell(1, COL_RUN_NO).value),
    cellRawText(wsRun.getCell(1, COL_RUN_LEN).value),
  ];
  if (!runHeader[0].includes('回次号') || !runHeader[1].includes('进尺')) {
    preview.error = `「${SHEET_RUNS}」表头不符（应为：回次号 | 回次进尺(m)），请使用模板文件填写`;
    return preview;
  }
  const coreHeader = [
    cellRawText(wsCore.getCell(1, COL_CORE_RUN_NO).value),
    cellRawText(wsCore.getCell(1, COL_CORE_TYPE).value),
    cellRawText(wsCore.getCell(1, COL_CORE_LEN).value),
    cellRawText(wsCore.getCell(1, COL_CORE_CNT).value),
  ];
  if (!coreHeader[0].includes('回次号') || !coreHeader[1].includes('类型')
    || !coreHeader[2].includes('长度') || !coreHeader[3].includes('块数')) {
    preview.error = `「${SHEET_CORES}」表头不符（应为：回次号 | 类型 | 长度(m) | 块数），请使用模板文件填写`;
    return preview;
  }

  // ---- 读「回次表」：行序 = 深度序 ----
  const runs: RunEntry[] = [];
  const runByNo = new Map<number, RunEntry>();
  for (let r = 2; r <= wsRun.actualRowCount; r++) {
    const runNo = readIntCell(wsRun.getCell(r, COL_RUN_NO));
    const len = readNumCell(wsRun.getCell(r, COL_RUN_LEN));
    if (runNo == null && len.num == null && len.text.length === 0) continue; // 整行空白
    const entry: RunEntry = { row: r, runNo, runLengthM: len.num };
    runs.push(entry);
    if (runNo != null) {
      if (runByNo.has(runNo)) {
        const bad = newImportRow(r);
        bad.runNo = runNo;
        bad.messages.push(`回次号 ${runNo} 在「${SHEET_RUNS}」中重复`);
        bad.status = 'blocked';
        preview.rows.push(bad);
        continue;
      }
      runByNo.set(runNo, entry);
    }
  }

  // ---- 读「岩心表」：按回次号分组，组内保持行序（= 由浅到深）----
  const coresByRun = new Map<number, CoreEntry[]>();
  for (let r = 2; r <= wsCore.actualRowCount; r++) {
    const runNo = readIntCell(wsCore.getCell(r, COL_CORE_RUN_NO));
    const type = cellRawText(wsCore.getCell(r, COL_CORE_TYPE).value);
    const len = readNumCell(wsCore.getCell(r, COL_CORE_LEN));
    const cnt = readNumCell(wsCore.getCell(r, COL_CORE_CNT));
    if (runNo == null && type.length === 0 && len.num == null && len.text.length === 0
      && cnt.num == null && cnt.text.length === 0) continue; // 整行空白

    const entry: CoreEntry = { row: r, runNo, type, len, cnt };
    if (runNo == null || !runByNo.has(runNo)) {
      const bad = newImportRow(r);
      bad.runNo = runNo;
      bad.messages.push(runNo == null
        ? `缺少回次号（「${SHEET_CORES}」每行都要填所属回次号）`
        : `回次号 ${runNo} 不在「${SHEET_RUNS}」中`);
      bad.status = 'blocked';
      preview.rows.push(bad);
      continue;
    }
    let list = coresByRun.get(runNo);
    if (list == null) {
      list = [];
      coresByRun.set(runNo, list);
    }
    list.push(entry);
  }

  // ---- 逐回次校验 + 拆记录段 + 重算指标（回次表行序 = 深度序）----
  let depthCursor = 0;
  for (const run of runs) {
    emitRun(preview, run, coresByRun.get(run.runNo ?? -1) ?? [], config, depthCursor);
    if (run.runLengthM != null && run.runLengthM > 0) depthCursor += run.runLengthM;
  }

  if (preview.rows.length === 0) {
    preview.error = `「${SHEET_RUNS}」中没有数据行`;
    return preview;
  }

  // 归并成完整性分段（预览默认锚点 0；导入时会按实际锚点重算）
  rebuildSegments(preview, preview.rows.filter((r) => r.status !== 'blocked'), config);
  return preview;
}

// ---------------------------------------------------------------------------
// 回次解析：校验 → 拆记录段 → 重算指标
// ---------------------------------------------------------------------------

function emitRun(
  preview: ImportPreview,
  run: RunEntry,
  cores: CoreEntry[],
  config: RockSpecificationConfig,
  depthCursor: number,
): void {
  const errors: string[] = [];
  const warns: string[] = [];

  if (run.runNo == null) errors.push(`「${SHEET_RUNS}」第 ${run.row} 行缺少回次号`);
  if (run.runLengthM == null) errors.push(`回次 ${run.runNo ?? '?'} 缺少进尺（「${SHEET_RUNS}」B 列）`);
  else if (run.runLengthM <= 0) errors.push(`回次 ${run.runNo ?? '?'} 进尺必须大于 0`);

  // ===== 逐行解析岩心区间 =====
  const intervals: Array<{ isPiece: boolean; lengthCm: number; count: number }> = [];
  let firstCoreRow = 0;
  let sumPieces = 0;
  let sumFragLen = 0;
  let fragTotal = 0;
  let pieceCount = 0;
  cores.forEach((c, idx) => {
    if (idx === 0) firstCoreRow = c.row;
    const seq = idx + 1;
    const label = `回次 ${run.runNo ?? '?'} 第 ${seq} 段`;

    if (c.type !== TYPE_COMPLETE && c.type !== TYPE_FRAG) {
      errors.push(`${label}：类型「${c.type}」无法识别（应为「${TYPE_COMPLETE}」或「${TYPE_FRAG}」）`);
      return;
    }
    if (c.len.num == null) {
      errors.push(c.len.text.length > 0
        ? `${label}：长度只能填数字（米），读到「${c.len.text}」`
        : `${label}：缺少长度`);
      return;
    }
    const lenCm = Math.round(c.len.num * 100 * 100) / 100;

    if (c.type === TYPE_COMPLETE) {
      if (c.cnt.num != null || c.cnt.text.length > 0) {
        errors.push(`${label}：完整段不能填块数（块数仅碎屑段填写）`);
        return;
      }
      if (c.len.num < MIN_PIECE_M) {
        errors.push(`${label}：段长 ${fmtMax(c.len.num, 3)}m <0.1m，<10cm 的碎屑应记为「${TYPE_FRAG}」行`);
        return;
      }
      intervals.push({ isPiece: true, lengthCm: lenCm, count: 1 });
      sumPieces += lenCm;
      pieceCount++;
      return;
    }

    // 碎屑段
    if (lenCm <= 0) {
      errors.push(`${label}：碎屑长度必须大于 0`);
      return;
    }
    let cnt: number;
    if (c.cnt.num == null && c.cnt.text.length === 0) {
      cnt = 1;
      warns.push(`${label}：碎屑长度有值但块数未填，按 1 块计`);
    } else if (c.cnt.num == null) {
      errors.push(`${label}：块数只能填整数，读到「${c.cnt.text}」`);
      return;
    } else {
      if (Math.abs(c.cnt.num - Math.round(c.cnt.num)) > 1e-9) {
        errors.push(`${label}：块数 ${fmtMax(c.cnt.num, 2)} 不是整数`);
        return;
      }
      cnt = Math.round(c.cnt.num);
      if (cnt < 1) {
        errors.push(`${label}：块数必须 ≥1`);
        return;
      }
    }
    const avg = lenCm / cnt;
    if (avg >= 10) warns.push(`${label}：碎屑平均 ${fmtFix(avg, 1)}cm ≥10cm，与「<10cm」矛盾，请核对块数`);
    else if (avg < 0.5) warns.push(`${label}：碎屑平均 ${fmtFix(avg, 2)}cm <0.5cm，疑似把同一破碎带拆成了多行`);
    intervals.push({ isPiece: false, lengthCm: lenCm, count: cnt });
    sumFragLen += lenCm;
    fragTotal += cnt;
  });

  const n = pieceCount + fragTotal;
  const runLenPositive = run.runLengthM != null && run.runLengthM > 0;
  if (runLenPositive && n === 0 && cores.length > 0) errors.push('既无完整段也无碎屑记录，无法判级');
  if (cores.length === 0 && runLenPositive) errors.push(`回次 ${run.runNo ?? '?'} 在「${SHEET_CORES}」中没有任何岩心记录`);

  // ===== 深度闭合校验：Σ长度 = 进尺×100（±1cm）=====
  const targetCm = runLenPositive ? (run.runLengthM as number) * 100 : 0;
  const diff = sumPieces + sumFragLen - targetCm;
  if (runLenPositive && errors.length === 0 && cores.length > 0) {
    if (diff > CLOSURE_TOL_CM) {
      errors.push(`段长+碎屑长合计 ${fmtMax(sumPieces + sumFragLen, 1)}cm 超过进尺 ${fmtMax(targetCm, 1)}cm（多记 ${fmtMax(diff, 1)}cm）`);
    } else if (diff < -CLOSURE_TOL_CM) {
      warns.push(`△ 段长+碎屑长合计 ${fmtMax(sumPieces + sumFragLen, 1)}cm 比进尺 ${fmtMax(targetCm, 1)}cm 少 ${fmtMax(-diff, 1)}cm（磨损/漏记，计入回次底部）`);
    }
  }

  // ===== 拆记录段：连续完整段并成一段；每个碎屑段独立 =====
  const subs: SubSeg[] = [];
  let cur: number[] = [];
  for (const iv of intervals) {
    if (iv.isPiece) {
      cur.push(iv.lengthCm);
      continue;
    }
    if (cur.length > 0) {
      subs.push({ pieces: cur, fragCount: null, fragLengthCm: null, thicknessCm: cur.reduce((s, p) => s + p, 0) });
      cur = [];
    }
    subs.push({ pieces: [], fragCount: iv.count, fragLengthCm: iv.lengthCm, thicknessCm: iv.lengthCm });
  }
  if (cur.length > 0) subs.push({ pieces: cur, fragCount: null, fragLengthCm: null, thicknessCm: cur.reduce((s, p) => s + p, 0) });
  if (subs.length === 0) subs.push({ pieces: [], fragCount: null, fragLengthCm: null, thicknessCm: targetCm });
  if (runLenPositive && subs.length > 0) {
    // 深度链严格等于进尺：差额计入回次底部
    subs[subs.length - 1].thicknessCm += targetCm - subs.reduce((s, x) => s + x.thicknessCm, 0);
  }

  let d = depthCursor;
  for (let i = 0; i < subs.length; i++) {
    const sub = subs[i];
    const row = newImportRow(firstCoreRow > 0 ? firstCoreRow : run.row);
    row.runNo = run.runNo;
    row.runLengthM = Math.round((sub.thicknessCm / 100) * 10000) / 10000;
    row.runTotalLengthM = run.runLengthM;
    row.fragCount = sub.fragCount;
    row.fragLengthCm = sub.fragLengthCm;
    row.runRowCount = subs.length;
    row.runPartIndex = i + 1;
    row.pieces.push(...sub.pieces);

    const sn = row.pieces.length + (sub.fragCount ?? 0);
    row.totalPieces = sn;
    row.jointCount = Math.max(0, sn - 1);
    const thkCm = sub.thicknessCm;
    // 小数位与判定口径一致：RQD、平均间距均保留 2 位
    row.rqd = thkCm > 0 ? Math.round((row.totalLengthCm / thkCm) * 100 * 100) / 100 : 0;
    row.avgSpacingCm = sn > 0 && thkCm > 0 ? Math.round((thkCm / sn) * 100) / 100 : 0;
    row.depthStart = Math.round(d * 1000) / 1000;
    row.depthEnd = Math.round((d + (row.runLengthM as number)) * 1000) / 1000;
    d += row.runLengthM as number;
    row.level = judgeIntegrity(row.avgSpacingCm, config.integrityLevelCriteria);

    if (row.avgSpacingCm > 500 && errors.length === 0) {
      row.messages.push('平均间距 >500cm，疑似长度按 cm 误填（模板单位米）');
    }

    row.messages.push(...errors);
    row.messages.push(...warns);
    row.status = errors.length > 0 ? 'blocked'
      : warns.length > 0 || row.messages.length > 0 ? 'warning'
      : 'ok';

    preview.rows.push(row);
  }
}

// ---------------------------------------------------------------------------
// 归并：记录段 → 完整性分段
// ---------------------------------------------------------------------------

/**
 * 把参与入库的记录段归并成完整性分段，并回填每行的段号。
 * 归并只在"将入库的行"之间进行：被排除/阻断的行留下深度空洞，天然成为分界。
 * 预览与导入各调用一次（导入时深度锚点可能已改，需按新深度重算）。
 */
export function rebuildSegments(
  preview: ImportPreview,
  groupable: readonly ImportRow[],
  config: RockSpecificationConfig,
): void {
  preview.segments = [];
  for (const row of preview.rows) row.segmentNo = 0;

  const thresholds = config.thresholds;

  // 一个回次拆出的多个子段共用 rowNumber，归并回填改用列表序号做键
  const sources = groupable.filter((r) => (r.runLengthM ?? 0) > 0);
  const byKey = new Map<number, ImportRow>();
  const inputs: RockMassRunInput[] = [];
  for (let i = 0; i < sources.length; i++) {
    const r = sources[i];
    byKey.set(i, r);
    inputs.push({
      key: i,
      depthStart: r.depthStart,
      depthEnd: r.depthEnd,
      totalPieces: r.totalPieces,
      totalLengthCm: r.totalLengthCm,
      rockTypeKey: 0, // v10：岩质在软件内录入，导入阶段不参与分界
    });
  }

  const built = thresholds.mergeRunsIntoRockSegments
    ? buildRockSegments(inputs, thresholds.rockSegmentMinThicknessM, thresholds.rockSegmentSpacingToleranceRatio)
    : buildOnePerRun(inputs);

  let no = 0;
  for (const seg of built) {
    no++;
    const model: ImportSegment = {
      no,
      depthStart: seg.depthStart,
      depthEnd: seg.depthEnd,
      thicknessM: seg.thicknessM,
      runCount: seg.runCount,
      totalPieces: seg.totalPieces,
      spacingCm: seg.spacingCm,
      rqd: seg.rqd,
      level: judgeIntegrity(seg.spacingCm, config.integrityLevelCriteria),
    };
    for (const run of seg.runs) {
      const src = byKey.get(run.key);
      if (src != null) src.segmentNo = no;
    }
    preview.segments.push(model);
  }

  // 回次数：未拆段的行各计 1，拆段的多行按其回次号只计 1
  const runCount = sources.filter((r) => r.runRowCount === 1).length
    + new Set(sources.filter((r) => r.runRowCount > 1).map((r) => r.runNo)).size;

  preview.segmentNote = !thresholds.mergeRunsIntoRockSegments
    ? '未启用归并（一个记录段 = 一个完整性分段）'
    : `${runCount} 个回次 / ${sources.length} 个记录段 → ${preview.segments.length} 个完整性分段` +
      `（最小段厚 ${fmtMax(thresholds.rockSegmentMinThicknessM, 2)}m，` +
      `间距容差 ${Math.round(thresholds.rockSegmentSpacingToleranceRatio * 100)}%）`;
}

/**
 * 深度锚点重排：导入对话框设定起始深度后，按最终深度重算各行深度
 * （预览锚点为 0）。调用后需再调 rebuildSegments 按新深度重算归并。
 */
export function applyDepthAnchor(preview: ImportPreview, startDepth: number): void {
  let cursor = startDepth;
  for (const row of preview.rows) {
    if ((row.runLengthM ?? 0) <= 0) continue;
    row.depthStart = Math.round(cursor * 1000) / 1000;
    row.depthEnd = Math.round((cursor + (row.runLengthM as number)) * 1000) / 1000;
    cursor += row.runLengthM as number;
  }
}

// ---------------------------------------------------------------------------
// 展示文本（预览表格 / 报告共用，对齐 1.x 显示格式）
// ---------------------------------------------------------------------------

export function runNoText(row: ImportRow): string {
  return row.runNo == null ? '—'
    : row.runRowCount > 1 ? `${row.runNo}-${row.runPartIndex}`
    : String(row.runNo);
}

export function fragText(row: ImportRow): string {
  return row.fragCount == null ? '—'
    : row.fragLengthCm == null ? String(row.fragCount)
    : `${fmtMax(row.fragLengthCm, 2)}/${row.fragCount}`;
}

export function depthText(row: Pick<ImportRow, 'depthStart' | 'depthEnd'>): string {
  return `${fmtFix(row.depthStart, 2)} → ${fmtFix(row.depthEnd, 2)}`;
}

export function spacingText(spacingCm: number): string {
  return spacingCm > 0 ? fmtFix(spacingCm, 2) : '—';
}

export function statusText(status: ImportRowStatus): string {
  return status === 'ok' ? '正常' : status === 'warning' ? '警告' : '阻断';
}
