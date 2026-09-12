using System.ComponentModel;

namespace RockCore.Core.Enums;

/// <summary>
/// 岩体结构类型，对应规范表 F.0.2 围岩初步分类中的岩体结构类型列。
/// 硬质岩10种，软质岩6种，其中2种描述完全相同（整体状或巨厚层状结构、碎裂结构）。
/// </summary>
public enum RockStructureType
{
    [Description("未设置")]
    NotSet = 0,

    // ===== 硬质岩 + 软质岩共享（2种）=====
    [Description("整体状或巨厚层状结构")]
    Massive,

    [Description("碎裂结构")]
    Cataclastic,

    // ===== 硬质岩独有（8种）=====
    [Description("块状结构")]
    Blocky,

    [Description("次块状结构")]
    SubBlocky,

    [Description("厚层状或中厚层状结构")]
    ThickLayered,

    [Description("互层状结构")]
    Interbedded,

    [Description("薄层状结构")]
    ThinLayered,

    [Description("镶嵌结构")]
    Mosaic,

    [Description("块裂结构")]
    BlockyFractured,

    [Description("碎块状或碎屑状结构")]
    GranularHard,

    // ===== 软质岩独有（4种）=====
    [Description("块状或次块状结构")]
    BlockyOrSubBlocky,

    [Description("厚层、中厚层或互层状结构")]
    ThickOrInterbedded,

    [Description("薄层状或块裂结构")]
    ThinOrBlockyFractured,

    [Description("碎块状或碎屑状散体结构")]
    GranularSoft
}
