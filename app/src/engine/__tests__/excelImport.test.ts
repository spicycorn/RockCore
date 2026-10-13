/**
 * 导入引擎端到端黄金用例（模板 v12 一行一回次）：用 exceljs 现场构造真实 xlsx 字节流 → previewWorkbook，
 * 校验解析/拆段/闭合/判级/归并全链路（无 mock）。
 *
 * 「岩心记录」：A=回次号 B=回次进尺(m) C..V=岩心段1..20（纯数字=完整段；"长度,块数"=碎屑带）
 * X=岩质 Y=岩体结构类型 Z=坚硬程度（选填，回次级），第 2 行黄底提示行。
 * 测试仍用 (runs, cores) 两段式描述数据，由 buildWorkbook 换算成 v12 单表行。
 */
import { describe, expect, it } from 'vitest';
import ExcelJS from 'exceljs';
import {
  applyDepthAnchor, HINT_MAIN_A, previewWorkbook, rebuildSegments,
} from '../excelImport';
import { defaultConfig } from '../config';
import { IntegrityLevel, RockHardnessLevel, RockStructureType, RockType } from '../enums';

const config = defaultConfig();

function toAB(b: ArrayBuffer | Uint8Array): ArrayBuffer {
  if (b instanceof ArrayBuffer) return b;
  return new Uint8Array(b).buffer;
}

const C = '完整';
const F = '碎屑';

/**
 * 构造 v12「岩心记录」工作簿并返回字节。
 * runs = [回次号, 进尺(m)]；cores = [回次号, 类型(完整|碎屑), 长度(m), 块数?, 岩质?, 结构?, 坚硬?]
 * ——helper 把同一回次的 core 行按序换算成 C..V 岩心段格（完整=数字，碎屑="长度,块数"），
 *   选填三列取该回次首个非空值写到 X/Y/Z；回次号缺失或无对应 run 的 core 行换算成独立脏行。
 * hint=true 时第 2 行为 v12 黄底提示行。
 */
async function buildWorkbook(
  runs: (number | null | string)[][],
  cores: (number | null | string)[][],
  opts: {
    legacy?: boolean; v1011?: boolean; mainHeader?: string[]; optHeader?: string[];
    hint?: boolean; infoNo?: string;
  } = {},
): Promise<ArrayBuffer> {
  const wb = new ExcelJS.Workbook();
  if (opts.infoNo != null) {
    const wsInfo = wb.addWorksheet('基本信息');
    wsInfo.getCell(1, 1).value = '钻孔编号';
    wsInfo.getCell(1, 2).value = opts.infoNo;
  }
  if (opts.legacy) {
    const wsOld = wb.addWorksheet('数据录入');
    wsOld.getCell(1, 1).value = '回次号';
    wsOld.getCell(1, 2).value = '行型';
  }
  if (opts.v1011) {
    wb.addWorksheet('回次表').getCell(1, 1).value = '回次号';
    wb.addWorksheet('岩心表').getCell(1, 1).value = '回次号';
  }

  const ws = wb.addWorksheet('岩心记录');
  const header = opts.mainHeader
    ?? ['回次号', '回次进尺(m)',
      ...Array.from({ length: 20 }, (_, i) => `岩心段${i + 1}`), 'RQD(%)'];
  header.forEach((h, ci) => { ws.getCell(1, ci + 1).value = h; });
  if (opts.optHeader) opts.optHeader.forEach((h, i) => { ws.getCell(1, 24 + i).value = h; });

  const base = opts.hint ? 3 : 2;
  if (opts.hint) ws.getCell(2, 1).value = HINT_MAIN_A;

  interface RowSpec { runNo: number | null; advance: number | null; cells: (number | string)[]; opt?: (string | null | undefined)[] }
  const rowSpecs: RowSpec[] = [];
  const specByRun = new Map<number, RowSpec>();
  for (const runRow of runs) {
    const spec: RowSpec = {
      runNo: runRow[0] as number | null,
      advance: (runRow[1] as number | null) ?? null,
      cells: [],
    };
    rowSpecs.push(spec);
    if (spec.runNo != null && !specByRun.has(spec.runNo)) specByRun.set(spec.runNo, spec);
  }
  for (const core of cores) {
    const runNo = core[0] as number | null;
    const type = core[1] as string;
    const len = core[2] as number | string;
    const cnt = (core[3] as number | null | undefined) ?? null;
    // 碎屑：len 已是「长度,块数」文本串时原样写入（构造脏数据用），否则拼 长度,块数
    const cell = type !== F ? len
      : typeof len === 'string' && /[,，]/.test(len) ? len
      : `${len},${cnt ?? ''}`;
    let spec = runNo != null ? specByRun.get(runNo) : undefined;
    if (spec == null) {
      // 回次号缺失/无对应回次 → 独立脏行
      spec = { runNo, advance: null, cells: [] };
      rowSpecs.push(spec);
      if (runNo != null) specByRun.set(runNo, spec);
    }
    spec.cells.push(cell);
    const opt = [core[4], core[5], core[6]] as (string | null | undefined)[];
    if (opt.some((v) => v != null)) {
      spec.opt ??= [null, null, null];
      opt.forEach((v, i) => { if (v != null && spec!.opt![i] == null) spec!.opt![i] = v as string; });
    }
  }

  rowSpecs.forEach((spec, ri) => {
    const r = ri + base;
    if (spec.runNo != null) ws.getCell(r, 1).value = spec.runNo;
    if (spec.advance != null) ws.getCell(r, 2).value = spec.advance;
    spec.cells.forEach((v, ci) => { ws.getCell(r, 3 + ci).value = v as number; });
    spec.opt?.forEach((v, i) => { if (v != null) ws.getCell(r, 24 + i).value = v; });
  });

  return toAB(await wb.xlsx.writeBuffer());
}

