using CommunityToolkit.Mvvm.ComponentModel;
using RockCore.Core.Enums;

namespace RockCore.Wpf.ViewModels;

/// <summary>
/// 完整性分段 ViewModel。完全来源于人工标注计算结果，不伪造数据。
/// </summary>
public partial class IntegritySegmentViewModel : ObservableObject
{
    [ObservableProperty]
    private double _depthStart;

    [ObservableProperty]
    private double _depthEnd;

    [ObservableProperty]
    private IntegrityLevel _level;

    [ObservableProperty]
    private double _lengthCm;

    [ObservableProperty]
    private string _basis = string.Empty;

    [ObservableProperty]
    private string _development = string.Empty;

    public string LevelText => Level switch
    {
        IntegrityLevel.Unknown => "未评定",
        IntegrityLevel.Intact => "完整",
        IntegrityLevel.RelativelyIntact => "较完整",
        IntegrityLevel.Poor => "完整性差",
        IntegrityLevel.RelativelyBroken => "较破碎",
        IntegrityLevel.Broken => "破碎",
        _ => Level.ToString()
    };
}
