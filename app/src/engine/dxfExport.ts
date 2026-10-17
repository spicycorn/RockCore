/**
 * DXF 导出（v1.1.2）：钻孔三维模型（3DFACE 色带）+ 二维柱状剖面，同文件分图层。
 * 基于 dxf-writer（js-dxf，MIT，成熟稳定）：最小 HEADER（仅 $ACADVER/$INSUNITS/$HANDSEED），
 * AC1021（R2007），图层全部 ACI 索引色 —— 对 AutoCAD 及国产 CAD 兼容性最好。
 * 坐标：X=东、Y=北、Z=高程（上）；二维剖面画在 XY 平面（CAD 2D 视图），整体偏移到 3D 模型东侧。
 * 出图深度一律用 Σ回次进尺（totalDepth），未记录段画灰色 GAP 带，绝不插值伪造。
 */

import Drawing from 'dxf-writer';

const DEG = Math.PI / 180;
/** 3D 色带圆周分段数 */
const RING = 12;
/** 2D 剖面柱宽（图面单位） */
const COL_W = 8;
/** 2D 相邻孔间距 */
const COL_GAP = 30;

export interface DxfSegment {
  depthStart: number;
  depthEnd: number;
  /** 保留字段（图层着色由图层名 → ACI 映射决定，兼容旧调用方） */
  colorHex: string;
  /** 图层名（SEG_I…SEG_V / GAP 等，由调用方按类别映射） */
  layer: string;
  /** 2D 剖面右侧标注文本（如「Ⅲ类」「未记录」） */
  label: string;
  /** 未记录段：3D 灰带 + 2D 标注「未记录」 */
  gap: boolean;
}

export interface DxfBorehole {
  number: string;
  orificeX: number | null;
  orificeY: number | null;
  orificeElevation: number | null;
  /** 孔向方位角（0=北，顺时针）；缺按 0 处理 */
  azimuth: number | null;
  /** 天顶角（钻探口径 0=竖直向下）；缺按 0 处理 */
  zenith: number | null;
  /** 孔深 = Σ回次进尺 */
  totalDepth: number | null;
  segments: readonly DxfSegment[];
}

export interface DxfOptions {
  /** 洞轴线方位角；null 则不画 */
  caveAxisAzimuth: number | null;
  /** 3D 色带半径 = max(孔深 × 系数, 0.5)，默认 0.02 */
  radiusFactor?: number;
}

/** 图层 → ACI 索引色（1红 2黄 3绿 4青 5蓝 7白/黑自动 8深灰 9灰 30棕） */
const LAYER_ACI: Record<string, number> = {
  SEG_I: 3, SEG_II: 5, SEG_III: 1, SEG_IV: 2, SEG_V: 30, SEG_UNK: 9,
  GAP: 8, HOLE_AXIS: 7, CAVE_AXIS: 4, TEXT: 7,
};

interface Vec3 { x: number; y: number; z: number }

const add3 = (a: Vec3, b: Vec3, s = 1): Vec3 => ({ x: a.x + b.x * s, y: a.y + b.y * s, z: a.z + b.z * s });
const cross = (a: Vec3, b: Vec3): Vec3 => ({
  x: a.y * b.z - a.z * b.y, y: a.z * b.x - a.x * b.z, z: a.x * b.y - a.y * b.x,
});
const norm = (a: Vec3): Vec3 => {
  const l = Math.hypot(a.x, a.y, a.z) || 1;
  return { x: a.x / l, y: a.y / l, z: a.z / l };
};

/** 孔轴单位向量与两条法向（e1、e2 ⟂ u），构成色带环基底 */
function axisFrame(azimuthDeg: number, zenithDeg: number): { u: Vec3; e1: Vec3; e2: Vec3 } {
  const a = azimuthDeg * DEG;
  const z = zenithDeg * DEG;
  const u = { x: Math.sin(z) * Math.sin(a), y: Math.sin(z) * Math.cos(a), z: -Math.cos(z) };
  const k: Vec3 = Math.abs(u.z) > 0.999 ? { x: 0, y: 1, z: 0 } : { x: 0, y: 0, z: 1 };
  const e1 = norm(cross(u, k));
  const e2 = cross(u, e1);
  return { u, e1, e2 };
}

/**
 * 生成 DXF（ASCII 字符串，AC1021/R2007）。
 * 3D 模型按真实空间坐标；2D 剖面在 XY 平面上整体偏移到模型东侧。
 */
