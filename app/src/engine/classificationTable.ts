/**
 * DL/T 5894-2025 表 F.0.2「围岩初步分类」查表实现
 * （逐行移植自 1.x RockClassificationTable，判定分支与提示文本保持一致）。
 *
 * 严格对照规范表：输入条件与表项一一对应，若信息缺失则返回失败并提示补充，
 * 不使用保守降级或硬编码默认值。
 */

import {
  GroundwaterCondition,
  IntegrityLevel,
  RockClass,
  RockHardnessLevel,
  RockHomogeneity,
  RockStructureType,
  RockType,
  integrityLevelLabel,
  rockHardnessLabel,
  rockStructureLabel,
} from './enums';

export interface ClassificationResult {
  /** 是否判定成功。输入信息不完整时为 false，此时 rockClass 无意义，应查看 missingFields 提示补充。 */
  success: boolean;
  rockClass: RockClass;
  /** 人类可读的判定依据文本 */
  basis: string;
  /** 判定失败时列出的缺失字段名 */
  missingFields: string[];
}

export interface ClassifyInput {
  rockType: RockType;
  structureType: RockStructureType;
  integrityLevel: IntegrityLevel;
  groundwater: GroundwaterCondition;
  hardnessLevel: RockHardnessLevel;
  homogeneity: RockHomogeneity;
  /** 洞轴线与岩层走向夹角是否小于 30°（null = 未设置） */
  caveAxisAngleLessThan30: boolean | null;
}

function success(rockClass: RockClass, basis: string): ClassificationResult {
  return { success: true, rockClass, basis, missingFields: [] };
}

function failure(basis: string, ...missingFields: string[]): ClassificationResult {
  return { success: false, rockClass: RockClass.I, basis, missingFields };
}

export function classify(input: ClassifyInput): ClassificationResult {
  const { rockType, structureType, integrityLevel, groundwater, hardnessLevel, homogeneity, caveAxisAngleLessThan30 } = input;

  if (rockType === RockType.HardRock) {
    return classifyHardRock(structureType, integrityLevel, groundwater, hardnessLevel, homogeneity, caveAxisAngleLessThan30);
  }
  if (rockType === RockType.SoftRock) {
    return classifySoftRock(structureType, integrityLevel, groundwater, hardnessLevel);
  }
  return failure('岩质类型未设置', '岩质类型');
}

