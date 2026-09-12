using CommunityToolkit.Mvvm.ComponentModel;
using RockCore.Core.Enums;

namespace RockCore.Core.Models;

/// <summary>
/// 钻孔完整性等级分组（阶段四核心数据模型）。
/// 由阶段三分析结果自动聚合生成，每个实例代表一个连续深度范围的完整性等级分段。
/// 用户可为每个分段设置岩质类型、岩体结构类型、坚硬程度等属性，用于阶段五围岩分类。
/// </summary>
public partial class BoreholeIntegritySegment : ObservableObject
{
    public int Id { get; set; }

    public int BoreholeId { get; set; }

    /// <summary>深度起点（米）</summary>
    public double DepthStart { get; set; }

    /// <summary>深度终点（米）</summary>
    public double DepthEnd { get; set; }

    /// <summary>完整性等级：完整/较完整/完整性差/较破碎/破碎</summary>
    public IntegrityLevel IntegrityLevel { get; set; }

    /// <summary>岩质类型：硬质岩/软质岩</summary>
    [ObservableProperty]
    private RockType _rockType;

    /// <summary>岩体结构类型，级联于 RockType + IntegrityLevel</summary>
    [ObservableProperty]
    private RockStructureType _rockStructureType;

    /// <summary>岩石坚硬程度细分：坚硬岩/中硬岩/较软岩/软岩</summary>
    [ObservableProperty]
    private RockHardnessLevel _rockHardnessLevel;

    /// <summary>岩质均一性：均一无软弱夹层/有软弱夹层</summary>
    [ObservableProperty]
    private RockHomogeneity _rockHomogeneity;

    /// <summary>地下水条件：干燥/潮湿/湿润</summary>
    [ObservableProperty]
    private GroundwaterCondition _groundwaterCondition;

    /// <summary>洞轴线夹角是否小于30°，仅当岩质=硬质岩且岩体结构=互层状结构时触发</summary>
    [ObservableProperty]
    private bool? _caveAxisAngleLessThan30;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 当前岩质类型 + 完整性等级 对应的可用岩体结构类型列表（严格对应规范表F.0.2）
    /// </summary>
    public RockStructureType[] AvailableRockStructureTypes
    {
        get
        {
            if (RockType == RockType.NotSet)
                return RockStructureMapping.AllStructures;
            return RockStructureMapping.GetStructures(RockType, IntegrityLevel);
        }
    }

    // RockType 变化时，刷新可用岩体结构列表，并清空不兼容的当前值
    partial void OnRockTypeChanged(RockType value)
    {
        OnPropertyChanged(nameof(AvailableRockStructureTypes));
        if (RockStructureType != RockStructureType.NotSet &&
            !AvailableRockStructureTypes.Contains(RockStructureType))
        {
            RockStructureType = RockStructureType.NotSet;
        }
    }
}