describe('previewWorkbook：解析与拆段', () => {
  it('连续完整格并成一个记录段，指标按回次重算', async () => {
    const buf = await buildWorkbook([[1, 2.0]], [[1, C, 1.2], [1, C, 0.8]]);
    const p = await previewWorkbook(buf, config);
    expect(p.error).toBeNull();
    expect(p.rows).toHaveLength(1);
    const row = p.rows[0];
    expect(row.pieces).toEqual([120, 80]);
    expect(row.totalPieces).toBe(2);
    expect(row.avgSpacingCm).toBe(100);
    expect(row.rqd).toBe(100);
    expect(row.level).toBe(IntegrityLevel.RelativelyIntact); // S=100 ∈ (50,100] 较完整档
    expect(row.status).toBe('ok');
    expect(row.depthStart).toBe(0);
    expect(row.depthEnd).toBe(2);
  });

  it('碎屑「长度,块数」独立成段且保持顺序，少记差额计入回次底部', async () => {
    const buf = await buildWorkbook([[1, 2.0]], [[1, C, 0.8], [1, C, 0.7], [1, F, 0.3, 5]]);
    const p = await previewWorkbook(buf, config);
    expect(p.rows).toHaveLength(2);
    const [a, b] = p.rows;
    expect(a.pieces).toEqual([80, 70]);
    expect(a.runLengthM).toBe(1.5);
    expect(b.fragCount).toBe(5);
    expect(b.fragLengthCm).toBe(30);
    expect(b.runLengthM).toBe(0.5); // 0.3 + 0.2 少记差额
    expect(b.messages.some((m) => m.includes('少 20cm'))).toBe(true);
    expect(b.status).toBe('warning');
    expect(b.depthStart).toBe(1.5);
    expect(b.depthEnd).toBe(2);
  });

  it('碎屑带在浅部、整段在深部：拆段顺序照实反映', async () => {
    const buf = await buildWorkbook([[1, 1.5]], [[1, F, 0.05, 6], [1, C, 1.4]]);
    const p = await previewWorkbook(buf, config);
    expect(p.rows).toHaveLength(2);
    expect(p.rows[0].fragCount).toBe(6);
    expect(p.rows[0].depthEnd).toBe(0.05);
    expect(p.rows[1].pieces).toEqual([140]);
    expect(p.rows[1].depthStart).toBe(0.05);
  });

  it('≤10cm 间距档按碎屑平均块径分档：<3.3cm→较破碎，≥3.3cm→完整性差', async () => {
    // 块径 90/30=3cm < 3.3 → >3 组 → 较破碎（S=100/30≈3.33cm ≤10）
    const fine = await previewWorkbook(await buildWorkbook([[1, 1.0]], [[1, F, 0.9, 30]]), config);
    expect(fine.rows[0].avgSpacingCm).toBeCloseTo(3.33, 1);
    expect(fine.rows[0].level).toBe(IntegrityLevel.RelativelyBroken);
    // 块径 90/10=9cm ≥ 3.3 → 2~3 组 → 完整性差
    const coarse = await previewWorkbook(await buildWorkbook([[1, 1.0]], [[1, F, 0.9, 10]]), config);
    expect(coarse.rows[0].level).toBe(IntegrityLevel.Poor);
  });

  it('空格跳过但顺序连贯 → 警告', async () => {
    const buf = await buildWorkbook([[1, 2.0]], [[1, C, 1.0], [1, C, 1.0]]);
    // 手工把第二段挪到岩心段3（中间空一格）
    const wb = new ExcelJS.Workbook();
    await wb.xlsx.load(buf);
    const ws = wb.getWorksheet('岩心记录')!;
    ws.getCell(2, 5).value = 1.0; // 岩心段3
    ws.getCell(2, 4).value = null; // 清空岩心段2
    const p = await previewWorkbook(toAB(await wb.xlsx.writeBuffer()), config);
    expect(p.rows).toHaveLength(1);
    expect(p.rows[0].pieces).toEqual([100, 100]);
    expect(p.rows[0].status).toBe('warning');
    expect(p.rows[0].messages.some((m) => m.includes('为空但后面还有段'))).toBe(true);
  });

  it('深度链按行序累加', async () => {
    const buf = await buildWorkbook([[1, 2.0], [2, 3.0]], [[1, C, 2.0], [2, C, 3.0]]);
    const p = await previewWorkbook(buf, config);
    expect(p.rows.map((r) => [r.depthStart, r.depthEnd])).toEqual([[0, 2], [2, 5]]);
  });

  it('回次号重复 / 缺失 / 缺进尺均阻断', async () => {
    const dup = await buildWorkbook([[1, 2.0], [1, 1.0]], [[1, C, 2.0]]);
    const p1 = await previewWorkbook(dup, config);
    expect(p1.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('重复')))).toBe(true);

    const noRun = await buildWorkbook([[1, 2.0]], [[null, C, 1.0]]);
    const p2 = await previewWorkbook(noRun, config);
    expect(p2.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('缺少回次号')))).toBe(true);

    const ghost = await buildWorkbook([[1, 2.0]], [[7, C, 1.0]]);
    const p3 = await previewWorkbook(ghost, config);
    expect(p3.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('缺少进尺')))).toBe(true);
  });

  it('回次没有任何岩心记录 → 阻断', async () => {
    const buf = await buildWorkbook([[1, 2.0]], []);
    const p = await previewWorkbook(buf, config);
    expect(p.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('没有任何岩心记录')))).toBe(true);
  });
});

