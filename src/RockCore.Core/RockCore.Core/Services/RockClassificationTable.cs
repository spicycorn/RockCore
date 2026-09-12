using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Core.Services;

/// <summary>
/// DL/T 5894-2025 表 F.0.2「围岩初步分类」的查表实现。
/// 严格对照规范表：输入条件与表项一一对应，若信息缺失则返回失败并提示补充，
/// 不使用保守降级或硬编码默认值。
/// </summary>
public static class RockClassificationTable
{
    /// <summary>
    /// 根据岩质、岩体结构、完整性等级、地下水、坚硬程度、均一性、洞轴线夹角判定围岩类别。
    /// 当必要信息缺失时，返回 Success=false，并在 MissingFields 中列出缺失项。
    /// </summary>
    public static ClassificationResult Classify(
        RockType rockType,
        RockStructureType structureType,
        IntegrityLevel integrityLevel,
        GroundwaterCondition groundwater,
        RockHardnessLevel hardnessLevel,
        RockHomogeneity homogeneity,
        bool? caveAxisAngleLessThan30)
    {
        bool hasGroundwater = groundwater == GroundwaterCondition.Wet;

        if (rockType == RockType.HardRock)
        {
            return ClassifyHardRock(structureType, integrityLevel, groundwater, hardnessLevel, homogeneity, caveAxisAngleLessThan30);
        }

        if (rockType == RockType.SoftRock)
        {
            return ClassifySoftRock(structureType, integrityLevel, groundwater, hardnessLevel);
        }

        return Failure("岩质类型未设置", "岩质类型");
    }

