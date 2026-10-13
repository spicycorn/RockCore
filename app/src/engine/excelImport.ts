/**
 * 岩心回次统计 Excel 导入引擎（模板 v12：一行一回次，岩心段按浅→深同列同序）。
 *
 * 录入格式（全部长度单位为米，软件内部换算成 cm 判级）：
 *   「基本信息」：B1 = 钻孔编号（选填，导入对话框展示并核对）；
 *   「岩心记录」：回次号 | 回次进尺(m) | 岩心段1..岩心段20 | RQD(%) | 岩质 | 岩体结构类型 | 坚硬程度
 *     - 一个回次一行，行序 = 由浅到深的钻进顺序；
 *     - 岩心段格按由浅到深依次填，一段一格（空格跳过，保持顺序连贯）：
 *         纯数字（如 1.2）   = ≥10cm 的整段，进 RQD 分子，也进段数 n；
 *         「长度,块数」（如 0.08,3）= <10cm 破碎带（碎块/碎屑），只进 n，不进 RQD；
 *     - RQD(%) 列为模板公式自动计算（仅现场参考），软件导入一律重算；
 *     - 后三列选填（现场顺手录），只认模板下拉的中文标签。
 * v12 双行表头：第 1 行列名、第 2 行黄底填写提示（按提示原文整行跳过），数据从第 3 行起。
 * 均一性、地下水、洞轴夹角仍是软件内判级参数，不在 Excel 中填。
 *
 * 数据链（三层，顺序不可颠倒）：
 *   回次记录段（每个碎屑格独立成段，连续完整格并成一段）
 *     → 岩体段（评价单元：相邻、平均间距相近的记录段归并，见 segments.ts）
 *       → 完整性分段（对归并段判级后入库）
 *
 * 铁律：
 *   · 全部派生指标由原始数据重算，不信任 Excel 公式列（RQD 列直接忽略）；
 *     n = 完整格数 + Σ碎屑块数；S = 进尺×100÷n (cm)；RQD = Σ完整段长÷进尺×100；
 *   · 判级只按平均间距 S 查表 F.0.4——「结构面发育组数」是地质描述量，回次取芯算不出它；
 *   · 先归并、后判级：归并只用原始量（进尺/段数/段长/岩质），不用判级结果；
 *   · 闭合校验按回次自动汇总：Σ段长+Σ碎屑长 = 进尺×100（±1cm），当场暴露漏记；
 *   · 选填三列只认模板下拉的中文标签，认不出即阻断（宁停不错）；
 *   · 旧版模板（v9 宽表、v10/v11 长表）整表拒收，指引重新下载。
 *
 * 本模块是纯函数（字节 → 预览），不触碰数据库；入库由 db 层完成。
 */

import ExcelJS from 'exceljs';
import {
  hardRockOnlyStructures,
  IntegrityLevel,
  RockHardnessLevel,
  RockStructureType,
  RockType,
  softRockOnlyStructures,
  parseHardnessLabel,
  parseRockTypeLabel,
  parseStructureLabel,
  rockStructureLabel,
  rockTypeLabel,
} from './enums';
import { judgeIntegrity } from './integrity';
import { buildOnePerRun, buildRockSegments, type RockMassRunInput } from './segments';
import type { RockSpecificationConfig } from './config';

// ---------------------------------------------------------------------------
// 预览数据结构
// ---------------------------------------------------------------------------

export type ImportRowStatus = 'ok' | 'warning' | 'blocked';

/** 预览单行 = 一个记录段（每个碎屑格独立成段；连续完整格并成一段） */
export interface ImportRow {
  /** 「岩心记录」中该回次的行号（被阻断的脏行 = 其自身行号） */
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

  /** 选填列：岩质（回次级，作用于本回次全部记录段） */
  rockType: RockType;
  /** 选填列：岩体结构类型 */
  structureType: RockStructureType;
  /** 选填列：坚硬程度 */
  hardness: RockHardnessLevel;
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
  /** 组内首个已填的岩质（都没填 = NotSet），入库后作为 F.0.2 初值 */
  rockType: RockType;
  structureType: RockStructureType;
  hardness: RockHardnessLevel;
}

