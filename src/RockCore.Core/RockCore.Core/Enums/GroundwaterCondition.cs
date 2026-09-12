using System.ComponentModel;

namespace RockCore.Core.Enums;

public enum GroundwaterCondition
{
    [Description("未设置")]
    NotSet = 0,

    [Description("干燥")]
    Dry,

    [Description("潮湿/渗水")]
    Damp,

    [Description("滴水/有地下水")]
    Wet
}