function classifyHardRock(
  structureType: RockStructureType,
  integrityLevel: IntegrityLevel,
  groundwater: GroundwaterCondition,
  hardnessLevel: RockHardnessLevel,
  homogeneity: RockHomogeneity,
  caveAxisAngleLessThan30: boolean | null,
): ClassificationResult {
  const hasGroundwater = groundwater === GroundwaterCondition.Wet;
  const S = RockStructureType;
  const L = IntegrityLevel;
  const H = RockHardnessLevel;

  switch (true) {
    case structureType === S.Massive && integrityLevel === L.Intact:
      // 规则1：坚硬岩 Ⅰ 类，中硬岩 Ⅱ 类
      if (hardnessLevel === H.NotSet)
        return failure('硬质岩·整体状或巨厚层状结构·完整，需补充岩石坚硬程度以区分 Ⅰ/Ⅱ 类', '岩石坚硬程度');
      if (hardnessLevel === H.StrongRock)
        return success(RockClass.I, '硬质岩·整体状或巨厚层状结构·完整·坚硬岩 → 查表规则1 → Ⅰ类');
      if (hardnessLevel === H.MediumHardRock)
        return success(RockClass.II, '硬质岩·整体状或巨厚层状结构·完整·中硬岩 → 查表规则1 → Ⅱ类');
      return failure(`硬质岩·整体状或巨厚层状结构·完整·${rockHardnessLabel[hardnessLevel]}：坚硬程度应为坚硬岩或中硬岩`, '岩石坚硬程度');

    case structureType === S.Blocky && integrityLevel === L.RelativelyIntact:
      // 规则2：坚硬岩 Ⅱ 类，中硬岩 Ⅲ 类
      if (hardnessLevel === H.NotSet)
        return failure('硬质岩·块状结构·较完整，需补充岩石坚硬程度以区分 Ⅱ/Ⅲ 类', '岩石坚硬程度');
      if (hardnessLevel === H.StrongRock)
        return success(RockClass.II, '硬质岩·块状结构·较完整·坚硬岩 → 查表规则2 → Ⅱ类');
      if (hardnessLevel === H.MediumHardRock)
        return success(RockClass.III, '硬质岩·块状结构·较完整·中硬岩 → 查表规则2 → Ⅲ类');
      return failure(`硬质岩·块状结构·较完整·${rockHardnessLabel[hardnessLevel]}：坚硬程度应为坚硬岩或中硬岩`, '岩石坚硬程度');

    case structureType === S.SubBlocky && integrityLevel === L.RelativelyIntact:
      // 规则3：坚硬岩 Ⅱ 类，中硬岩 Ⅲ 类
      if (hardnessLevel === H.NotSet)
        return failure('硬质岩·次块状结构·较完整，需补充岩石坚硬程度以区分 Ⅱ/Ⅲ 类', '岩石坚硬程度');
      if (hardnessLevel === H.StrongRock)
        return success(RockClass.II, '硬质岩·次块状结构·较完整·坚硬岩 → 查表规则3 → Ⅱ类');
      if (hardnessLevel === H.MediumHardRock)
        return success(RockClass.III, '硬质岩·次块状结构·较完整·中硬岩 → 查表规则3 → Ⅲ类');
      return failure(`硬质岩·次块状结构·较完整·${rockHardnessLabel[hardnessLevel]}：坚硬程度应为坚硬岩或中硬岩`, '岩石坚硬程度');

    case structureType === S.ThickLayered && integrityLevel === L.RelativelyIntact:
      // 规则4：坚硬岩 Ⅱ 类，中硬岩 Ⅲ 类
      if (hardnessLevel === H.NotSet)
        return failure('硬质岩·厚层状或中厚层状结构·较完整，需补充岩石坚硬程度以区分 Ⅱ/Ⅲ 类', '岩石坚硬程度');
      if (hardnessLevel === H.StrongRock)
        return success(RockClass.II, '硬质岩·厚层状或中厚层状结构·较完整·坚硬岩 → 查表规则4 → Ⅱ类');
      if (hardnessLevel === H.MediumHardRock)
        return success(RockClass.III, '硬质岩·厚层状或中厚层状结构·较完整·中硬岩 → 查表规则4 → Ⅲ类');
      return failure(`硬质岩·厚层状或中厚层状结构·较完整·${rockHardnessLabel[hardnessLevel]}：坚硬程度应为坚硬岩或中硬岩`, '岩石坚硬程度');

    case structureType === S.Interbedded && integrityLevel === L.RelativelyIntact:
      // 规则5：洞轴线与岩层走向夹角小于30°时定 Ⅳ 类
      if (caveAxisAngleLessThan30 == null)
        return failure('硬质岩·互层状结构·较完整，需明确洞轴线与岩层走向夹角是否小于30°以区分 Ⅲ/Ⅳ 类', '洞轴线夹角');
      if (caveAxisAngleLessThan30 === true)
        return success(RockClass.IV, '硬质岩·互层状结构·较完整·洞轴线与岩层走向夹角小于30° → 查表规则5 → Ⅳ类');
      return success(RockClass.III, '硬质岩·互层状结构·较完整·洞轴线与岩层走向夹角大于等于30° → 查表规则5 → Ⅲ类');

    case structureType === S.ThinLayered && integrityLevel === L.Poor:
      // 规则6：岩质均一、无软弱夹层时定 Ⅲ 类
      if (homogeneity === RockHomogeneity.NotSet)
        return failure('硬质岩·薄层状结构·完整性差，需明确岩质均一性（有无软弱夹层）以区分 Ⅲ/Ⅳ 类', '岩质均一性');
      if (homogeneity === RockHomogeneity.HomogeneousNoWeakLayer)
        return success(RockClass.III, '硬质岩·薄层状结构·完整性差·岩质均一无软弱夹层 → 查表规则6 → Ⅲ类');
      return success(RockClass.IV, '硬质岩·薄层状结构·完整性差·有软弱夹层 → 查表规则6 → Ⅳ类');

    case structureType === S.Mosaic && integrityLevel === L.Poor:
      // 规则7
      return success(RockClass.III, '硬质岩·镶嵌结构·完整性差 → 查表规则7 → Ⅲ类');

    case structureType === S.BlockyFractured && integrityLevel === L.Poor:
      // 规则8
      return success(RockClass.IV, '硬质岩·块裂结构·完整性差 → 查表规则8 → Ⅳ类');

    case structureType === S.Cataclastic && integrityLevel === L.RelativelyBroken:
      // 规则9：有地下水时定 Ⅴ 类
      if (groundwater === GroundwaterCondition.NotSet)
        return failure('硬质岩·碎裂结构·较破碎，需明确地下水状态以区分 Ⅳ/Ⅴ 类', '地下水状态');
      if (hasGroundwater)
        return success(RockClass.V, '硬质岩·碎裂结构·较破碎·有地下水 → 查表规则9 → Ⅴ类');
      return success(RockClass.IV, '硬质岩·碎裂结构·较破碎·无地下水 → 查表规则9 → Ⅳ类');

    case structureType === S.GranularHard && integrityLevel === L.Broken:
      // 规则10
      return success(RockClass.V, '硬质岩·碎块状或碎屑状结构·破碎 → 查表规则10 → Ⅴ类');
  }

  return failure(
    `硬质岩·${rockStructureLabel[structureType]}·${integrityLevelLabel[integrityLevel]} 在表 F.0.2 中无对应围岩类别，请检查岩体结构类型与完整性等级组合`,
    '岩体结构类型/完整性等级',
  );
}