    private static ClassificationResult ClassifyHardRock(
        RockStructureType structureType,
        IntegrityLevel integrityLevel,
        GroundwaterCondition groundwater,
        RockHardnessLevel hardnessLevel,
        RockHomogeneity homogeneity,
        bool? caveAxisAngleLessThan30)
    {
        bool hasGroundwater = groundwater == GroundwaterCondition.Wet;

        switch (structureType)
        {
            case RockStructureType.Massive when integrityLevel == IntegrityLevel.Intact:
                // 规则1：坚硬岩 I 类，中硬岩 II 类
                if (hardnessLevel == RockHardnessLevel.NotSet)
                    return Failure("硬质岩·整体状或巨厚层状结构·完整，需补充岩石坚硬程度以区分 I/II 类", "岩石坚硬程度");
                if (hardnessLevel == RockHardnessLevel.StrongRock)
                    return Success(RockClass.I, "硬质岩·整体状或巨厚层状结构·完整·坚硬岩 → 查表规则1 → I类");
                if (hardnessLevel == RockHardnessLevel.MediumHardRock)
                    return Success(RockClass.II, "硬质岩·整体状或巨厚层状结构·完整·中硬岩 → 查表规则1 → II类");
                return Failure($"硬质岩·整体状或巨厚层状结构·完整·{HardnessText(hardnessLevel)}：坚硬程度应为坚硬岩或中硬岩", "岩石坚硬程度");

            case RockStructureType.Blocky when integrityLevel == IntegrityLevel.RelativelyIntact:
                // 规则2：坚硬岩 II 类，中硬岩 III 类
                if (hardnessLevel == RockHardnessLevel.NotSet)
                    return Failure("硬质岩·块状结构·较完整，需补充岩石坚硬程度以区分 II/III 类", "岩石坚硬程度");
                if (hardnessLevel == RockHardnessLevel.StrongRock)
                    return Success(RockClass.II, "硬质岩·块状结构·较完整·坚硬岩 → 查表规则2 → II类");
                if (hardnessLevel == RockHardnessLevel.MediumHardRock)
                    return Success(RockClass.III, "硬质岩·块状结构·较完整·中硬岩 → 查表规则2 → III类");
                return Failure($"硬质岩·块状结构·较完整·{HardnessText(hardnessLevel)}：坚硬程度应为坚硬岩或中硬岩", "岩石坚硬程度");

            case RockStructureType.SubBlocky when integrityLevel == IntegrityLevel.RelativelyIntact:
                // 规则3：坚硬岩 II 类，中硬岩 III 类
                if (hardnessLevel == RockHardnessLevel.NotSet)
                    return Failure("硬质岩·次块状结构·较完整，需补充岩石坚硬程度以区分 II/III 类", "岩石坚硬程度");
                if (hardnessLevel == RockHardnessLevel.StrongRock)
                    return Success(RockClass.II, "硬质岩·次块状结构·较完整·坚硬岩 → 查表规则3 → II类");
                if (hardnessLevel == RockHardnessLevel.MediumHardRock)
                    return Success(RockClass.III, "硬质岩·次块状结构·较完整·中硬岩 → 查表规则3 → III类");
                return Failure($"硬质岩·次块状结构·较完整·{HardnessText(hardnessLevel)}：坚硬程度应为坚硬岩或中硬岩", "岩石坚硬程度");

            case RockStructureType.ThickLayered when integrityLevel == IntegrityLevel.RelativelyIntact:
                // 规则4：坚硬岩 II 类，中硬岩 III 类
                if (hardnessLevel == RockHardnessLevel.NotSet)
                    return Failure("硬质岩·厚层状或中厚层状结构·较完整，需补充岩石坚硬程度以区分 II/III 类", "岩石坚硬程度");
                if (hardnessLevel == RockHardnessLevel.StrongRock)
                    return Success(RockClass.II, "硬质岩·厚层状或中厚层状结构·较完整·坚硬岩 → 查表规则4 → II类");
                if (hardnessLevel == RockHardnessLevel.MediumHardRock)
                    return Success(RockClass.III, "硬质岩·厚层状或中厚层状结构·较完整·中硬岩 → 查表规则4 → III类");
                return Failure($"硬质岩·厚层状或中厚层状结构·较完整·{HardnessText(hardnessLevel)}：坚硬程度应为坚硬岩或中硬岩", "岩石坚硬程度");

            case RockStructureType.Interbedded when integrityLevel == IntegrityLevel.RelativelyIntact:
                // 规则5：洞轴线与岩层走向夹角小于30°时定 IV 类
                if (caveAxisAngleLessThan30 == null)
                    return Failure("硬质岩·互层状结构·较完整，需明确洞轴线与岩层走向夹角是否小于30°以区分 III/IV 类", "洞轴线夹角");
                if (caveAxisAngleLessThan30 == true)
                    return Success(RockClass.IV, "硬质岩·互层状结构·较完整·洞轴线与岩层走向夹角小于30° → 查表规则5 → IV类");
                return Success(RockClass.III, "硬质岩·互层状结构·较完整·洞轴线与岩层走向夹角大于等于30° → 查表规则5 → III类");

            case RockStructureType.ThinLayered when integrityLevel == IntegrityLevel.Poor:
                // 规则6：岩质均一、无软弱夹层时定 III 类
                if (homogeneity == RockHomogeneity.NotSet)
                    return Failure("硬质岩·薄层状结构·完整性差，需明确岩质均一性（有无软弱夹层）以区分 III/IV 类", "岩质均一性");
                if (homogeneity == RockHomogeneity.HomogeneousNoWeakLayer)
                    return Success(RockClass.III, "硬质岩·薄层状结构·完整性差·岩质均一无软弱夹层 → 查表规则6 → III类");
                return Success(RockClass.IV, "硬质岩·薄层状结构·完整性差·有软弱夹层 → 查表规则6 → IV类");

            case RockStructureType.Mosaic when integrityLevel == IntegrityLevel.Poor:
                // 规则7
                return Success(RockClass.III, "硬质岩·镶嵌结构·完整性差 → 查表规则7 → III类");

            case RockStructureType.BlockyFractured when integrityLevel == IntegrityLevel.Poor:
                // 规则8
                return Success(RockClass.IV, "硬质岩·块裂结构·完整性差 → 查表规则8 → IV类");

            case RockStructureType.Cataclastic when integrityLevel == IntegrityLevel.RelativelyBroken:
                // 规则9：有地下水时定 V 类
                if (groundwater == GroundwaterCondition.NotSet)
                    return Failure("硬质岩·碎裂结构·较破碎，需明确地下水状态以区分 IV/V 类", "地下水状态");
                if (hasGroundwater)
                    return Success(RockClass.V, "硬质岩·碎裂结构·较破碎·有地下水 → 查表规则9 → V类");
                return Success(RockClass.IV, "硬质岩·碎裂结构·较破碎·无地下水 → 查表规则9 → IV类");

            case RockStructureType.GranularHard when integrityLevel == IntegrityLevel.Broken:
                // 规则10
                return Success(RockClass.V, "硬质岩·碎块状或碎屑状结构·破碎 → 查表规则10 → V类");
        }

        return Failure(
            $"硬质岩·{StructureText(structureType)}·{IntegrityText(integrityLevel)} 在表 F.0.2 中无对应围岩类别，请检查岩体结构类型与完整性等级组合",
            "岩体结构类型/完整性等级");
    }