export interface ImportPreview {
  error: string | null;
  rows: ImportRow[];
  /** 归并后的完整性分段（= 实际写入数据库的内容） */
  segments: ImportSegment[];
  /** 归并口径说明，直接展示在导入对话框底部 */
  segmentNote: string;
  /** 「基本信息」B1 的钻孔编号（无表/未填 = null），用于与当前选中钻孔核对 */
  boreholeNo: string | null;
}

// ---------------------------------------------------------------------------
// 常量（模板 v12 列序；提示行文本与 make-template.mjs 逐字一致）
// ---------------------------------------------------------------------------

const SHEET_MAIN = '岩心记录';
const SHEET_INFO = '基本信息';
const SHEET_LEGACY = '数据录入'; // v9 及更早
const SHEET_V1011_RUN = '回次表'; // v10/v11
const SHEET_V1011_CORE = '岩心表'; // v10/v11

const COL_RUN_NO = 1;
const COL_RUN_LEN = 2;
/** 岩心段格：C..V 共 20 格，按由浅到深同列同序 */
const FIRST_PIECE_COL = 3;
const PIECE_COL_COUNT = 20;
const LAST_PIECE_COL = FIRST_PIECE_COL + PIECE_COL_COUNT - 1; // V = 22
// 23 = RQD(%)：模板公式列，引擎忽略（导入一律重算）
const COL_ROCK = 24;
const COL_STRUCT = 25;
const COL_HARD = 26;

const COL_INFO_NO = 2; // 「基本信息」B1 = 钻孔编号

/** 第 2 行黄底提示行的 A 列原文：整行按提示跳过（v12 双行表头） */
export const HINT_MAIN_A = '整数，从1开始，一个回次填一行，按钻进顺序由浅到深';

/** 完整段下限（米）：10cm。模板单位为米，解析后统一换算成 cm 判级。 */
const MIN_PIECE_M = 0.1;

/** 深度闭合容差（cm）：1mm 级量测误差。 */
const CLOSURE_TOL_CM = 1.0;

/** 碎屑格分隔符：半角/全角逗号都认 */
const FRAG_SPLIT = /[,，]/;

/** v9 及更早宽表拒收文案 */
const LEGACY_REJECT =
  '这是旧版模板（v9 及更早：一回次三行 × L1..L30 宽表）。当前模板（v12）改为一行一回次：' +
  '「岩心记录」表每回次一行，岩心段1..20 按由浅到深依次填——整段只填长度（米，≥0.1m），' +
  '不足10厘米的破碎带填「长度,块数」（如 0.08,3），RQD 列自动计算。' +
  '请用「下载导入模板」重新下载，把数据按新格式整理后再导入';

/** v10/v11 长表拒收文案 */
const V1011_REJECT =
  '这是上一版模板（v10/v11：「回次表」+「岩心表」长表，一段一行）。当前模板（v12）改为一行一回次：' +
  '「岩心记录」表每回次一行，岩心段1..20 按由浅到深依次填——整段只填长度（米，≥0.1m），' +
  '不足10厘米的破碎带填「长度,块数」（如 0.08,3），RQD 列自动计算。' +
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

/** 「岩心记录」的一行（一个回次） */
interface RunEntry {
  row: number;
  runNo: number | null;
  runLengthM: number | null;
  /** 非空的岩心段格（按列序 = 由浅到深） */
  cells: Array<{ col: number; num: number | null; text: string }>;
  /** 选填三列的原始文本（解析与报错在 emitRun 逐回次做） */
  rockText: string;
  structText: string;
  hardText: string;
}

/** 回次自动拆出的一个记录段：完整段（pieces）或碎屑段（frag*）。 */
interface SubSeg {
  pieces: number[];
  fragCount: number | null;
  fragLengthCm: number | null;
  thicknessCm: number;
  rockType: RockType;
  structureType: RockStructureType;
  hardness: RockHardnessLevel;
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
    rockType: RockType.NotSet,
    structureType: RockStructureType.NotSet,
    hardness: RockHardnessLevel.NotSet,
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
  const preview: ImportPreview = { error: null, rows: [], segments: [], segmentNote: '', boreholeNo: null };