function classifySoftRock(
  structureType: RockStructureType,
  integrityLevel: IntegrityLevel,
  groundwater: GroundwaterCondition,
  hardnessLevel: RockHardnessLevel,
): ClassificationResult {
  const hasGroundwater = groundwater === GroundwaterCondition.Wet;
  const S = RockStructureType;
  const L = IntegrityLevel;
  const H = RockHardnessLevel;

  switch (true) {
    case structureType === S.Massive && integrityLevel === L.Intact:
      // 规则11：较软岩无地下水时定 Ⅲ 类，有地下水时定 Ⅳ 类；软岩定 Ⅳ 类
      if (hardnessLevel === H.NotSet)
        return failure('软质岩·整体状或巨厚层状结构·完整，需补充岩石坚硬程度（较软岩/软岩）以区分 Ⅲ/Ⅳ 类', '岩石坚硬程度');
      if (groundwater === GroundwaterCondition.NotSet)
        return failure('软质岩·整体状或巨厚层状结构·完整，需明确地下水状态以区分 Ⅲ/Ⅳ 类', '地下水状态');
      if (hardnessLevel === H.RelativelySoftRock) {
        return hasGroundwater
          ? success(RockClass.IV, '软质岩·整体状或巨厚层状结构·完整·较软岩·有地下水 → 查表规则11 → Ⅳ类')
          : success(RockClass.III, '软质岩·整体状或巨厚层状结构·完整·较软岩·无地下水 → 查表规则11 → Ⅲ类');
      }
      if (hardnessLevel === H.SoftRock) {
        return success(RockClass.IV, '软质岩·整体状或巨厚层状结构·完整·软岩 → 查表规则11 → Ⅳ类');
      }
      return failure(`软质岩·整体状或巨厚层状结构·完整·${rockHardnessLabel[hardnessLevel]}：坚硬程度应为较软岩或软岩`, '岩石坚硬程度');

    case structureType === S.BlockyOrSubBlocky && integrityLevel === L.RelativelyIntact:
      // 规则12：无地下水时定 Ⅳ 类；有地下水时定 Ⅴ 类
      if (groundwater === GroundwaterCondition.NotSet)
        return failure('软质岩·块状或次块状结构·较完整，需明确地下水状态以区分 Ⅳ/Ⅴ 类', '地下水状态');
      return hasGroundwater
        ? success(RockClass.V, '软质岩·块状或次块状结构·较完整·有地下水 → 查表规则12 → Ⅴ类')
        : success(RockClass.IV, '软质岩·块状或次块状结构·较完整·无地下水 → 查表规则12 → Ⅳ类');

    case structureType === S.ThickOrInterbedded && integrityLevel === L.RelativelyIntact:
      // 规则13：无地下水时定 Ⅳ 类；有地下水时定 Ⅴ 类
      if (groundwater === GroundwaterCondition.NotSet)
        return failure('软质岩·厚层、中厚层或互层状结构·较完整，需明确地下水状态以区分 Ⅳ/Ⅴ 类', '地下水状态');
      return hasGroundwater
        ? success(RockClass.V, '软质岩·厚层、中厚层或互层状结构·较完整·有地下水 → 查表规则13 → Ⅴ类')
        : success(RockClass.IV, '软质岩·厚层、中厚层或互层状结构·较完整·无地下水 → 查表规则13 → Ⅳ类');

    case structureType === S.ThinOrBlockyFractured && integrityLevel === L.Poor:
      // 规则14：较软岩无地下水时定 Ⅳ 类
      if (hardnessLevel === H.NotSet)
        return failure('软质岩·薄层状或块裂结构·完整性差，需补充岩石坚硬程度（较软岩/软岩）以区分 Ⅳ/Ⅴ 类', '岩石坚硬程度');
      if (groundwater === GroundwaterCondition.NotSet)
        return failure('软质岩·薄层状或块裂结构·完整性差，需明确地下水状态以区分 Ⅳ/Ⅴ 类', '地下水状态');
      if (hardnessLevel === H.RelativelySoftRock && !hasGroundwater)
        return success(RockClass.IV, '软质岩·薄层状或块裂结构·完整性差·较软岩·无地下水 → 查表规则14 → Ⅳ类');
      return success(RockClass.V, `软质岩·薄层状或块裂结构·完整性差·${rockHardnessLabel[hardnessLevel]}·${hasGroundwater ? '有地下水' : '无地下水'} → 查表规则14 → Ⅴ类`);

    case structureType === S.Cataclastic && integrityLevel === L.RelativelyBroken:
      // 规则15：较软岩无地下水时定 Ⅳ 类
      if (hardnessLevel === H.NotSet)
        return failure('软质岩·碎裂结构·较破碎，需补充岩石坚硬程度（较软岩/软岩）以区分 Ⅳ/Ⅴ 类', '岩石坚硬程度');
      if (groundwater === GroundwaterCondition.NotSet)
        return failure('软质岩·碎裂结构·较破碎，需明确地下水状态以区分 Ⅳ/Ⅴ 类', '地下水状态');
      if (hardnessLevel === H.RelativelySoftRock && !hasGroundwater)
        return success(RockClass.IV, '软质岩·碎裂结构·较破碎·较软岩·无地下水 → 查表规则15 → Ⅳ类');
      return success(RockClass.V, `软质岩·碎裂结构·较破碎·${rockHardnessLabel[hardnessLevel]}·${hasGroundwater ? '有地下水' : '无地下水'} → 查表规则15 → Ⅴ类`);

    case structureType === S.GranularSoft && integrityLevel === L.Broken:
      // 规则16
      return success(RockClass.V, '软质岩·碎块状或碎屑状散体结构·破碎 → 查表规则16 → Ⅴ类');
  }

  return failure(
    `软质岩·${rockStructureLabel[structureType]}·${integrityLevelLabel[integrityLevel]} 在表 F.0.2 中无对应围岩类别，请检查岩体结构类型与完整性等级组合`,
    '岩体结构类型/完整性等级',
  );
}
