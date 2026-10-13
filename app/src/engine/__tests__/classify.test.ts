/**
 * 围岩分类管线用例：逐段查表、缺项诊断、UserOverride 跳过。
 */
import { describe, expect, it } from 'vitest';
import { classifySegments, classificationDisplayText, missingFieldsHint, type ClassifiableSegment, type SegmentF02 } from '../classify';
import {
  GroundwaterCondition,
  IntegrityLevel,
  RockClass,
  RockHardnessLevel,
  RockHomogeneity,
  RockStructureType,
  RockType,
} from '../enums';

const EMPTY_F02: SegmentF02 = {
  rockType: RockType.NotSet,
  structureType: RockStructureType.NotSet,
  hardnessLevel: RockHardnessLevel.NotSet,
  homogeneity: RockHomogeneity.NotSet,
  groundwater: GroundwaterCondition.NotSet,
  caveAxisAngleLessThan30: null,
};

const FULL_F02: SegmentF02 = {
  rockType: RockType.HardRock,
  structureType: RockStructureType.Massive,
  hardnessLevel: RockHardnessLevel.StrongRock,
  homogeneity: RockHomogeneity.HomogeneousNoWeakLayer,
  groundwater: GroundwaterCondition.Dry,
  caveAxisAngleLessThan30: false,
};

function seg(key: number, depthStart: number, depthEnd: number): ClassifiableSegment {
  return { depthStart, depthEnd, runs: [{ key }] };
}

describe('classifySegments', () => {
  it('信息齐全 → 查表成功，置信度 1.0，Basis 含规则号', () => {
    const s = classifySegments([seg(1, 0, 5)], new Map([[1, IntegrityLevel.Intact]]), () => FULL_F02);
    const c = s.classifications[0];
    expect(c.result.success).toBe(true);
    expect(c.result.rockClass).toBe(RockClass.I);
    expect(c.result.basis).toContain('查表规则1');
    expect(c.confidenceScore).toBe(1.0);
    expect(classificationDisplayText(c)).toBe('Ⅰ类');
  });

  it('等级未设置 / UserOverride → 未分类「完整性等级未设置」', () => {
    const s = classifySegments(
      [seg(1, 0, 1), seg(2, 1, 2)],
      new Map([[1, IntegrityLevel.Unknown], [2, IntegrityLevel.UserOverride]]),
      () => FULL_F02,
    );
    for (const c of s.classifications) {
      expect(c.result.success).toBe(false);
      expect(c.result.basis).toBe('完整性等级未设置');
      expect(c.confidenceScore).toBe(0);
    }
    expect(s.totalInfoSegments).toBe(0);
  });

  it('缺岩质 → 提示选硬/软岩，并按等级计入 missingRockTypeByLevel', () => {
    const s = classifySegments([seg(1, 0, 1)], new Map([[1, IntegrityLevel.RelativelyIntact]]), () => EMPTY_F02);
    const c = s.classifications[0];
    expect(c.result.basis).toBe('岩质类型未设置，请选择硬质岩或软质岩');
    expect(s.missingRockTypeByLevel[IntegrityLevel.RelativelyIntact]).toBe(1);
    expect(s.totalInfoSegments).toBe(1);
    expect(s.segmentsWithRockType).toBe(0);
  });

  it('有岩质缺结构 → 提示结构类型未设置', () => {
    const s = classifySegments(
      [seg(1, 0, 1)],
      new Map([[1, IntegrityLevel.Intact]]),
      () => ({ ...EMPTY_F02, rockType: RockType.HardRock }),
    );
    expect(s.classifications[0].result.basis).toBe('岩体结构类型未设置');
    expect(s.segmentsWithRockType).toBe(1);
    expect(s.segmentsWithStructure).toBe(0);
  });

  it('F.0.2 组合无对应行 → 失败并说明无对应围岩类别', () => {
    const s = classifySegments(
      [seg(1, 0, 1)],
      new Map([[1, IntegrityLevel.RelativelyIntact]]),
      () => ({ ...FULL_F02, structureType: RockStructureType.Cataclastic }),
    );
    expect(s.classifications[0].result.success).toBe(false);
    expect(s.classifications[0].result.basis).toContain('无对应围岩类别');
  });

  it('按 depthStart 排序输出，missingFieldsHint 汇总缺项', () => {
    const s = classifySegments(
      [seg(2, 5, 10), seg(1, 0, 5)],
      new Map<number, IntegrityLevel>([[1, IntegrityLevel.Intact], [2, IntegrityLevel.Unknown]]),
      (g) => (g.runs[0].key === 1 ? { ...FULL_F02, rockType: RockType.NotSet } : FULL_F02),
    );
    expect(s.classifications.map((c) => c.segmentKey)).toEqual([1, 2]);
    const hint = missingFieldsHint(s);
    expect(hint).toContain('1 段缺岩质类型');
    expect(hint).toContain('1 段完整性等级未设置');
  });

  it('信息齐全无缺项 → hint 为 null', () => {
    const s = classifySegments([seg(1, 0, 5)], new Map([[1, IntegrityLevel.Intact]]), () => FULL_F02);
    expect(missingFieldsHint(s)).toBeNull();
  });
});