  const workbook = new ExcelJS.Workbook();
  try {
    await workbook.xlsx.load(buffer);
  } catch (ex) {
    preview.error = `无法打开 Excel 文件：${ex instanceof Error ? ex.message : String(ex)}`;
    return preview;
  }

  const findSheet = (name: string) => workbook.worksheets.find((ws) => ws.name.trim() === name);

  // 旧版模板整表拒收：旧格式读入 v12 会静默丢数据，必须挡在解析前。
  // v12 模板绝不含「数据录入」（v9）或「回次表」「岩心表」（v10/v11）——出现即旧表。
  if (findSheet(SHEET_LEGACY) != null) {
    preview.error = LEGACY_REJECT;
    return preview;
  }
  if (findSheet(SHEET_V1011_RUN) != null || findSheet(SHEET_V1011_CORE) != null) {
    preview.error = V1011_REJECT;
    return preview;
  }
  const wsMain = findSheet(SHEET_MAIN);
  if (wsMain == null) {
    preview.error = `未找到「${SHEET_MAIN}」工作表，请使用模板文件填写（旧版模板请先在软件中重新下载）`;
    return preview;
  }

  // 表头识别：缺关键表头 = 版本不符/表被改坏，拒收而不是逐行报错。
  const headerA = cellRawText(wsMain.getCell(1, COL_RUN_NO).value);
  const headerB = cellRawText(wsMain.getCell(1, COL_RUN_LEN).value);
  const headerC = cellRawText(wsMain.getCell(1, FIRST_PIECE_COL).value);
  if (!headerA.includes('回次号') || !headerB.includes('进尺') || !headerC.includes('岩心段')) {
    preview.error = `「${SHEET_MAIN}」表头不符（应为：回次号 | 回次进尺(m) | 岩心段1..岩心段20 | RQD(%) | …），请使用模板文件填写`;
    return preview;
  }

  // 选填三列（X/Y/Z）：整列留空 = 不填；出现任一列名则三列必须齐全且对位。
  const optHeader = [COL_ROCK, COL_STRUCT, COL_HARD]
    .map((c) => cellRawText(wsMain.getCell(1, c).value));
  if (optHeader.some((t) => t.length > 0)) {
    if (!optHeader[0].includes('岩质') || !optHeader[1].includes('结构') || !optHeader[2].includes('坚硬')) {
      preview.error = `「${SHEET_MAIN}」选填列表头不符（RQD 列后三列应为：岩质 | 岩体结构类型 | 坚硬程度；不用可整列留空，但不要改列名或调换顺序）`;
      return preview;
    }
  }

  // 「基本信息」B1 = 钻孔编号（选填）：仅用于导入对话框核对，不参与计算。
  const wsInfo = findSheet(SHEET_INFO);
  if (wsInfo != null) {
    preview.boreholeNo = cellRawText(wsInfo.getCell(1, COL_INFO_NO).value) || null;
  }

  // ---- 读「岩心记录」：行序 = 深度序 ----
  const runs: RunEntry[] = [];
  const runByNo = new Map<number, RunEntry>();
  for (let r = 2; r <= wsMain.actualRowCount; r++) {
    if (cellRawText(wsMain.getCell(r, COL_RUN_NO).value) === HINT_MAIN_A) continue; // v12 提示行
    const runNo = readIntCell(wsMain.getCell(r, COL_RUN_NO));
    const len = readNumCell(wsMain.getCell(r, COL_RUN_LEN));
    const cells: RunEntry['cells'] = [];
    for (let c = FIRST_PIECE_COL; c <= LAST_PIECE_COL; c++) {
      const cell = readNumCell(wsMain.getCell(r, c));
      if (cell.num == null && cell.text.length === 0) continue; // 空格跳过
      cells.push({ col: c, num: cell.num, text: cell.text });
    }
    const rockText = cellRawText(wsMain.getCell(r, COL_ROCK).value);
    const structText = cellRawText(wsMain.getCell(r, COL_STRUCT).value);
    const hardText = cellRawText(wsMain.getCell(r, COL_HARD).value);
    if (runNo == null && len.num == null && len.text.length === 0
      && cells.length === 0 && rockText.length === 0 && structText.length === 0 && hardText.length === 0) {
      continue; // 整行空白
    }
    const entry: RunEntry = { row: r, runNo, runLengthM: len.num, cells, rockText, structText, hardText };
    runs.push(entry);
    if (runNo != null) {
      if (runByNo.has(runNo)) {
        const bad = newImportRow(r);
        bad.runNo = runNo;
        bad.messages.push(`回次号 ${runNo} 在「${SHEET_MAIN}」中重复`);
        bad.status = 'blocked';
        preview.rows.push(bad);
        continue;
      }
      runByNo.set(runNo, entry);
    }
  }

