/**
 * 导入引擎端到端黄金用例（模板 v13：一行一回次，岩心段不限列）：用 exceljs 现场构造真实 xlsx 字节流
 * → previewWorkbook，校验解析/拆段/闭合/判级/归并全链路（无 mock）。
 *
 * 「岩心记录」：A1=「钻孔编号」标签、B1=孔号（选填）；第 2 行表头 A=回次号 B=回次进尺(m) C=RQD(%)
 * D=岩质 E=岩体结构类型 F=坚硬程度 G 起=岩心段1..（helper 只建 10 列，引擎按实际列数读、不限列）；
 * 第 3 行提示行；数据第 4 行起。
 * 格语义：纯数字 = 正常段（<10cm 短段也有效，计入 n 与深度链、不计 RQD 分子，短段均值参与 ≤10cm 档分档）；
 * 「长度，破碎」（全/半角逗号、可带 m 后缀）= 破碎段：独立记录行、level=破碎、不计 n/S/RQD、
 * 计入采取率与深度链，与正常段强制分界（破碎-破碎可归并）；「0.08,3」旧碎屑写法 → 阻断。
 * RQD 列导入一律忽略（重算）；闭合：多记（>进尺+1cm）阻断，少记只影响采取率。
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

const C = '正常'; // 纯数字正常段
const F = '破碎'; // 「长度，破碎」破碎段（全角逗号）
const RAW = 'RAW'; // 格内容原样写入（构造脏数据/写法变体）
const GAP = 'GAP'; // 留空一格（构造空格跳格）

/** cores = [回次号, 类型, 值, 岩质?, 结构?, 坚硬?]；回次号缺失或无对应回次 → 独立脏行 */
type Core = [number | null, string, (number | string | null)?, (string | null)?, (string | null)?, (string | null)?];

/**
 * 构造 v13「岩心记录」工作簿并返回字节。runs = [回次号, 进尺(m), RQD列垃圾值?]。
 * 同一回次的 core 按序换算成 G 列起的岩心段格；选填三列取该回次首个非空值写 D/E/F（回次级）。
 */
async function buildWorkbook(
  runs: (number | null | string)[][],
  cores: Core[],
  opts: {
    legacy?: boolean; v1011?: boolean; infoSheet?: boolean;
    mainHeader?: string[]; optHeader?: string[];
    holeNo?: string; extraHintAfter?: number;
  } = {},
): Promise<ArrayBuffer> {
  const wb = new ExcelJS.Workbook();
  const ws = wb.addWorksheet('岩心记录');

  // 第 1 行标签必须写：引擎按 actualRowCount（有数据行数）扫行，真实模板 A1 恒有「钻孔编号」标签
  ws.getCell(1, 1).value = '钻孔编号';
  if (opts.holeNo != null) ws.getCell(1, 2).value = opts.holeNo;
  if (opts.legacy) wb.addWorksheet('数据录入').getCell(1, 1).value = '回次号';
  if (opts.v1011) {
    wb.addWorksheet('回次表').getCell(1, 1).value = '回次号';
    wb.addWorksheet('岩心表').getCell(1, 1).value = '回次号';
  }
  if (opts.infoSheet) wb.addWorksheet('基本信息').getCell(1, 1).value = '钻孔编号';

  const header = opts.mainHeader
    ?? ['回次号', '回次进尺(m)', 'RQD(%)', '岩质', '岩体结构类型', '坚硬程度',
      ...Array.from({ length: 10 }, (_, i) => `岩心段${i + 1}`)];
  header.forEach((h, ci) => { ws.getCell(2, ci + 1).value = h; });
  if (opts.optHeader) opts.optHeader.forEach((h, i) => { ws.getCell(2, 4 + i).value = h; });
  ws.getCell(3, 1).value = HINT_MAIN_A; // 第 3 行提示行

  interface RowSpec { runNo: number | null; advance: number | null; rqd?: number | string | null; cells: (number | string | null)[]; opt?: (string | null | undefined)[] }
  const rowSpecs: RowSpec[] = [];
  const specByRun = new Map<number, RowSpec>();
  for (const runRow of runs) {
    const spec: RowSpec = {
      runNo: runRow[0] as number | null,
      advance: (runRow[1] as number | null) ?? null,
      rqd: (runRow[2] as number | string | null) ?? null,
      cells: [],
    };
    rowSpecs.push(spec);
    if (spec.runNo != null && !specByRun.has(spec.runNo)) specByRun.set(spec.runNo, spec);
  }
  for (const [runNo, kind, value, rock, struct, hard] of cores) {
    let spec = runNo != null ? specByRun.get(runNo) : undefined;
    if (spec == null) {
      spec = { runNo, advance: null, cells: [] };
      rowSpecs.push(spec);
      if (runNo != null) specByRun.set(runNo, spec);
    }
    if (kind === GAP) spec.cells.push(null);
    else if (kind === RAW) spec.cells.push(value ?? null);
    else if (kind === F) spec.cells.push(`${value}，破碎`);
    else spec.cells.push(value as number);
    if (rock != null || struct != null || hard != null) {
      spec.opt ??= [null, null, null];
      [rock, struct, hard].forEach((v, i) => { if (v != null && spec!.opt![i] == null) spec!.opt![i] = v; });
    }
  }

  let r = 4;
  rowSpecs.forEach((spec, ri) => {
    if (opts.extraHintAfter === ri) { ws.getCell(r, 1).value = HINT_MAIN_A; r++; } // 数据中间插提示行
    if (spec.runNo != null) ws.getCell(r, 1).value = spec.runNo;
    if (spec.advance != null) ws.getCell(r, 2).value = spec.advance;
    if (spec.rqd != null) ws.getCell(r, 3).value = spec.rqd;
    spec.opt?.forEach((v, i) => { if (v != null) ws.getCell(r, 4 + i).value = v; });
    spec.cells.forEach((v, ci) => { if (v != null) ws.getCell(r, 7 + ci).value = v as number; });
    r++;
  });

  return toAB(await wb.xlsx.writeBuffer());
}

