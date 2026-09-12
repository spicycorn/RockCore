using System.ComponentModel;

namespace RockCore.Core.Enums;

public enum RockType
{
    [Description("未设置")]
    NotSet = 0,

    [Description("硬质岩")]
    HardRock,

    [Description("软质岩")]
    SoftRock
}
