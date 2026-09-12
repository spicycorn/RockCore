using System.ComponentModel;

namespace RockCore.Core.Enums;

/// <summary>
/// 岩质均一性及软弱夹层情况，对应规范表 F.0.2 中的说明列条件。
/// </summary>
public enum RockHomogeneity
{
    [Description("未设置")]
    NotSet = 0,

    [Description("均一无软弱夹层")]
    HomogeneousNoWeakLayer,

    [Description("有软弱夹层")]
    HasWeakLayer
}
