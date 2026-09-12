using System.ComponentModel;

namespace RockCore.Core.Enums;

public enum IntegrityLevel
{
    [Description("未评定")]
    Unknown = 0,

    [Description("完整")]
    Intact = 1,

    [Description("较完整")]
    RelativelyIntact = 2,

    [Description("完整性差")]
    Poor = 3,

    [Description("较破碎")]
    RelativelyBroken = 4,

    [Description("破碎")]
    Broken = 5,

    [Description("用户指定")]
    UserOverride = 99
}