    private static ClassificationResult ClassifySoftRock(
        RockStructureType structureType,
        IntegrityLevel integrityLevel,
        GroundwaterCondition groundwater,
        RockHardnessLevel hardnessLevel)
    {
        bool hasGroundwater = groundwater == GroundwaterCondition.Wet;

        switch (structureType)
        {
            case RockStructureType.Massive when integrityLevel == IntegrityLevel.Intact:
                // 规则11：较软岩无地下水时定 III 类，有地下水时定 IV 类；软岩定 IV 类
                if (hardnessLevel == RockHardnessLevel.NotSet)
                    return Failure("软质岩·整体状或巨厚层状结构·完整，需补充岩石坚硬程度（较软岩/软岩）以区分 III/IV 类", "岩石坚硬程度");
                if (groundwater == GroundwaterCondition.NotSet)
                    return Failure("软质岩·整体状或巨厚层状结构·完整，需明确地下水状态以区分 III/IV 类", "地下水状态");
                if (hardnessLevel == RockHardnessLevel.RelativelySoftRock)
                {
                    return hasGroundwater
                        ? Success(RockClass.IV, "软质岩·整体状或巨厚层状结构·完整·较软岩·有地下水 → 查表规则11 → IV类")
                        : Success(RockClass.III, "软质岩·整体状或巨厚层状结构·完整·较软岩·无地下水 → 查表规则11 → III类");
                }
                if (hardnessLevel == RockHardnessLevel.SoftRock)
                {
                    return Success(RockClass.IV, "软质岩·整体状或巨厚层状结构·完整·软岩 → 查表规则11 → IV类");
                }
                return Failure($"软质岩·整体状或巨厚层状结构·完整·{HardnessText(hardnessLevel)}：坚硬程度应为较软岩或软岩", "岩石坚硬程度");

            case RockStructureType.BlockyOrSubBlocky when integrityLevel == IntegrityLevel.RelativelyIntact:
                // 规则12：无地下水时定 IV 类；有地下水时定 V 类
                if (groundwater == GroundwaterCondition.NotSet)
                    return Failure("软质岩·块状或次块状结构·较完整，需明确地下水状态以区分 IV/V 类", "地下水状态");
                return hasGroundwater
                    ? Success(RockClass.V, "软质岩·块状或次块状结构·较完整·有地下水 → 查表规则12 → V类")
                    : Success(RockClass.IV, "软质岩·块状或次块状结构·较完整·无地下水 → 查表规则12 → IV类");

            case RockStructureType.ThickOrInterbedded when integrityLevel == IntegrityLevel.RelativelyIntact:
                // 规则13：无地下水时定 IV 类；有地下水时定 V 类
                if (groundwater == GroundwaterCondition.NotSet)
                    return Failure("软质岩·厚层、中厚层或互层状结构·较完整，需明确地下水状态以区分 IV/V 类", "地下水状态");
                return hasGroundwater
                    ? Success(RockClass.V, "软质岩·厚层、中厚层或互层状结构·较完整·有地下水 → 查表规则13 → V类")
                    : Success(RockClass.IV, "软质岩·厚层、中厚层或互层状结构·较完整·无地下水 → 查表规则13 → IV类");

            case RockStructureType.ThinOrBlockyFractured when integrityLevel == IntegrityLevel.Poor:
                // 规则14：较软岩无地下水时定 IV 类
                if (hardnessLevel == RockHardnessLevel.NotSet)
                    return Failure("软质岩·薄层状或块裂结构·完整性差，需补充岩石坚硬程度（较软岩/软岩）以区分 IV/V 类", "岩石坚硬程度");
                if (groundwater == GroundwaterCondition.NotSet)
                    return Failure("软质岩·薄层状或块裂结构·完整性差，需明确地下水状态以区分 IV/V 类", "地下水状态");
                if (hardnessLevel == RockHardnessLevel.RelativelySoftRock && !hasGroundwater)
                    return Success(RockClass.IV, "软质岩·薄层状或块裂结构·完整性差·较软岩·无地下水 → 查表规则14 → IV类");
                return Success(RockClass.V, $"软质岩·薄层状或块裂结构·完整性差·{HardnessText(hardnessLevel)}·{(hasGroundwater ? "有地下水" : "无地下水")} → 查表规则14 → V类");

            case RockStructureType.Cataclastic when integrityLevel == IntegrityLevel.RelativelyBroken:
                // 规则15：较软岩无地下水时定 IV 类
                if (hardnessLevel == RockHardnessLevel.NotSet)
                    return Failure("软质岩·碎裂结构·较破碎，需补充岩石坚硬程度（较软岩/软岩）以区分 IV/V 类", "岩石坚硬程度");
                if (groundwater == GroundwaterCondition.NotSet)
                    return Failure("软质岩·碎裂结构·较破碎，需明确地下水状态以区分 IV/V 类", "地下水状态");
                if (hardnessLevel == RockHardnessLevel.RelativelySoftRock && !hasGroundwater)
                    return Success(RockClass.IV, "软质岩·碎裂结构·较破碎·较软岩·无地下水 → 查表规则15 → IV类");
                return Success(RockClass.V, $"软质岩·碎裂结构·较破碎·{HardnessText(hardnessLevel)}·{(hasGroundwater ? "有地下水" : "无地下水")} → 查表规则15 → V类");

            case RockStructureType.GranularSoft when integrityLevel == IntegrityLevel.Broken:
                // 规则16
                return Success(RockClass.V, "软质岩·碎块状或碎屑状散体结构·破碎 → 查表规则16 → V类");
        }

        return Failure(
            $"软质岩·{StructureText(structureType)}·{IntegrityText(integrityLevel)} 在表 F.0.2 中无对应围岩类别，请检查岩体结构类型与完整性等级组合",
            "岩体结构类型/完整性等级");
    }

