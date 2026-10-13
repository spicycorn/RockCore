/**
 * 导入引擎端到端黄金用例（模板 v10 长表）：用 exceljs 现场构造真实 xlsx 字节流 → previewWorkbook，
 * 校验解析/拆段/闭合/判级/归并全链路（无 mock）。
 *
 * 「回次表」：A=回次号 B=回次进尺(m)；「岩心表」：A=回次号 B=类型 C=长度(m) D=块数。
 */
import { describe, expect, it } from 'vitest';
import ExcelJS from 'exceljs';
import { applyDepthAnchor, previewWorkbook, rebuildSegments } from '../excelImport';
import { defaultConfig } from '../config';
import { IntegrityLevel } from '../enums';

const config = defaultConfig();

function toAB(b: ArrayBuffer | Uint8Array): ArrayBuffer {
  if (b instanceof ArrayBuffer) return b;
  return new Uint8Array(b).buffer;
}

/** 构造「回次表」+「岩心表」工作簿并返回字节 */
async function buildWorkbook(
  runs: (number | null | string)[][],
  cores: (number | null | string)[][],
  opts: { legacy?: boolean; runHeader?: string[]; coreHeader?: string[] } = {},
): Promise<ArrayBuffer> {
  const wb = new ExcelJS.Workbook();
  const wsRun = wb.addWorksheet('回次表');
  const runHeader = opts.runHeader ?? ['回次号', '回次进尺(m)'];
  wsRun.getCell(1, 1).value = runHeader[0];
  wsRun.getCell(1, 2).value = runHeader[1];
  runs.forEach((cells, ri) => {
    cells.forEach((v, ci) => {
      if (v !== null) wsRun.getCell(ri + 2, ci + 1).value = v as number;
    });
  });
  const wsCore = wb.addWorksheet('岩心表');
  const coreHeader = opts.coreHeader ?? ['回次号', '类型', '长度(m)', '块数'];
  coreHeader.forEach((h, ci) => {
    wsCore.getCell(1, ci + 1).value = h;
  });
  cores.forEach((cells, ri) => {
    cells.forEach((v, ci) => {
      if (v !== null) wsCore.getCell(ri + 2, ci + 1).value = v as number;
    });
  });
  if (opts.legacy) {
    const wsOld = wb.addWorksheet('数据录入');
    wsOld.getCell(1, 1).value = '回次号';
    wsOld.getCell(1, 2).value = '行型';
  }
  return toAB(await wb.xlsx.writeBuffer());
}

const C = '完整';
const F = '碎屑';

describe('previewWorkbook：解析与拆段', () => {
  it('连续完整行并成一个记录段，指标按回次重算', async () => {
    const buf = await buildWorkbook([[1, 2.0]], [[1, C, 1.2], [1, C, 0.8]]);
    const p = await previewWorkbook(buf, config);
    expect(p.error).toBeNull();
    expect(p.rows).toHaveLength(1);
    const row = p.rows[0];
    expect(row.pieces).toEqual([120, 80]);
    expect(row.totalPieces).toBe(2);
    expect(row.avgSpacingCm).toBe(100);
    expect(row.rqd).toBe(100);
    expect(row.level).toBe(IntegrityLevel.Intact); // S=100 > 95
    expect(row.status).toBe('ok');
    expect(row.depthStart).toBe(0);
    expect(row.depthEnd).toBe(2);
  });

  it('碎屑行独立成段，少记差额计入回次底部', async () => {
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

  it('深度链按回次表行序累加', async () => {
    const buf = await buildWorkbook([[1, 2.0], [2, 3.0]], [[1, C, 2.0], [2, C, 3.0]]);
    const p = await previewWorkbook(buf, config);
    expect(p.rows.map((r) => [r.depthStart, r.depthEnd])).toEqual([[0, 2], [2, 5]]);
  });

  it('回次号重复 / 缺失 / 不在回次表中均阻断', async () => {
    const dup = await buildWorkbook([[1, 2.0], [1, 1.0]], [[1, C, 2.0]]);
    const p1 = await previewWorkbook(dup, config);
    expect(p1.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('重复')))).toBe(true);

    const noRun = await buildWorkbook([[1, 2.0]], [[null, C, 1.0]]);
    const p2 = await previewWorkbook(noRun, config);
    expect(p2.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('缺少回次号')))).toBe(true);

    const ghost = await buildWorkbook([[1, 2.0]], [[7, C, 1.0]]);
    const p3 = await previewWorkbook(ghost, config);
    expect(p3.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('不在「回次表」中')))).toBe(true);
  });

  it('回次在岩心表中没有任何记录 → 阻断', async () => {
    const buf = await buildWorkbook([[1, 2.0]], []);
    const p = await previewWorkbook(buf, config);
    expect(p.rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('没有任何岩心记录')))).toBe(true);
  });
});

describe('previewWorkbook：行校验', () => {
  const one = async (cores: (number | null | string)[][]) =>
    (await previewWorkbook(await buildWorkbook([[1, 2.0]], cores), config)).rows;

  it('完整段填块数 → 阻断', async () => {
    const rows = await one([[1, C, 1.0, 3]]);
    expect(rows[0].status).toBe('blocked');
    expect(rows[0].messages.some((m) => m.includes('完整段不能填块数'))).toBe(true);
  });

  it('段长 <0.1m → 阻断并提示记为碎屑行', async () => {
    const rows = await one([[1, C, 0.05], [1, C, 1.95]]);
    expect(rows.some((r) => r.status === 'blocked' && r.messages.some((m) => m.includes('<0.1m')))).toBe(true);
  });

  it('长度非数字 → 阻断', async () => {
    const rows = await one([[1, C, '约1米']]);
    expect(rows.some((r) => r.messages.some((m) => m.includes('只能填数字')))).toBe(true);
  });

  it('类型无法识别 → 阻断', async () => {
    const rows = await one([[1, '破碎', 1.0]]);
    expect(rows.some((r) => r.messages.some((m) => m.includes('无法识别')))).toBe(true);
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
    expect(tiny.some((r) => r.messages.some((m) => m.includes('拆成了多行')))).toBe(true);
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

  it('缺工作表 → 拒收', async () => {
    const wb = new ExcelJS.Workbook();
    wb.addWorksheet('回次表').getCell(1, 1).value = '回次号';
    const p = await previewWorkbook(toAB(await wb.xlsx.writeBuffer()), config);
    expect(p.error).toContain('未找到');
  });

  it('表头不符 → 拒收', async () => {
    const buf = await buildWorkbook([[1, 2.0]], [[1, C, 2.0]], { coreHeader: ['回次', '类型', '长度(m)', '块数'] });
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
    const cores = [[1, C, 1.0], [2, C, 0.9], [3, C, 1.0]]; // 回次2 少记→警告仍入库；改用阻断构造空洞
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
