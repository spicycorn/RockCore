/**
 * 回次 → 岩体段归并黄金用例（对齐 1.x RockMassSegmentBuilder 行为）。
 */
import { describe, expect, it } from 'vitest';
import { buildOnePerRun, buildRockSegments, type RockMassRunInput } from '../segments';
import { RockType } from '../enums';

/** 便捷构造：depths 为 [start, end]，n 段数，lenCm 合格段总长 */
function run(key: number, start: number, end: number, n: number, lenCm: number, rockType = RockType.NotSet): RockMassRunInput {
  return { key, depthStart: start, depthEnd: end, totalPieces: n, totalLengthCm: lenCm, rockTypeKey: rockType };
}

describe('buildRockSegments 基本口径', () => {
  it('段厚=Σ进尺、S=段厚×100÷Σn、RQD=ΣL÷(段厚×100)×100', () => {
    const segs = buildRockSegments([run(0, 0, 2, 4, 150), run(1, 2, 4, 4, 170)], 1.0, 0.5);
    expect(segs).toHaveLength(1);
    const s = segs[0];
    expect(s.thicknessM).toBe(4);
    expect(s.totalPieces).toBe(8);
    expect(s.spacingCm).toBe(50);
    expect(s.rqd).toBe(80);
    expect(s.runCount).toBe(2);
  });

  it('深度不连续强制分段', () => {
    const segs = buildRockSegments([run(0, 0, 2, 4, 150), run(1, 5, 7, 4, 150)], 1.0, 0.5);
    expect(segs).toHaveLength(2);
  });

  it('岩质已知且不同强制分段；一侧未填不设限', () => {
    const segs = buildRockSegments([
      run(0, 0, 2, 4, 150, RockType.HardRock),
      run(1, 2, 4, 4, 150, RockType.SoftRock),
    ], 1.0, 0.5);
    expect(segs).toHaveLength(2);

    const segs2 = buildRockSegments([
      run(0, 0, 2, 4, 150, RockType.HardRock),
      run(1, 2, 4, 4, 150, RockType.NotSet),
    ], 1.0, 0.5);
    expect(segs2).toHaveLength(1);
    expect(segs2[0].rockTypeKey).toBe(RockType.HardRock);
  });

  it('间距相对差超容差另起一段（回次 vs 段）', () => {
    // 回次1 S=200，回次2 S=10：|200-10| > 0.5×200 → 分段
    const segs = buildRockSegments([run(0, 0, 2, 1, 150), run(1, 2, 4, 20, 150)], 1.0, 0.5);
    expect(segs).toHaveLength(2);
  });

  it('容差 ≥1 时不因间距分段', () => {
    const segs = buildRockSegments([run(0, 0, 2, 1, 150), run(1, 2, 4, 20, 150)], 1.0, 1);
    expect(segs).toHaveLength(1);
  });

  it('进尺非正的脏数据不参与归并', () => {
    const segs = buildRockSegments([run(0, 0, 2, 4, 150), run(1, 3, 3, 1, 0)], 1.0, 0.5);
    expect(segs).toHaveLength(1);
  });
});

describe('薄段处理', () => {
  it('薄回次与相邻段间距相近 → 主归并直接并入（含薄段吸收兜底）', () => {
    const segs = buildRockSegments([
      run(0, 0, 2, 4, 150),
      run(1, 2, 2.5, 1, 30),
      run(2, 2.5, 4.5, 4, 150),
    ], 1.0, 0.5);
    expect(segs).toHaveLength(1);
    expect(segs[0].thicknessM).toBe(4.5);
  });

  it('间距不相容的薄碎屑带保留为独立段（破碎带不被摊平）', () => {
    // 正常回次 S=100；碎屑带 0.5m、n=10 → S=5，|100-5| > 0.5×100 → 独立成段
    const segs = buildRockSegments([
      run(0, 0, 2, 2, 150),
      run(1, 2, 2.5, 10, 20),
      run(2, 2.5, 4.5, 2, 150),
    ], 1.0, 0.5);
    expect(segs).toHaveLength(3);
    expect(segs[1].thicknessM).toBeCloseTo(0.5, 4);
    expect(segs[1].spacingCm).toBe(5);
  });

  it('薄段并入间距相容的一侧，另一侧独立', () => {
    // 前段 S=50（0–2m, n=4）；薄段 S=50（2–2.5m, n=1）→ 并入前段；
    // 后段 S=10（2.5–4.5m, n=20）与合并段 S=50 差 40 > 25 → 独立
    const segs = buildRockSegments([
      run(0, 0, 2, 4, 150),
      run(1, 2, 2.5, 1, 30),
      run(2, 2.5, 4.5, 20, 150),
    ], 1.0, 0.5);
    expect(segs).toHaveLength(2);
    expect(segs[0].thicknessM).toBeCloseTo(2.5, 4);
    expect(segs[1].thicknessM).toBeCloseTo(2, 4);
  });
});

describe('buildOnePerRun（关闭归并）', () => {
  it('一个记录段 = 一个完整性分段', () => {
    const segs = buildOnePerRun([run(0, 0, 2, 4, 150), run(1, 2, 4, 4, 150)]);
    expect(segs).toHaveLength(2);
    expect(segs[0].isSingleRun).toBe(true);
  });
});

describe('乱序输入按深度排序归并', () => {
  it('输入乱序不影响结果', () => {
    const a = buildRockSegments([run(1, 2, 4, 4, 150), run(0, 0, 2, 4, 150)], 1.0, 0.5);
    expect(a).toHaveLength(1);
    expect(a[0].depthStart).toBe(0);
    expect(a[0].depthEnd).toBe(4);
  });
});