describe('previewWorkbook：解析与拆段', () => {
  it('连续正常格并成一个记录段，指标按回次重算', async () => {
    const p = await previewWorkbook(await buildWorkbook([[1, 2.0]], [[1, C, 1.2], [1, C, 0.8]]), config);
    expect(p.error).toBeNull();
    expect(p.rows).toHaveLength(1);
    const row = p.rows[0];
    expect(row.pieces).toEqual([120, 80]);
    expect(row.totalPieces).toBe(2);
    expect(row.avgSpacingCm).toBe(100);
    expect(row.rqd).toBe(100);
    expect(row.recoveryPct).toBe(100);
    expect(row.level).toBe(IntegrityLevel.RelativelyIntact); // S=100 ∈ (50,100] 较完整档
    expect(row.status).toBe('ok');
    expect(row.depthStart).toBe(0);
    expect(row.depthEnd).toBe(2);
  });

  it('破碎段独立成行且保持顺序，少记差额计入回次底部（采取率记录，不警告）', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 0.8], [1, C, 0.7], [1, F, 0.3]]), config);
    expect(p.rows).toHaveLength(2);
    const [a, b] = p.rows;
    expect(a.pieces).toEqual([80, 70]);
    expect(a.runLengthM).toBe(1.5);
    expect(a.runRowCount).toBe(2);
    expect(a.runPartIndex).toBe(1);
    expect(b.broken).toBe(true);
    expect(b.pieces).toEqual([]);
    expect(b.level).toBe(IntegrityLevel.Broken);
    expect(b.totalPieces).toBe(0);
    expect(b.avgSpacingCm).toBe(0);
    expect(b.rqd).toBe(0);
    expect(b.runLengthM).toBe(0.5); // 0.3 + 0.2 少记差额（取芯损耗）计入回次底部
    expect(b.messages.some((m) => m.includes('少'))).toBe(false);
    expect(b.status).toBe('ok');
    expect(b.recoveryPct).toBe(90); // (150+30)/200，破碎段计入采取率
    expect(b.depthStart).toBe(1.5);
    expect(b.depthEnd).toBe(2);
  });

  it('破碎带在浅部、整段在深部：拆段顺序照实反映', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 1.5]], [[1, F, 0.05], [1, C, 1.4]]), config);
    expect(p.rows).toHaveLength(2);
    expect(p.rows[0].broken).toBe(true);
    expect(p.rows[0].depthEnd).toBe(0.05);
    expect(p.rows[1].pieces).toEqual([140]);
    expect(p.rows[1].runLengthM).toBe(1.45); // 少记 5cm 差额计入回次底部
    expect(p.rows[1].depthStart).toBe(0.05);
  });

  it('破碎写法变体：半角逗号、m 后缀均识别为破碎段', async () => {
    const half = await previewWorkbook(
      await buildWorkbook([[1, 1.0]], [[1, RAW, '0.5,破碎'], [1, C, 0.5]]), config);
    expect(half.error).toBeNull();
    expect(half.rows).toHaveLength(2);
    expect(half.rows[0].broken).toBe(true);
    expect(half.rows[0].level).toBe(IntegrityLevel.Broken);
    const suffix = await previewWorkbook(
      await buildWorkbook([[1, 1.0]], [[1, RAW, '1.0m，破碎']]), config);
    expect(suffix.rows).toHaveLength(1);
    expect(suffix.rows[0].broken).toBe(true);
    expect(suffix.rows[0].recoveryPct).toBe(100);
  });

  it('<10cm 短段计入 n 与深度链、不计 RQD 分子，短段均值进 fragCount/fragLengthCm', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 0.55]], [[1, C, 0.5], [1, C, 0.03], [1, C, 0.02]]), config);
    const row = p.rows[0];
    expect(row.pieces).toEqual([50, 3, 2]);
    expect(row.totalPieces).toBe(3); // n 含短段
    expect(row.fragCount).toBe(2);
    expect(row.fragLengthCm).toBe(5);
    expect(row.avgSpacingCm).toBe(18.33); // 55/3
    expect(row.rqd).toBe(90.91); // 50/55，短段不进分子
    expect(row.level).toBe(IntegrityLevel.Poor); // S=18.33 ∈ (10,30]
    expect(row.recoveryPct).toBe(100);
  });

  it('≤10cm 间距档按短段平均块径分档：<3.3cm→较破碎，≥3.3cm→完整性差', async () => {
    // 30 段 ×1cm：S=1cm，均值 1cm < 3.3 → >3 组 → 较破碎
    const fine = await previewWorkbook(
      await buildWorkbook([[1, 0.3]], Array.from({ length: 30 }, () => [1, C, 0.01] as Core)), config);
    expect(fine.rows[0].avgSpacingCm).toBe(1);
    expect(fine.rows[0].rqd).toBe(0); // 全部 <10cm
    expect(fine.rows[0].level).toBe(IntegrityLevel.RelativelyBroken);
    // 3 段 ×4cm：S=4cm，均值 4cm ≥ 3.3 → 2~3 组 → 完整性差
    const coarse = await previewWorkbook(
      await buildWorkbook([[1, 0.12]], [[1, C, 0.04], [1, C, 0.04], [1, C, 0.04]]), config);
    expect(coarse.rows[0].level).toBe(IntegrityLevel.Poor);
  });

  it('空格跳过但顺序连贯 → 警告', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 1.0], [1, GAP], [1, C, 1.0]]), config);
    expect(p.rows).toHaveLength(1);
    expect(p.rows[0].pieces).toEqual([100, 100]);
    expect(p.rows[0].status).toBe('warning');
    expect(p.rows[0].messages.some((m) => m.includes('为空但后面还有段'))).toBe(true);
  });

  it('深度链按行序累加', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.0], [2, 3.0]], [[1, C, 2.0], [2, C, 3.0]]), config);
    expect(p.rows.map((r) => [r.depthStart, r.depthEnd])).toEqual([[0, 2], [2, 5]]);
  });

  it('回次号重复 / 缺失 / 缺进尺均阻断', async () => {
    const p1 = await previewWorkbook(
      await buildWorkbook([[1, 2.0], [1, 1.0]], [[1, C, 2.0]]), config);
    expect(p1.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('重复')))).toBe(true);

    const p2 = await previewWorkbook(await buildWorkbook([[1, 2.0]], [[null, C, 1.0]]), config);
    expect(p2.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('缺少回次号')))).toBe(true);

    const p3 = await previewWorkbook(await buildWorkbook([[1, 2.0]], [[7, C, 1.0]]), config);
    expect(p3.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('缺少进尺')))).toBe(true);
  });

  it('回次没有任何岩心记录 → 阻断', async () => {
    const p = await previewWorkbook(await buildWorkbook([[1, 2.0]], []), config);
    expect(p.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('没有任何岩心记录')))).toBe(true);
  });

  it('RQD 列导入忽略：填垃圾值不报错，指标一律重算', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.0, '现场参考']], [[1, C, 1.2], [1, C, 0.8]]), config);
    expect(p.error).toBeNull();
    expect(p.rows[0].status).toBe('ok');
    expect(p.rows[0].rqd).toBe(100);
  });

  it('提示行整行跳过：第 3 行固定跳过，数据中间被插提示行也不误读', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.0], [2, 2.0]], [[1, C, 2.0], [2, C, 2.0]], { extraHintAfter: 1 }), config);
    expect(p.error).toBeNull();
    expect(p.rows).toHaveLength(2);
    expect(p.rows.map((r) => [r.depthStart, r.depthEnd])).toEqual([[0, 2], [2, 4]]);
    expect(p.rows.every((r) => r.status === 'ok')).toBe(true);
  });
});