describe('previewWorkbook：格校验', () => {
  const one = async (cores: (number | null | string)[][]) =>
    (await previewWorkbook(await buildWorkbook([[1, 2.0]], cores), config)).rows;

  it('纯数字段长 <0.1m → 阻断并提示写成 长度,块数', async () => {
    const rows = await one([[1, C, 0.05], [1, C, 1.95]]);
    expect(rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('<0.1m') && m.includes('0.08,3')))).toBe(true);
  });

  it('文本无法识别 → 阻断', async () => {
    const rows = await one([[1, C, '约1米']]);
    expect(rows.some((r) => r.messages.some((m) => m.includes('无法识别')))).toBe(true);
  });

  it('碎屑格式逗号过多 → 阻断', async () => {
    const rows = await one([[1, F, '0.1,2,3'] as unknown as (number | null | string)[]]);
    expect(rows.some((r) => r.messages.some((m) => m.includes('只能有一个逗号')))).toBe(true);
  });

  it('碎屑块数：未填按 1 块（警告）、非整数/小于 1 阻断', async () => {
    const noCnt = await one([[1, F, 0.3], [1, C, 1.7]]);
    expect(noCnt.some((r) => r.status === 'warning' && r.messages.some((m) => m.includes('按 1 块计')))).toBe(true);

    const frac = await one([[1, F, 0.3, 2.5]]);
    expect(frac.some((r) => r.messages.some((m) => m.includes('不是整数')))).toBe(true);

    const zero = await one([[1, F, 0.3, 0]]);
    expect(zero.some((r) => r.messages.some((m) => m.includes('必须 ≥1')))).toBe(true);
  });

  it('碎屑平均粒径矛盾/过细 → 警告', async () => {
    const big = await one([[1, F, 0.5, 3]]); // 平均 16.7cm ≥10
    expect(big.some((r) => r.messages.some((m) => m.includes('矛盾')))).toBe(true);

    const tiny = await one([[1, F, 0.002, 1]]); // 平均 0.2cm <0.5
    expect(tiny.some((r) => r.messages.some((m) => m.includes('拆成了多格')))).toBe(true);
  });

  it('碎屑长度非数字 → 阻断', async () => {
    const rows = await one([[1, F, 'abc,3'] as unknown as (number | null | string)[]]);
    expect(rows.some((r) => r.messages.some((m) => m.includes('碎屑长度只能填数字')))).toBe(true);
  });

  it('闭合：多记阻断、少记警告', async () => {
    const over = await one([[1, C, 1.5], [1, C, 1.0]]); // 250 vs 200
    expect(over.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('多记 50cm')))).toBe(true);

    const under = await one([[1, C, 1.0]]); // 100 vs 200
    expect(under.some((r) => r.status === 'warning' && r.messages.some((m) => m.includes('少 100cm')))).toBe(true);
  });

  it('平均间距 >500cm → 疑似单位错警告', async () => {
    const rows = await one([[1, C, 2.0]]);
    // 单段 2m：S=200cm 不触发；构造 10m 回次
    const p = await previewWorkbook(await buildWorkbook([[1, 10.0]], [[1, C, 10.0]]), config);
    expect(p.rows[0].avgSpacingCm).toBe(1000);
    expect(p.rows[0].messages.some((m) => m.includes('>500cm'))).toBe(true);
    expect(rows[0].status).toBe('ok');
  });
});

