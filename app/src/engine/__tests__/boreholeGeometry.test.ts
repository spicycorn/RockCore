/** 天顶角换算 / 洞轴夹角 / 地下水自动判定（v1.1.2） */
import { describe, expect, it } from 'vitest';
import {
  autoGroundwater,
  boreholeCaveAngle,
  inclinationToZenith,
  zenithToInclination,
} from '../boreholeGeometry';

describe('天顶角换算（钻探口径：0=竖直向下，90=水平，>90=仰孔）', () => {
  it('倾角 ↔ 天顶角互逆', () => {
    expect(inclinationToZenith(90)).toBe(0);   // 竖直向下孔
    expect(inclinationToZenith(0)).toBe(90);   // 水平孔
    expect(inclinationToZenith(-30)).toBe(120); // 仰孔
    expect(zenithToInclination(0)).toBe(90);
    expect(zenithToInclination(90)).toBe(0);
    expect(zenithToInclination(120)).toBe(-30);
    for (const inc of [-89, -30, 0, 45, 90]) {
      expect(zenithToInclination(inclinationToZenith(inc))).toBeCloseTo(inc);
    }
  });
});

describe('洞轴线夹角（锐角 0–90°）', () => {
  it('缺任一输入 → null', () => {
    expect(boreholeCaveAngle(null, 90, 0)).toBeNull();
    expect(boreholeCaveAngle(0, null, 0)).toBeNull();
    expect(boreholeCaveAngle(0, 90, null)).toBeNull();
  });
  it('竖直孔对任意水平洞轴线恒 90°', () => {
    expect(boreholeCaveAngle(37, 90, 123)).toBeCloseTo(90);
  });
  it('水平孔：平行 0°、正交 90°、45° 对穿 45°', () => {
    expect(boreholeCaveAngle(90, 0, 90)).toBeCloseTo(0);
    expect(boreholeCaveAngle(0, 0, 90)).toBeCloseTo(90);
    expect(boreholeCaveAngle(45, 0, 90)).toBeCloseTo(45);
  });
  it('方位差按 180° 对称（反向也是平行）', () => {
    expect(boreholeCaveAngle(270, 0, 90)).toBeCloseTo(0);
  });
  it('斜孔：cosθ=|cosα·cosΔA| → α=45°、ΔA=45° 时 θ=60°', () => {
    expect(boreholeCaveAngle(45, 45, 90)).toBeCloseTo(60);
  });
  it('方位平行但下倾 30° 的孔，与水平洞轴线夹角即倾角 30°', () => {
    expect(boreholeCaveAngle(90, 30, 90)).toBeCloseTo(30);
  });
});

describe('地下水自动判定', () => {
  it('水位埋深缺失 → null（未定）', () => {
    expect(autoGroundwater(50, 90, null)).toBeNull();
  });
  it('竖直孔：段底深度对比水位埋深', () => {
    expect(autoGroundwater(50, 90, 30)).toBe(true);
    expect(autoGroundwater(20, 90, 30)).toBe(false);
    expect(autoGroundwater(30, 90, 30)).toBe(true); // 恰在水位处算有水
  });
  it('倾角未填按竖直处理', () => {
    expect(autoGroundwater(50, null, 30)).toBe(true);
    expect(autoGroundwater(20, null, 30)).toBe(false);
  });
  it('斜孔按铅垂深度：50m×sin30°=25m，水位 30m → 无水', () => {
    expect(autoGroundwater(50, 30, 30)).toBe(false);
    expect(autoGroundwater(50, 30, 20)).toBe(true);
  });
  it('仰孔/水平孔恒无水', () => {
    expect(autoGroundwater(100, -30, 5)).toBe(false);
    expect(autoGroundwater(100, 0, 5)).toBe(false);
  });
});