describe('previewWorkbook：格校验', () => {
  const one = async (cores: Core[]) =>
    (await previewWorkbook(await buildWorkbook([[1, 2.0]], cores), config)).rows;

  it('数字段长 ≤0 → 阻断', async () => {
    const rows = await one([[1, C, 0], [1, C, 2.0]]);
    expect(rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('段长必须大于 0')))).toBe(true);
  });

  it('文本无法识别 → 阻断', async () => {
    const rows = await one([[1, RAW, '约1米']]);
    expect(rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('无法识别')))).toBe(true);
  });

  it('旧碎屑写法「0.08,3」→ 阻断并指引 v13 写法', async () => {
    const rows = await one([[1, RAW, '0.08,3']]);
    expect(rows.some((r) => r.status === 'blocked'
      && r.messages.some((m) => m.includes('旧版碎屑写法') && m.includes('长度，破碎')))).toBe(true);
  });

  it('破碎写法脏变体：逗号过多/长度非数字/零长/错标签 → 均无法识别阻断', async () => {
    const rows = await one([[1, RAW, '0.5,破碎,3']]);
    expect(rows.some((r) => r.messages.some((m) => m.includes('无法识别')))).toBe(true);
    const rows2 = await one([[1, RAW, 'abc，破碎']]);
    expect(rows2.some((r) => r.messages.some((m) => m.includes('无法识别')))).toBe(true);
    const rows3 = await one([[1, RAW, '0，破碎']]);
    expect(rows3.some((r) => r.messages.some((m) => m.includes('无法识别')))).toBe(true);
    const rows4 = await one([[1, RAW, '1.8，碎裂']]);
    expect(rows4.some((r) => r.messages.some((m) => m.includes('无法识别')))).toBe(true);
  });

  it('闭合：多记阻断；少记属取芯损耗，不警告、记采取率', async () => {
    const over = await one([[1, C, 1.5], [1, C, 1.0]]); // 250 vs 200
    expect(over.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('多记 50cm')))).toBe(true);

    const overBroken = await one([[1, C, 0.6], [1, F, 0.6]]); // 破碎段也计入闭合：120 vs 200 不多记
    expect(overBroken.some((r) => r.status === 'blocked')).toBe(false);

    const under = await one([[1, C, 1.0]]); // 100 vs 200 → 采取率 50%
    expect(under.some((r) => r.messages.some((m) => m.includes('少')))).toBe(false);
    expect(under[0].status).toBe('ok');
    expect(under[0].recoveryPct).toBe(50);
  });

  it('多记 ≤1cm 容差不阻断，采取率封顶 100', async () => {
    const rows = await one([[1, C, 2.005]]); // 200.5 vs 200，差 0.5cm
    expect(rows[0].status).toBe('ok');
    expect(rows[0].recoveryPct).toBe(100);
  });

  it('平均间距 >500cm → 疑似单位错警告', async () => {
    const p = await previewWorkbook(await buildWorkbook([[1, 10.0]], [[1, C, 10.0]]), config);
    expect(p.rows[0].avgSpacingCm).toBe(1000);
    expect(p.rows[0].messages.some((m) => m.includes('>500cm'))).toBe(true);
    expect(p.rows[0].status).toBe('warning');
  });
});