describe('previewWorkbook：版本拒收', () => {
  it('旧版宽表（数据录入 sheet）整表拒收', async () => {
    const buf = await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { legacy: true });
    const p = await previewWorkbook(buf, config);
    expect(p.error).toContain('旧版模板');
  });

  it('v10/v11 长表（回次表/岩心表 sheet）整表拒收', async () => {
    const buf = await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { v1011: true });
    const p = await previewWorkbook(buf, config);
    expect(p.error).toContain('上一版模板');
  });

  it('缺工作表 → 拒收', async () => {
    const wb = new ExcelJS.Workbook();
    wb.addWorksheet('Sheet1').getCell(1, 1).value = 'x';
    const p = await previewWorkbook(toAB(await wb.xlsx.writeBuffer()), config);
    expect(p.error).toContain('未找到');
  });

  it('表头不符 → 拒收', async () => {
    const buf = await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { mainHeader: ['回次', '进尺', '段1'] });
    const p = await previewWorkbook(buf, config);
    expect(p.error).toContain('表头不符');
  });

  it('回次表无数据行 → 拒收', async () => {
    const buf = await buildWorkbook([], []);
    const p = await previewWorkbook(buf, config);
    expect(p.error).toContain('没有数据行');
  });
});

describe('rebuildSegments：归并成完整性分段', () => {
  it('间距相近的相邻回次归并为一段', async () => {
    const runs = [1, 2, 3, 4, 5].map((i) => [i, 1.0]);
    const cores = [1, 2, 3, 4, 5].map((i) => [i, C, 1.0]);
    const p = await previewWorkbook(await buildWorkbook(runs, cores), config);
    expect(p.segments).toHaveLength(1);
    expect(p.segments[0].thicknessM).toBe(5);
    expect(p.segments[0].runCount).toBe(5);
    expect(p.segmentNote).toContain('5 个回次');
    expect(p.rows.every((r) => r.segmentNo === 1)).toBe(true);
  });

  it('阻断行留下深度空洞，天然分界', async () => {
    const runs = [[1, 1.0], [2, 1.0], [3, 1.0]];
    const cores = [[1, C, 1.0], [2, C, 0.9], [3, C, 1.0]]; // 回次2 少记→警告仍入库
    const p = await previewWorkbook(await buildWorkbook(runs, cores), config);
    // 回次2 少记 10cm → 警告但入库，深度链连续 → 仍归为一段
    expect(p.segments).toHaveLength(1);

    const blocked = await buildWorkbook(runs, [[1, C, 1.0], [2, C, 1.5], [3, C, 1.0]]); // 回次2 多记→阻断
    const p2 = await previewWorkbook(blocked, config);
    const okRows = p2.rows.filter((r) => r.status !== 'blocked');
    rebuildSegments(p2, okRows, config);
    expect(p2.segments.length).toBeGreaterThanOrEqual(2);
  });

  it('关闭归并时一个记录段 = 一个分段', async () => {
    const runs = [1, 2, 3].map((i) => [i, 1.0]);
    const cores = [1, 2, 3].map((i) => [i, C, 1.0]);
    const p = await previewWorkbook(await buildWorkbook(runs, cores), config);
    const cfg = defaultConfig();
    cfg.thresholds.mergeRunsIntoRockSegments = false;
    rebuildSegments(p, p.rows, cfg);
    expect(p.segments).toHaveLength(3);
    expect(p.segmentNote).toContain('未启用归并');
  });

  it('深度锚点重排后按新深度重算归并', async () => {
    const runs = [[1, 2.0], [2, 3.0]];
    const cores = [[1, C, 2.0], [2, C, 3.0]];
    const p = await previewWorkbook(await buildWorkbook(runs, cores), config);
    applyDepthAnchor(p, 10);
    rebuildSegments(p, p.rows, config);
    expect(p.rows.map((r) => r.depthStart)).toEqual([10, 12]);
    expect(p.segments[0].depthStart).toBe(10);
    expect(p.segments[0].depthEnd).toBe(15);
  });
});

