/** DXF 导出（dxf-writer/js-dxf）：结构、图层、实体数量、NaN 防御、AutoCAD 兼容性 */
import { describe, expect, it } from 'vitest';
import { buildDxf, type DxfBorehole, type DxfSegment } from '../dxfExport';

const seg = (a: number, b: number, layer: string, label: string, gap = false): DxfSegment => ({
  depthStart: a, depthEnd: b, colorHex: gap ? '#9ca3af' : '#6495ed', layer, label, gap,
});

const hole: DxfBorehole = {
  number: 'ZK1', orificeX: 100, orificeY: 200, orificeElevation: 50,
  azimuth: 90, zenith: 0, totalDepth: 50,
  segments: [seg(0, 20, 'SEG_III', 'Ⅲ类'), seg(20, 30, 'GAP', '未记录', true), seg(30, 50, 'SEG_IV', 'Ⅳ类')],
};

describe('buildDxf（DXF R2007 / dxf-writer）', () => {
  const dxf = buildDxf([hole], { caveAxisAzimuth: 135 });
  /** ENTITIES 段（其余段也可能出现同名行，统计实体数必须排除） */
  const entities = dxf.slice(dxf.indexOf('ENTITIES'));

  it('标准 DXF 结构与版本头（AC1021 = R2007）', () => {
    expect(dxf).toContain('SECTION');
    expect(dxf).toContain('ENDSEC');
    expect(dxf).toContain('EOF');
    expect(dxf).toMatch(/AC1021/);
  });
  it('图层表包含用到的全部图层', () => {
    for (const layer of ['SEG_III', 'SEG_IV', 'GAP', 'HOLE_AXIS', 'CAVE_AXIS', 'TEXT']) {
      expect(dxf).toContain(layer);
    }
  });
  it('3D 色带：3 段 × 12 面 3DFACE', () => {
    expect((entities.match(/^\s*3DFACE\s*$/gm) ?? []).length).toBe(36);
  });
  it('2D 剖面矩形：每段一条闭合 LWPOLYLINE', () => {
    expect((entities.match(/^\s*LWPOLYLINE\s*$/gm) ?? []).length).toBe(3);
  });
  it('文本：孔号/标题/刻度/标注/洞轴线都在', () => {
    for (const s of ['ZK1', '天顶角0°', '洞轴线 方位角135°', 'Ⅲ类', '未记录']) {
      expect(dxf).toContain(s);
    }
  });
  it('坐标不含 NaN', () => {
    expect(dxf).not.toContain('NaN');
  });

  it('缺孔口坐标/方位/天顶角也能出图（按默认竖直处理）', () => {
    const dxf2 = buildDxf([{ number: 'ZK2', orificeX: null, orificeY: null, orificeElevation: null,
      azimuth: null, zenith: null, totalDepth: null, segments: [seg(0, 10, 'SEG_V', 'Ⅴ类')] }],
    { caveAxisAzimuth: null });
    expect(dxf2).toContain('ZK2');
    expect(dxf2).not.toContain('NaN');
    expect(dxf2).not.toContain('洞轴线');
  });

  it('多孔：两孔标题都在，2D 剖面互不重叠（X 偏移）', () => {
    const dxf3 = buildDxf([hole, { ...hole, number: 'ZK9', orificeX: 130 }], { caveAxisAzimuth: null });
    expect(dxf3).toContain('ZK1');
    expect(dxf3).toContain('ZK9');
    const ent3 = dxf3.slice(dxf3.indexOf('ENTITIES'));
    expect((ent3.match(/^\s*3DFACE\s*$/gm) ?? []).length).toBe(72);
  });
});

/**
 * AutoCAD 兼容性回归（旧库 @mlightcad/data-model 曾因头变量不合规报
 * 「图形头中发生错误 50」并放弃导入；dxf-writer 只写最小头，这里防回归）。
 */
describe('AutoCAD 兼容性', () => {
  const dxf = buildDxf([hole], { caveAxisAzimuth: 135 });
  const lines = dxf.split(/\r?\n/);
  const pairs: Array<[string, string]> = [];
  for (let i = 0; i + 1 < lines.length; i += 2) pairs.push([lines[i].trim(), lines[i + 1].trim()]);

  it('头变量全部是 AutoCAD 已知变量（无 $EXTMIN 空值/未知变量）', () => {
    const known = new Set(['$HANDSEED', '$ACADVER', '$INSUNITS']);
    for (const [c, v] of pairs) {
      if (c === '9') expect(known, `头变量 ${v} 应为 AutoCAD 已知变量`).toContain(v);
    }
  });
  it('不存在「值为空」的头变量（9 后必须紧跟值组码而非下一个 9）', () => {
    for (let i = 0; i + 1 < pairs.length; i += 1) {
      if (pairs[i][0] === '9') expect(pairs[i + 1][0], `${pairs[i][1]} 后必须跟值`).not.toBe('9');
    }
  });
  it('$HANDSEED 大于全文件最大句柄', () => {
    const handseedIdx = pairs.findIndex(([c, v]) => c === '9' && v === '$HANDSEED') + 1;
    let max = 0;
    for (let i = 0; i < pairs.length; i += 1) {
      if (pairs[i][0] === '5' && i !== handseedIdx) max = Math.max(max, Number.parseInt(pairs[i][1], 16) || 0);
    }
    expect(Number.parseInt(pairs[handseedIdx][1], 16)).toBeGreaterThan(max);
  });
  it('每个 LAYER 记录都有 62 颜色且线型合法', () => {
    let inLayer = false;
    let has62 = false;
    let count = 0;
    for (const [c, v] of pairs) {
      if (c === '0') {
        if (inLayer) expect(has62, 'LAYER 记录含 62').toBe(true);
        inLayer = v === 'LAYER';
        has62 = false;
        if (inLayer) count += 1;
      } else if (inLayer && c === '62') has62 = true;
      else if (inLayer && c === '6') {
        expect(['CONTINUOUS', 'Continuous', 'ByLayer', 'ByBlock'], `线型 ${v}`).toContain(v);
      }
    }
    if (inLayer) expect(has62).toBe(true);
    expect(count).toBeGreaterThanOrEqual(7); // 0 + HOLE_AXIS/CAVE_AXIS/TEXT/GAP/SEG_III/SEG_IV
  });
  it('固定图层用 ACI 索引色（黑底/白底都可见）', () => {
    // HOLE_AXIS/TEXT → 62 7（自动对比色），CAVE_AXIS → 62 4，GAP → 62 8
    expect(dxf).toMatch(/HOLE_AXIS[\s\S]{0,120}?\n62\n7\n/);
    expect(dxf).toMatch(/CAVE_AXIS[\s\S]{0,120}?\n62\n4\n/);
    expect(dxf).toMatch(/GAP[\s\S]{0,120}?\n62\n8\n/);
  });
  it('TEXT 实体不引用未定义样式（无组码 7 或引用 STANDARD）', () => {
    let inText = false;
    for (const [c, v] of pairs) {
      if (c === '0') inText = v === 'TEXT';
      else if (inText && c === '7') expect(v.toUpperCase()).toBe('STANDARD');
    }
  });
});