describe('previewWorkbook：版本拒收', () => {
  it('v9 宽表（数据录入 sheet）整表拒收', async () => {
    const p = await previewWorkbook(await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { legacy: true }), config);
    expect(p.error).toContain('旧版模板（v9');
  });

  it('v10/v11 长表（回次表/岩心表 sheet）整表拒收', async () => {
    const p = await previewWorkbook(await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { v1011: true }), config);
    expect(p.error).toContain('旧版模板（v10/v11');
  });

  it('v12（基本信息单独成表）整表拒收「上一版模板」', async () => {
    const p = await previewWorkbook(await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { infoSheet: true }), config);
    expect(p.error).toContain('上一版模板（v12');
  });

  it('缺工作表 → 拒收', async () => {
    const wb = new ExcelJS.Workbook();
    wb.addWorksheet('Sheet1').getCell(1, 1).value = 'x';
    const p = await previewWorkbook(toAB(await wb.xlsx.writeBuffer()), config);
    expect(p.error).toContain('未找到');
  });

  it('表头不符（第 2 行）→ 拒收', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0]],
        { mainHeader: ['回次', '进尺', 'RQD(%)', '岩质', '岩体结构类型', '坚硬程度', '岩心段1'] }), config);
    expect(p.error).toContain('表头不符');
  });

  it('没有数据行 → 拒收', async () => {
    const p = await previewWorkbook(await buildWorkbook([], []), config);
    expect(p.error).toContain('没有数据行');
  });
});