    private static ClassificationResult Success(RockClass rockClass, string basis)
    {
        return new ClassificationResult
        {
            Success = true,
            RockClass = rockClass,
            Basis = basis
        };
    }

    private static ClassificationResult Failure(string basis, params string[] missingFields)
    {
        return new ClassificationResult
        {
            Success = false,
            Basis = basis,
            MissingFields = missingFields.ToList()
        };
    }

    private static string StructureText(RockStructureType structureType)
    {
        return structureType switch
        {
            RockStructureType.Massive => "整体状或巨厚层状结构",
            RockStructureType.Blocky => "块状结构",
            RockStructureType.SubBlocky => "次块状结构",
            RockStructureType.ThickLayered => "厚层状或中厚层状结构",
            RockStructureType.Interbedded => "互层状结构",
            RockStructureType.ThinLayered => "薄层状结构",
            RockStructureType.Mosaic => "镶嵌结构",
            RockStructureType.BlockyFractured => "块裂结构",
            RockStructureType.Cataclastic => "碎裂结构",
            RockStructureType.GranularHard => "碎块状或碎屑状结构",
            RockStructureType.BlockyOrSubBlocky => "块状或次块状结构",
            RockStructureType.ThickOrInterbedded => "厚层、中厚层或互层状结构",
            RockStructureType.ThinOrBlockyFractured => "薄层状或块裂结构",
            RockStructureType.GranularSoft => "碎块状或碎屑状散体结构",
            _ => structureType.ToString()
        };
    }

    private static string IntegrityText(IntegrityLevel level)
    {
        return level switch
        {
            IntegrityLevel.Intact => "完整",
            IntegrityLevel.RelativelyIntact => "较完整",
            IntegrityLevel.Poor => "完整性差",
            IntegrityLevel.RelativelyBroken => "较破碎",
            IntegrityLevel.Broken => "破碎",
            _ => level.ToString()
        };
    }

    private static string HardnessText(RockHardnessLevel hardness)
    {
        return hardness switch
        {
            RockHardnessLevel.StrongRock => "坚硬岩",
            RockHardnessLevel.MediumHardRock => "中硬岩",
            RockHardnessLevel.RelativelySoftRock => "较软岩",
            RockHardnessLevel.SoftRock => "软岩",
            _ => "未设置"
        };
    }
}