export function buildDxf(boreholes: readonly DxfBorehole[], opts: DxfOptions): string {
  const d = new Drawing();
  d.setUnits('Meters');

  // 图层：固定层 + 数据里出现的段层（未知段层默认 ACI 7）
  const layerNames = new Set(Object.keys(LAYER_ACI));
  for (const b of boreholes) for (const s of b.segments) layerNames.add(s.layer);
  for (const name of layerNames) d.addLayer(name, LAYER_ACI[name] ?? 7, 'CONTINUOUS');

  const radiusFactor = opts.radiusFactor ?? 0.02;
  // 3D 模型范围（用于 2D 剖面偏移与洞轴线长度）
  const xs: number[] = [];
  const ys: number[] = [];
  let maxDepth = 1;
  for (const b of boreholes) {
    xs.push(b.orificeX ?? 0);
    ys.push(b.orificeY ?? 0);
    maxDepth = Math.max(maxDepth, b.totalDepth ?? 0, ...b.segments.map((s) => s.depthEnd));
  }
  const spread = Math.max(Math.max(...xs) - Math.min(...xs), Math.max(...ys) - Math.min(...ys), 10);
  const textH = Math.max(maxDepth * 0.02, 0.5);
  const twoDX = Math.max(...xs) + spread * 1.5 + COL_GAP;

  boreholes.forEach((b, i) => {
    const e0 = b.orificeX ?? 0;
    const n0 = b.orificeY ?? 0;
    const u0 = b.orificeElevation ?? 0;
    const depth = b.totalDepth ?? b.segments.reduce((a, s) => Math.max(a, s.depthEnd), 0);
    const { u, e1, e2 } = axisFrame(b.azimuth ?? 0, b.zenith ?? 0);
    const r = Math.max(depth * radiusFactor, 0.5);
    const at = (dep: number): Vec3 => add3({ x: e0, y: n0, z: u0 }, u, dep);

    // 孔轴线（3D）
    const tip = at(depth);
    d.setActiveLayer('HOLE_AXIS').drawLine3d(e0, n0, u0, tip.x, tip.y, tip.z);

    // 3D 色带：每段 RING 个 3DFACE 四边形
    for (const seg of b.segments) {
      d.setActiveLayer(seg.layer);
      for (let k = 0; k < RING; k += 1) {
        const t1 = (k / RING) * Math.PI * 2;
        const t2 = ((k + 1) / RING) * Math.PI * 2;
        const ringPoint = (dep: number, t: number): Vec3 => {
          const c = Math.cos(t) * r;
          const s = Math.sin(t) * r;
          return add3(add3(at(dep), e1, c), e2, s);
        };
        const p1 = ringPoint(seg.depthStart, t1);
        const p2 = ringPoint(seg.depthStart, t2);
        const p3 = ringPoint(seg.depthEnd, t2);
        const p4 = ringPoint(seg.depthEnd, t1);
        d.drawFace(p1.x, p1.y, p1.z, p2.x, p2.y, p2.z, p3.x, p3.y, p3.z, p4.x, p4.y, p4.z);
      }
    }

    // 孔号（3D 孔口旁，XY 平面 z=0 不可靠，放到 2D 区标题里；此处仍留 2D 文本于模型北侧）
    d.setActiveLayer('TEXT').drawText(e0 - r * 2, n0 - r * 3, textH, 0, b.number);

    // ---- 2D 柱状剖面（XY 平面：x=图面横向，y=高程方向） ----
    const x2 = twoDX + i * COL_GAP;
    const topY = u0;
    d.setActiveLayer('HOLE_AXIS').drawLine(x2, topY, x2, topY - depth);
    for (const seg of b.segments) {
      const y1 = topY - seg.depthStart;
      const y2 = topY - seg.depthEnd;
      d.setActiveLayer(seg.layer).drawPolyline(
        [
          [x2 - COL_W / 4, y1],
          [x2 + COL_W / 4, y1],
          [x2 + COL_W / 4, y2],
          [x2 - COL_W / 4, y2],
        ],
        true,
      );
      d.setActiveLayer('TEXT').drawText(x2 + COL_W / 4 + 1, (y1 + y2) / 2 - textH * 0.4, textH * 0.8, 0, seg.label);
    }
    // 深度刻度（每 10m）
    d.setActiveLayer('HOLE_AXIS');
    for (let dep = 0; dep <= depth + 0.001; dep += 10) {
      const y = topY - dep;
      d.drawLine(x2 - COL_W / 4 - 2, y, x2 - COL_W / 4, y);
      d.setActiveLayer('TEXT').drawText(x2 - COL_W / 4 - 6, y - textH * 0.4, textH * 0.8, 0, String(Math.round(dep)));
      d.setActiveLayer('HOLE_AXIS');
    }
    d.setActiveLayer('TEXT').drawText(
      x2 - COL_W / 4, topY + textH * 2, textH, 0,
      `${b.number}  天顶角${b.zenith ?? '未填'}° 方位角${b.azimuth ?? '未填'}° 孔深${depth.toFixed(1)}m`,
    );
  });

  // 洞轴线参照（过孔口质心的水平线）
  if (opts.caveAxisAzimuth != null && boreholes.length > 0) {
    const cx = xs.reduce((a, v) => a + v, 0) / xs.length;
    const cy = ys.reduce((a, v) => a + v, 0) / ys.length;
    const cz = boreholes.reduce((a, b) => a + (b.orificeElevation ?? 0), 0) / boreholes.length;
    const a = opts.caveAxisAzimuth * DEG;
    const dir = { x: Math.sin(a), y: Math.cos(a), z: 0 };
    const L = spread * 1.5 + 50;
    d.setActiveLayer('CAVE_AXIS')
      .drawLine3d(cx - dir.x * L, cy - dir.y * L, cz, cx + dir.x * L, cy + dir.y * L, cz);
    d.setActiveLayer('TEXT')
      .drawText(twoDX - COL_W, Math.max(...ys) + textH * 3, textH, 0, `洞轴线 方位角${opts.caveAxisAzimuth}°`);
  }

  return d.toDxfString();
}