describe('模板 v12：提示行、基本信息与选填三列', () => {
  it('双行表头：第 2 行提示行整行跳过', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 1.2], [1, C, 0.8]], { hint: true }), config);
    expect(p.error).toBeNull();
    expect(p.rows).toHaveLength(1);
    expect(p.rows[0].pieces).toEqual([120, 80]);
    expect(p.rows[0].status).toBe('ok');
  });

  it('「基本信息」B1 钻孔编号读出；无该表 = null', async () => {
    const withInfo = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { infoNo: 'ZK1' }), config);
    expect(withInfo.boreholeNo).toBe('ZK1');
    const without = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0]]), config);
    expect(without.boreholeNo).toBeNull();
  });

  const withCols = async (cores: (number | null | string)[][]) =>
    (await previewWorkbook(
      await buildWorkbook([[1, 2.0]], cores, { optHeader: ['岩质', '岩体结构类型', '坚硬程度'] }),
      config));

  it('选填三列解析进记录段与完整性分段（回次级，作用于全部记录段）', async () => {
    const p = await withCols([[1, C, 1.2, null, '硬质岩', '块状结构', '坚硬岩'], [1, F, 0.3, 4]]);
    expect(p.error).toBeNull();
    expect(p.rows).toHaveLength(2);
    for (const row of p.rows) {
      expect(row.rockType).toBe(RockType.HardRock);
      expect(row.structureType).toBe(RockStructureType.Blocky);
      expect(row.hardness).toBe(RockHardnessLevel.StrongRock);
    }
    expect(p.segments[0].rockType).toBe(RockType.HardRock);
    expect(p.segments[0].structureType).toBe(RockStructureType.Blocky);
    expect(p.segments[0].hardness).toBe(RockHardnessLevel.StrongRock);
  });

  it('三列留空 = NotSet', async () => {
    const p = await withCols([[1, C, 2.0]]);
    expect(p.rows[0].rockType).toBe(RockType.NotSet);
    expect(p.rows[0].structureType).toBe(RockStructureType.NotSet);
    expect(p.rows[0].hardness).toBe(RockHardnessLevel.NotSet);
    expect(p.segments[0].rockType).toBe(RockType.NotSet);
  });

  it('标签认不出 → 阻断（宁停不错）', async () => {
    const badRock = await withCols([[1, C, 2.0, null, '火成岩']]);
    expect(badRock.rows[0].status).toBe('blocked');
    expect(badRock.rows[0].messages.some((m) => m.includes('岩质「火成岩」无法识别'))).toBe(true);

    const badStruct = await withCols([[1, C, 2.0, null, null, '层状结构']]);
    expect(badStruct.rows[0].messages.some((m) => m.includes('岩体结构类型「层状结构」无法识别'))).toBe(true);

    const badHard = await withCols([[1, C, 2.0, null, null, null, '很硬']]);
    expect(badHard.rows[0].messages.some((m) => m.includes('坚硬程度「很硬」无法识别'))).toBe(true);
  });

  it('结构与岩质半区矛盾 → 阻断；共享结构两边通用', async () => {
    const softWithHardStruct = await withCols([[1, C, 2.0, null, '软质岩', '块状结构']]);
    expect(softWithHardStruct.rows[0].status).toBe('blocked');
    expect(softWithHardStruct.rows[0].messages.some((m) => m.includes('属硬质岩') && m.includes('矛盾'))).toBe(true);

    const hardWithSoftStruct = await withCols([[1, C, 2.0, null, '硬质岩', '块状或次块状结构']]);
    expect(hardWithSoftStruct.rows[0].messages.some((m) => m.includes('属软质岩') && m.includes('矛盾'))).toBe(true);

    const shared = await withCols([[1, C, 2.0, null, '硬质岩', '碎裂结构']]);
    expect(shared.rows[0].status).toBe('ok');
    expect(shared.rows[0].structureType).toBe(RockStructureType.Cataclastic);
  });

  it('岩质不同 → 强制分界（间距相近也不归并）', async () => {
    const runs = [1, 2, 3, 4].map((i) => [i, 1.0]);
    const p = await withColsCols(runs);
    expect(p.segments).toHaveLength(2);
    expect(p.segments[0].rockType).toBe(RockType.HardRock);
    expect(p.segments[1].rockType).toBe(RockType.SoftRock);
  });

  /** 4 个回次：1-2 硬质岩、3-4 软质岩，间距相同 */
  async function withColsCols(runs: number[][]) {
    const cores = runs.map(([i]) => [i, C, 1.0, null, i <= 2 ? '硬质岩' : '软质岩']);
    return previewWorkbook(
      await buildWorkbook(runs, cores, { optHeader: ['岩质', '岩体结构类型', '坚硬程度'] }),
      config);
  }

  it('选填列表头错位 → 整表拒收', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { optHeader: ['岩性', '结构', '硬度'] }),
      config);
    expect(p.error).toContain('选填列表头不符');
  });
});
