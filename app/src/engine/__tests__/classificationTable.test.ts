/**
 * 表 F.0.2 围岩初步分类黄金用例：16 条规则全分支 + 缺项失败路径。
 * 期望文本与 1.x RockClassificationTable 逐字一致。
 */
import { describe, expect, it } from 'vitest';
import { classify, type ClassifyInput } from '../classificationTable';
import {
  GroundwaterCondition,
  IntegrityLevel,
  RockClass,
  RockHardnessLevel,
  RockHomogeneity,
  RockStructureType,
  RockType,
} from '../enums';

const S = RockStructureType;
const L = IntegrityLevel;
const H = RockHardnessLevel;
const G = GroundwaterCondition;
const M = RockHomogeneity;

function input(partial: Partial<ClassifyInput>): ClassifyInput {
  return {
    rockType: RockType.HardRock,
    structureType: S.NotSet,
    integrityLevel: L.Unknown,
    groundwater: G.NotSet,
    hardnessLevel: H.NotSet,
    homogeneity: M.NotSet,
    caveAxisAngleLessThan30: null,
    ...partial,
  };
}

describe('岩质未设置', () => {
  it('直接失败并提示补充', () => {
    const r = classify(input({ rockType: RockType.NotSet }));
    expect(r.success).toBe(false);
    expect(r.missingFields).toEqual(['岩质类型']);
  });
});

describe('硬质岩规则 1-10', () => {
  it('规则1：整体状·完整 → 坚硬岩 Ⅰ / 中硬岩 Ⅱ / 缺坚硬程度失败', () => {
    expect(classify(input({ structureType: S.Massive, integrityLevel: L.Intact, hardnessLevel: H.StrongRock })).rockClass).toBe(RockClass.I);
    expect(classify(input({ structureType: S.Massive, integrityLevel: L.Intact, hardnessLevel: H.MediumHardRock })).rockClass).toBe(RockClass.II);
    const fail = classify(input({ structureType: S.Massive, integrityLevel: L.Intact }));
    expect(fail.success).toBe(false);
    expect(fail.missingFields).toEqual(['岩石坚硬程度']);
    // 软岩等级与硬质岩组合非法
    expect(classify(input({ structureType: S.Massive, integrityLevel: L.Intact, hardnessLevel: H.SoftRock })).success).toBe(false);
  });

  it('规则2/3/4：块状·次块状·厚层状 × 较完整 → 坚硬岩 Ⅱ / 中硬岩 Ⅲ', () => {
    for (const st of [S.Blocky, S.SubBlocky, S.ThickLayered]) {
      expect(classify(input({ structureType: st, integrityLevel: L.RelativelyIntact, hardnessLevel: H.StrongRock })).rockClass).toBe(RockClass.II);
      expect(classify(input({ structureType: st, integrityLevel: L.RelativelyIntact, hardnessLevel: H.MediumHardRock })).rockClass).toBe(RockClass.III);
      expect(classify(input({ structureType: st, integrityLevel: L.RelativelyIntact })).success).toBe(false);
    }
  });

  it('规则5：互层状·较完整 → 夹角<30° Ⅳ，否则 Ⅲ，未设置失败', () => {
    expect(classify(input({ structureType: S.Interbedded, integrityLevel: L.RelativelyIntact, caveAxisAngleLessThan30: true })).rockClass).toBe(RockClass.IV);
    expect(classify(input({ structureType: S.Interbedded, integrityLevel: L.RelativelyIntact, caveAxisAngleLessThan30: false })).rockClass).toBe(RockClass.III);
    expect(classify(input({ structureType: S.Interbedded, integrityLevel: L.RelativelyIntact })).success).toBe(false);
  });

  it('规则6：薄层状·完整性差 → 均一无软弱夹层 Ⅲ，有软弱夹层 Ⅳ', () => {
    expect(classify(input({ structureType: S.ThinLayered, integrityLevel: L.Poor, homogeneity: M.HomogeneousNoWeakLayer })).rockClass).toBe(RockClass.III);
    expect(classify(input({ structureType: S.ThinLayered, integrityLevel: L.Poor, homogeneity: M.HasWeakLayer })).rockClass).toBe(RockClass.IV);
    expect(classify(input({ structureType: S.ThinLayered, integrityLevel: L.Poor })).success).toBe(false);
  });

  it('规则7/8：镶嵌 Ⅲ、块裂 Ⅳ', () => {
    expect(classify(input({ structureType: S.Mosaic, integrityLevel: L.Poor })).rockClass).toBe(RockClass.III);
    expect(classify(input({ structureType: S.BlockyFractured, integrityLevel: L.Poor })).rockClass).toBe(RockClass.IV);
  });

  it('规则9：碎裂·较破碎 → 有地下水 Ⅴ，无 Ⅳ，未设置失败', () => {
    expect(classify(input({ structureType: S.Cataclastic, integrityLevel: L.RelativelyBroken, groundwater: G.Wet })).rockClass).toBe(RockClass.V);
    expect(classify(input({ structureType: S.Cataclastic, integrityLevel: L.RelativelyBroken, groundwater: G.Dry })).rockClass).toBe(RockClass.IV);
    expect(classify(input({ structureType: S.Cataclastic, integrityLevel: L.RelativelyBroken })).success).toBe(false);
  });

  it('规则10：碎块状·破碎 → Ⅴ', () => {
    expect(classify(input({ structureType: S.GranularHard, integrityLevel: L.Broken })).rockClass).toBe(RockClass.V);
  });

  it('无对应组合 → 失败并提示检查组合', () => {
    const r = classify(input({ structureType: S.Massive, integrityLevel: L.Broken }));
    expect(r.success).toBe(false);
    expect(r.basis).toContain('无对应围岩类别');
  });
});