describe('rebuildSegments：归并成完整性分段', () => {
  it('间距相近的相邻回次归并为一段', async () => {
    const runs = [1, 2, 3, 4, 5].map((i) => [i, 1.0]);
    const cores = [1, 2, 3, 4, 5].map((i) => [i, C, 1.0] as Core);
    const p = await previewWorkbook(await buildWorkbook(runs, cores), config);
    expect(p.segments).toHaveLength(1);
    expect(p.segments[0].thicknessM).toBe(5);
    expect(p.segments[0].runCount).toBe(5);
    expect(p.segmentNote).toContain('5 个回次');
    expect(p.rows.every((r) => r.segmentNo === 1)).toBe(true);
  });

  it('阻断行留下深度空洞，天然分界', async () => {
    const runs = [[1, 1.0], [2, 1.0], [3, 1.0]];
    const p = await previewWorkbook(
      await buildWorkbook(runs, [[1, C, 1.0], [2, C, 1.5], [3, C, 1.0]]), config); // 回次2 多记→阻断
    const okRows = p.rows.filter((r) => r.status !== 'blocked');
    rebuildSegments(p, okRows, config);
    expect(p.segments.length).toBeGreaterThanOrEqual(2);
  });

  it('关闭归并时一个记录段 = 一个分段', async () => {
    const runs = [1, 2, 3].map((i) => [i, 1.0]);
    const cores = [1, 2, 3].map((i) => [i, C, 1.0] as Core);
    const p = await previewWorkbook(await buildWorkbook(runs, cores), config);
    const cfg = defaultConfig();
    cfg.thresholds.mergeRunsIntoRockSegments = false;
    rebuildSegments(p, p.rows, cfg);
    expect(p.segments).toHaveLength(3);
    expect(p.segmentNote).toContain('未启用归并');
  });

  it('深度锚点重排后按新深度重算归并', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.0], [2, 3.0]], [[1, C, 2.0], [2, C, 3.0]]), config);
    applyDepthAnchor(p, 10);
    rebuildSegments(p, p.rows, config);
    expect(p.rows.map((r) => r.depthStart)).toEqual([10, 12]);
    expect(p.segments[0].depthStart).toBe(10);
    expect(p.segments[0].depthEnd).toBe(15);
  });

  it('破碎段与正常段强制分界；破碎-破碎可归并', async () => {
    const runs = [[1, 1.0], [2, 1.0], [3, 1.0]];
    const p3 = await previewWorkbook(
      await buildWorkbook(runs, [[1, C, 1.0], [2, F, 1.0], [3, C, 1.0]]), config);
    expect(p3.segments).toHaveLength(3);
    expect(p3.segments.map((s) => s.level)).toEqual(
      [IntegrityLevel.RelativelyIntact, IntegrityLevel.Broken, IntegrityLevel.RelativelyIntact]);

    const p2 = await previewWorkbook(
      await buildWorkbook(runs, [[1, C, 1.0], [2, F, 1.0], [3, F, 1.0]]), config);
    expect(p2.segments).toHaveLength(2);
    expect(p2.segments[1].level).toBe(IntegrityLevel.Broken);
    expect(p2.segments[1].runCount).toBe(2);
  });

  it('岩质不同 → 强制分界（间距相近也不归并）', async () => {
    const runs = [1, 2, 3, 4].map((i) => [i, 1.0]);
    const cores = [1, 2, 3, 4].map((i) => [i, C, 1.0, i <= 2 ? '硬质岩' : '软质岩'] as Core);
    const p = await previewWorkbook(await buildWorkbook(runs, cores), config);
    expect(p.segments).toHaveLength(2);
    expect(p.segments[0].rockType).toBe(RockType.HardRock);
    expect(p.segments[1].rockType).toBe(RockType.SoftRock);
  });
});

