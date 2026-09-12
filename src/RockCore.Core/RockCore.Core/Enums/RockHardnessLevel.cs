using System.ComponentModel;

namespace RockCore.Core.Enums;

/// <summary>
/// 岩石坚硬程度细分，对应规范表 F.0.2 中的"坚硬岩/中硬岩/较软岩/软岩"。
/// </summary>
public enum RockHardnessLevel
{
    [Description("未设置")]
    NotSet = 0,

    [Description("坚硬岩")]
    StrongRock,

    [Description("中硬岩")]
    MediumHardRock,

    [Description("较软岩")]
    RelativelySoftRock,

    [Description("软岩")]
    SoftRock
}
