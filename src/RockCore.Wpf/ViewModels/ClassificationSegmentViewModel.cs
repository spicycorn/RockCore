using CommunityToolkit.Mvvm.ComponentModel;
using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Wpf.ViewModels;

/// <summary>
/// 分析段（三段融合结果）ViewModel。
/// </summary>
public partial class ClassificationSegmentViewModel : ObservableObject
{
    private readonly ClassificationSegment _segment;

    public ClassificationSegmentViewModel(ClassificationSegment segment)
    {
        _segment = segment;
    }

    public int Id => _segment.Id;

    public int BoreholeId => _segment.BoreholeId;

    public double DepthStart => _segment.DepthStart;

    public double DepthEnd => _segment.DepthEnd;

    public double Length => _segment.DepthEnd - _segment.DepthStart;

    public RockClass RockClass => _segment.RockClass;

    public string RockClassText => _segment.RockClass switch
    {
        RockClass.I => "I 类",
        RockClass.II => "II 类",
        RockClass.III => "III 类",
        RockClass.IV => "IV 类",
        RockClass.V => "V 类",
        _ => _segment.RockClass.ToString()
    };

    public IntegrityLevel IntegrityLevel => _segment.IntegrityLevel;

    public string IntegrityLevelText => _segment.IntegrityLevel switch
    {
        IntegrityLevel.Intact => "完整",
        IntegrityLevel.RelativelyIntact => "较完整",
        IntegrityLevel.Poor => "完整性差",
        IntegrityLevel.RelativelyBroken => "较破碎",
        IntegrityLevel.Broken => "破碎",
        _ => _segment.IntegrityLevel.ToString()
    };

    public RockType RockType => _segment.RockType;

    public string RockTypeText => _segment.RockType switch
    {
        RockType.HardRock => "硬质岩",
        RockType.SoftRock => "软质岩",
        _ => _segment.RockType.ToString()
    };

    public RockStructureType RockStructureType => _segment.RockStructureType;

    public string RockStructureTypeText => _segment.RockStructureType switch
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
        _ => _segment.RockStructureType.ToString()
    };

    public GroundwaterCondition GroundwaterCondition => _segment.GroundwaterCondition;

    public string GroundwaterText => _segment.GroundwaterCondition switch
    {
        GroundwaterCondition.Dry => "干燥",
        GroundwaterCondition.Damp => "潮湿/渗水",
        GroundwaterCondition.Wet => "滴水/有地下水",
        _ => "未设置"
    };

    public double ConfidenceScore => _segment.ConfidenceScore;

    public string Basis => _segment.Basis;
}