  // ---- 逐回次校验 + 拆记录段 + 重算指标（行序 = 深度序）----
  let depthCursor = 0;
  for (const run of runs) {
    emitRun(preview, run, config, depthCursor);
    if (run.runLengthM != null && run.runLengthM > 0) depthCursor += run.runLengthM;
  }

  if (preview.rows.length === 0) {
    preview.error = `「${SHEET_MAIN}」中没有数据行`;
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
  config: RockSpecificationConfig,
  depthCursor: number,
): void {
  const errors: string[] = [];
  const warns: string[] = [];
  const runLabel = `回次 ${run.runNo ?? `?(第${run.row}行)`}`;

  if (run.runNo == null) errors.push(`「${SHEET_MAIN}」第 ${run.row} 行缺少回次号`);
  if (run.runLengthM == null) errors.push(`${runLabel} 缺少进尺（B 列）`);
  else if (run.runLengthM <= 0) errors.push(`${runLabel} 进尺必须大于 0`);

  // ===== 选填三列（回次级）：只认模板下拉的中文标签，认不出即阻断（宁停不错）=====
  const rockType = parseRockTypeLabel(run.rockText);
  const structureType = parseStructureLabel(run.structText);
  const hardness = parseHardnessLabel(run.hardText);
  if (run.rockText.length > 0 && rockType == null) {
    errors.push(`${runLabel}：岩质「${run.rockText}」无法识别（下拉选：硬质岩/软质岩）`);
  }
  if (run.structText.length > 0 && structureType == null) {
    errors.push(`${runLabel}：岩体结构类型「${run.structText}」无法识别（选岩质后从下拉选择）`);
  }
  if (run.hardText.length > 0 && hardness == null) {
    errors.push(`${runLabel}：坚硬程度「${run.hardText}」无法识别（下拉选：坚硬岩/中硬岩/较软岩/软岩）`);
  }
  if (rockType === RockType.HardRock && structureType != null
    && softRockOnlyStructures.has(structureType)) {
    errors.push(`${runLabel}：结构「${rockStructureLabel[structureType]}」属软质岩，与岩质「${rockTypeLabel[RockType.HardRock]}」矛盾，请核对下拉选择`);
  }
  if (rockType === RockType.SoftRock && structureType != null
    && hardRockOnlyStructures.has(structureType)) {
    errors.push(`${runLabel}：结构「${rockStructureLabel[structureType]}」属硬质岩，与岩质「${rockTypeLabel[RockType.SoftRock]}」矛盾，请核对下拉选择`);
  }
  const params = {
    rockType: rockType ?? RockType.NotSet,
    structureType: structureType ?? RockStructureType.NotSet,
    hardness: hardness ?? RockHardnessLevel.NotSet,
  };

  // ===== 逐格解析岩心段：纯数字 = 完整段；「长度,块数」= 碎屑带 =====
  const intervals: Array<{
    isPiece: boolean; lengthCm: number; count: number; label: string;
    rockType: RockType; structureType: RockStructureType; hardness: RockHardnessLevel;
  }> = [];
  let sumPieces = 0;
  let sumFragLen = 0;
  let fragTotal = 0;
  let pieceCount = 0;
  let prevCol = FIRST_PIECE_COL - 1;
  for (const cell of run.cells) {
    const label = `${runLabel} 岩心段${cell.col - FIRST_PIECE_COL + 1}`;
    if (cell.col > prevCol + 1) {
      warns.push(`${runLabel}：岩心段${prevCol + 1 - FIRST_PIECE_COL + 1} 为空但后面还有段，其后各段按顺序连续处理`);
    }
    prevCol = cell.col;

    if (cell.num != null) {
      // 完整段（纯数字）
      if (cell.num <= 0) {
        errors.push(`${label}：段长必须大于 0`);
        continue;
      }
      if (cell.num < MIN_PIECE_M) {
        errors.push(`${label}：段长 ${fmtMax(cell.num, 3)}m <0.1m，不足10厘米请写成「长度,块数」（如 0.08,3）`);
        continue;
      }
      const lenCm = Math.round(cell.num * 100 * 100) / 100;
      intervals.push({ isPiece: true, lengthCm: lenCm, count: 1, label, ...params });
      sumPieces += lenCm;
      pieceCount++;
      continue;
    }

    // 碎屑带（文本「长度,块数」）
    const parts = cell.text.split(FRAG_SPLIT);
    if (parts.length < 2) {
      errors.push(`${label}：无法识别「${cell.text}」——整段填数字（≥0.1m），破碎带填 长度,块数（如 0.08,3）`);
      continue;
    }
    if (parts.length > 2) {
      errors.push(`${label}：「${cell.text}」格式不对，应为 长度,块数（如 0.08,3），只能有一个逗号`);
      continue;
    }
    const lenNum = tryParseDouble(parts[0]);
    if (lenNum == null) {
      errors.push(`${label}：碎屑长度只能填数字（米），读到「${parts[0]}」`);
      continue;
    }
    const lenCm = Math.round(lenNum * 100 * 100) / 100;
    if (lenCm <= 0) {
      errors.push(`${label}：碎屑长度必须大于 0`);
      continue;
    }
    const cntText = parts[1].trim();
    let cnt: number;
    if (cntText.length === 0) {
      cnt = 1;
      warns.push(`${label}：碎屑长度有值但块数未填，按 1 块计`);
    } else {
      const cntNum = tryParseDouble(cntText);
      if (cntNum == null) {
        errors.push(`${label}：块数只能填整数，读到「${cntText}」`);
        continue;
      }
      if (Math.abs(cntNum - Math.round(cntNum)) > 1e-9) {
        errors.push(`${label}：块数 ${fmtMax(cntNum, 2)} 不是整数`);
        continue;
      }
      cnt = Math.round(cntNum);
      if (cnt < 1) {
        errors.push(`${label}：块数必须 ≥1`);
        continue;
      }
    }
    const avg = lenCm / cnt;
    if (avg >= 10) warns.push(`${label}：碎屑平均 ${fmtFix(avg, 1)}cm ≥10cm，与「<10cm」矛盾，请核对块数`);
    else if (avg < 0.5) warns.push(`${label}：碎屑平均 ${fmtFix(avg, 2)}cm <0.5cm，疑似把同一破碎带拆成了多格`);
    intervals.push({ isPiece: false, lengthCm: lenCm, count: cnt, label, ...params });
    sumFragLen += lenCm;
    fragTotal += cnt;
  }

  const n = pieceCount + fragTotal;
  const runLenPositive = run.runLengthM != null && run.runLengthM > 0;
  if (runLenPositive && n === 0 && errors.length === 0) {
    errors.push(`${runLabel} 没有任何岩心记录（岩心段1..${PIECE_COL_COUNT} 全空），无法判级`);
  }

  // ===== 深度闭合校验：Σ段长+Σ碎屑长 = 进尺×100（±1cm）=====
  const targetCm = runLenPositive ? (run.runLengthM as number) * 100 : 0;
  const diff = sumPieces + sumFragLen - targetCm;
  if (runLenPositive && errors.length === 0 && run.cells.length > 0) {
    if (diff > CLOSURE_TOL_CM) {
      errors.push(`段长+碎屑长合计 ${fmtMax(sumPieces + sumFragLen, 1)}cm 超过进尺 ${fmtMax(targetCm, 1)}cm（多记 ${fmtMax(diff, 1)}cm）`);
    } else if (diff < -CLOSURE_TOL_CM) {
      warns.push(`△ 段长+碎屑长合计 ${fmtMax(sumPieces + sumFragLen, 1)}cm 比进尺 ${fmtMax(targetCm, 1)}cm 少 ${fmtMax(-diff, 1)}cm（磨损/漏记，计入回次底部）`);
    }
  }

  // ===== 拆记录段：连续完整段并成一段；每个碎屑段独立 =====
  // v12 选填参数是回次级，回次内不存在并段冲突；跨回次冲突由 rebuildSegments 取先填值。
  const subs: SubSeg[] = [];
  let cur: number[] = [];
  for (const iv of intervals) {
    if (iv.isPiece) {
      cur.push(iv.lengthCm);
      continue;
    }
    if (cur.length > 0) {
      subs.push({ pieces: cur, fragCount: null, fragLengthCm: null, thicknessCm: cur.reduce((s, p) => s + p, 0), ...params });
      cur = [];
    }
    subs.push({
      pieces: [], fragCount: iv.count, fragLengthCm: iv.lengthCm, thicknessCm: iv.lengthCm, ...params,
    });
  }
  if (cur.length > 0) subs.push({ pieces: cur, fragCount: null, fragLengthCm: null, thicknessCm: cur.reduce((s, p) => s + p, 0), ...params });
  if (subs.length === 0) {
    subs.push({
      pieces: [], fragCount: null, fragLengthCm: null, thicknessCm: targetCm,
      rockType: RockType.NotSet, structureType: RockStructureType.NotSet, hardness: RockHardnessLevel.NotSet,
    });
  }
  if (runLenPositive && subs.length > 0) {
    // 深度链严格等于进尺：差额计入回次底部
    subs[subs.length - 1].thicknessCm += targetCm - subs.reduce((s, x) => s + x.thicknessCm, 0);
  }

  let d = depthCursor;
  for (let i = 0; i < subs.length; i++) {
    const sub = subs[i];
    const row = newImportRow(run.row);
    row.runNo = run.runNo;
    row.runLengthM = Math.round((sub.thicknessCm / 100) * 10000) / 10000;
    row.runTotalLengthM = run.runLengthM;
    row.fragCount = sub.fragCount;
    row.fragLengthCm = sub.fragLengthCm;
    row.runRowCount = subs.length;
    row.runPartIndex = i + 1;
    row.pieces.push(...sub.pieces);
    row.rockType = sub.rockType;
    row.structureType = sub.structureType;
    row.hardness = sub.hardness;

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
    // 碎屑平均块径（cm）：仅「长度,块数」记录的碎屑参与；无碎屑 → NaN
    const fragMean = (row.fragCount ?? 0) > 0 && row.fragLengthCm != null
      ? row.fragLengthCm / (row.fragCount as number)
      : Number.NaN;
    row.level = judgeIntegrity(row.avgSpacingCm, config.integrityLevelCriteria, fragMean,
      config.thresholds.fragMeanSizeThresholdCm);

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
      fragCount: r.fragCount ?? 0,
      fragLengthCm: r.fragLengthCm ?? 0,
      // Excel 选填列带来的岩质参与强制分界（未填=0 不设限）
      rockTypeKey: r.rockType,
    });
  }

  const built = thresholds.mergeRunsIntoRockSegments
    ? buildRockSegments(inputs, thresholds.rockSegmentMinThicknessM, thresholds.rockSegmentSpacingToleranceRatio)
    : buildOnePerRun(inputs);

  let no = 0;
  for (const seg of built) {
    no++;
    const srcRows = seg.runs
      .map((run) => byKey.get(run.key))
      .filter((r): r is ImportRow => r != null);
    const model: ImportSegment = {
      no,
      depthStart: seg.depthStart,
      depthEnd: seg.depthEnd,
      thicknessM: seg.thicknessM,
      runCount: seg.runCount,
      totalPieces: seg.totalPieces,
      spacingCm: seg.spacingCm,
      rqd: seg.rqd,
      level: judgeIntegrity(seg.spacingCm, config.integrityLevelCriteria, seg.fragMeanSizeCm,
        config.thresholds.fragMeanSizeThresholdCm),
      rockType: srcRows.find((r) => r.rockType !== RockType.NotSet)?.rockType ?? RockType.NotSet,
      structureType: srcRows.find((r) => r.structureType !== RockStructureType.NotSet)?.structureType
        ?? RockStructureType.NotSet,
      hardness: srcRows.find((r) => r.hardness !== RockHardnessLevel.NotSet)?.hardness
        ?? RockHardnessLevel.NotSet,
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