describe('模板 v13：钻孔编号与选填三列', () => {
  it('B1 钻孔编号读出；未填 = null', async () => {
    const withNo = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { holeNo: 'ZK1' }), config);
    expect(withNo.boreholeNo).toBe('ZK1');
    const without = await previewWorkbook(await buildWorkbook([[1, 2.0]], [[1, C, 2.0]]), config);
    expect(without.boreholeNo).toBeNull();
  });

  it('选填三列解析进记录段与完整性分段（回次级，作用于全部记录段）', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.3]],
        [[1, C, 1.2, '硬质岩', '块状结构', '坚硬岩'], [1, C, 0.8], [1, F, 0.3]]), config);
    expect(p.error).toBeNull();
    expect(p.rows).toHaveLength(2);
    for (const row of p.rows) {
      expect(row.rockType).toBe(RockType.HardRock);
      expect(row.structureType).toBe(RockStructureType.Blocky);
      expect(row.hardness).toBe(RockHardnessLevel.StrongRock);
    }
    for (const seg of p.segments) {
      expect(seg.rockType).toBe(RockType.HardRock);
      expect(seg.structureType).toBe(RockStructureType.Blocky);
      expect(seg.hardness).toBe(RockHardnessLevel.StrongRock);
    }
  });

  it('三列留空 = NotSet', async () => {
    const p = await previewWorkbook(await buildWorkbook([[1, 2.0]], [[1, C, 2.0]]), config);
    expect(p.rows[0].rockType).toBe(RockType.NotSet);
    expect(p.rows[0].structureType).toBe(RockStructureType.NotSet);
    expect(p.rows[0].hardness).toBe(RockHardnessLevel.NotSet);
    expect(p.segments[0].rockType).toBe(RockType.NotSet);
  });

  it('标签认不出 → 阻断（宁停不错）', async () => {
    const badRock = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0, '火成岩']]), config);
    expect(badRock.rows[0].status).toBe('blocked');
    expect(badRock.rows[0].messages.some((m) => m.includes('岩质「火成岩」无法识别'))).toBe(true);

    const badStruct = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0, null, '层状结构']]), config);
    expect(badStruct.rows[0].messages.some((m) => m.includes('岩体结构类型「层状结构」无法识别'))).toBe(true);

    const badHard = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0, null, null, '很硬']]), config);
    expect(badHard.rows[0].messages.some((m) => m.includes('坚硬程度「很硬」无法识别'))).toBe(true);
  });

  it('结构与岩质半区矛盾 → 阻断；共享结构两边通用', async () => {
    const softWithHardStruct = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0, '软质岩', '块状结构']]), config);
    expect(softWithHardStruct.rows[0].status).toBe('blocked');
    expect(softWithHardStruct.rows[0].messages.some((m) => m.includes('属硬质岩') && m.includes('矛盾'))).toBe(true);

    const hardWithSoftStruct = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0, '硬质岩', '块状或次块状结构']]), config);
    expect(hardWithSoftStruct.rows[0].messages.some((m) => m.includes('属软质岩') && m.includes('矛盾'))).toBe(true);

    const shared = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0, '硬质岩', '碎裂结构']]), config);
    expect(shared.rows[0].status).toBe('ok');
    expect(shared.rows[0].structureType).toBe(RockStructureType.Cataclastic);
  });

  it('选填列表头错位 → 整表拒收', async () => {
    const p = await previewWorkbook(
      await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { optHeader: ['岩性', '结构', '硬度'] }), config);
    expect(p.error).toContain('选填列表头不符');
  });
});