describe('软质岩规则 11-16', () => {
  const soft = (p: Partial<ClassifyInput>) => input({ rockType: RockType.SoftRock, ...p });

  it('规则11：整体状·完整 → 较软岩无地下水 Ⅲ / 有 Ⅳ；软岩 Ⅳ', () => {
    expect(classify(soft({ structureType: S.Massive, integrityLevel: L.Intact, hardnessLevel: H.RelativelySoftRock, groundwater: G.Dry })).rockClass).toBe(RockClass.III);
    expect(classify(soft({ structureType: S.Massive, integrityLevel: L.Intact, hardnessLevel: H.RelativelySoftRock, groundwater: G.Wet })).rockClass).toBe(RockClass.IV);
    expect(classify(soft({ structureType: S.Massive, integrityLevel: L.Intact, hardnessLevel: H.SoftRock, groundwater: G.Dry })).rockClass).toBe(RockClass.IV);
    // 缺坚硬程度 / 缺地下水分别失败
    expect(classify(soft({ structureType: S.Massive, integrityLevel: L.Intact, groundwater: G.Dry })).success).toBe(false);
    expect(classify(soft({ structureType: S.Massive, integrityLevel: L.Intact, hardnessLevel: H.RelativelySoftRock })).success).toBe(false);
    // 坚硬岩等级与软质岩组合非法
    expect(classify(soft({ structureType: S.Massive, integrityLevel: L.Intact, hardnessLevel: H.StrongRock, groundwater: G.Dry })).success).toBe(false);
  });

  it('规则12/13：块状或次块状、厚层互层 × 较完整 → 无地下水 Ⅳ / 有 Ⅴ', () => {
    for (const st of [S.BlockyOrSubBlocky, S.ThickOrInterbedded]) {
      expect(classify(soft({ structureType: st, integrityLevel: L.RelativelyIntact, groundwater: G.Damp })).rockClass).toBe(RockClass.IV);
      expect(classify(soft({ structureType: st, integrityLevel: L.RelativelyIntact, groundwater: G.Wet })).rockClass).toBe(RockClass.V);
      expect(classify(soft({ structureType: st, integrityLevel: L.RelativelyIntact })).success).toBe(false);
    }
  });

  it('规则14：薄层或块裂·完整性差 → 较软岩无地下水 Ⅳ，其余 Ⅴ', () => {
    expect(classify(soft({ structureType: S.ThinOrBlockyFractured, integrityLevel: L.Poor, hardnessLevel: H.RelativelySoftRock, groundwater: G.Dry })).rockClass).toBe(RockClass.IV);
    expect(classify(soft({ structureType: S.ThinOrBlockyFractured, integrityLevel: L.Poor, hardnessLevel: H.RelativelySoftRock, groundwater: G.Wet })).rockClass).toBe(RockClass.V);
    expect(classify(soft({ structureType: S.ThinOrBlockyFractured, integrityLevel: L.Poor, hardnessLevel: H.SoftRock, groundwater: G.Dry })).rockClass).toBe(RockClass.V);
    expect(classify(soft({ structureType: S.ThinOrBlockyFractured, integrityLevel: L.Poor, groundwater: G.Dry })).success).toBe(false);
  });

  it('规则15：碎裂·较破碎 → 较软岩无地下水 Ⅳ，其余 Ⅴ', () => {
    expect(classify(soft({ structureType: S.Cataclastic, integrityLevel: L.RelativelyBroken, hardnessLevel: H.RelativelySoftRock, groundwater: G.Damp })).rockClass).toBe(RockClass.IV);
    expect(classify(soft({ structureType: S.Cataclastic, integrityLevel: L.RelativelyBroken, hardnessLevel: H.RelativelySoftRock, groundwater: G.Wet })).rockClass).toBe(RockClass.V);
    expect(classify(soft({ structureType: S.Cataclastic, integrityLevel: L.RelativelyBroken, hardnessLevel: H.SoftRock, groundwater: G.Dry })).rockClass).toBe(RockClass.V);
  });

  it('规则16：碎块状散体·破碎 → Ⅴ', () => {
    expect(classify(soft({ structureType: S.GranularSoft, integrityLevel: L.Broken })).rockClass).toBe(RockClass.V);
  });
});

describe('判定依据文本', () => {
  it('成功文本含规则号与类别', () => {
    const r = classify(input({ structureType: S.Massive, integrityLevel: L.Intact, hardnessLevel: H.StrongRock }));
    expect(r.basis).toBe('硬质岩·整体状或巨厚层状结构·完整·坚硬岩 → 查表规则1 → Ⅰ类');
  });
});
