/**
 * 钻孔轴线几何计算（v1.1.2）。
 *  - 天顶角（钻探口径）：0°=竖直向下、90°=水平、>90°=仰孔；
 *    数据库内部仍存倾角（水平=0、竖直向下=90、负值=仰孔），仅 UI 以天顶角呈现。
 *  - 洞轴线夹角：孔轴线与洞轴线（水平线）的空间两线夹角，取锐角（0–90°）；
 *    竖直孔对任意水平洞轴线恒为 90°。
 *  - 地下水自动判定：段底铅垂深度（沿孔深 × sin倾角）≥ 水位埋深 ⇒ 有水。
 */

const DEG = Math.PI / 180;

/** 倾角（内部存储口径）→ 天顶角（钻探口径） */
export function inclinationToZenith(inclination: number): number {
  return 90 - inclination;
}

/** 天顶角（钻探口径）→ 倾角（内部存储口径） */
export function zenithToInclination(zenith: number): number {
  return 90 - zenith;
}

/**
 * 孔轴线与洞轴线夹角（度，锐角 0–90）。
 * 推导：孔轴线单位向量（东,北,天）= (sinA₁·cosα, cosA₁·cosα, −sinα)，
 * 洞轴线水平 = (sinA₂, cosA₂, 0)，两向量点积 = cosα·cos(A₁−A₂)，
 * 取 |·| 得锐角：θ = arccos|cosα·cos(A₁−A₂)|。
 * 任一输入缺失返回 null（未定，回退人工）。
 */
export function boreholeCaveAngle(
  azimuth: number | null,
  inclination: number | null,
  caveAxisAzimuth: number | null,
): number | null {
  if (azimuth == null || inclination == null || caveAxisAzimuth == null) return null;
  const cos = Math.abs(
    Math.cos(inclination * DEG) * Math.cos((azimuth - caveAxisAzimuth) * DEG),
  );
  return Math.acos(Math.min(1, cos)) / DEG;
}

/**
 * 地下水自动判定：段底铅垂深度 ≥ 水位埋深 ⇒ 有水。
 * 倾角未填按竖直向下处理（与出图口径一致）；仰孔/水平孔（倾角 ≤ 0）恒无水；
 * 水位埋深缺失返回 null（未定，回退人工）。
 */
export function autoGroundwater(
  depthEndM: number,
  inclination: number | null,
  groundwaterDepthM: number | null,
): boolean | null {
  if (groundwaterDepthM == null) return null;
  const inc = inclination ?? 90;
  if (inc <= 0) return false;
  return depthEndM * Math.sin(inc * DEG) >= groundwaterDepthM;
}

export interface DepthBandInput {
  depth_start: number;
  depth_end: number;
}

export interface DepthBand<T> {
  d0: number;
  d1: number;
  /** null = 未记录段（间隙/孔底以深），出图灰色带，绝不插值伪造 */
  seg: T | null;
}

/** 视为无间隙的容差（米） */
const GAP_EPS = 0.005;

/**
 * 深度分带（三维色带与 DXF GAP 图层共用）：
 * 段按顶深排序，间隙 >0.005m 与尾部（total − 最大底深）补未记录带；
 * 重叠段以最大底深推进游标，不产生负长度带。
 */
export function buildDepthBands<T extends DepthBandInput>(
  segs: readonly T[],
  total: number,
): Array<DepthBand<T>> {
  const sorted = segs.slice().sort((a, b) => a.depth_start - b.depth_start);
  const bands: Array<DepthBand<T>> = [];
  let cursor = 0;
  for (const s of sorted) {
    if (s.depth_start - cursor > GAP_EPS) bands.push({ d0: cursor, d1: s.depth_start, seg: null });
    bands.push({ d0: s.depth_start, d1: s.depth_end, seg: s });
    cursor = Math.max(cursor, s.depth_end);
  }
  if (total - cursor > GAP_EPS) bands.push({ d0: cursor, d1: total, seg: null });
  return bands;
}
