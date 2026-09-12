using CommunityToolkit.Mvvm.ComponentModel;

namespace RockCore.Wpf.ViewModels;

/// <summary>
/// 结构面发育程度分段 ViewModel。
/// 与完整性等级分段独立计算，互不影响。
/// </summary>
public partial class DevelopmentSegmentViewModel : ObservableObject
{
    [ObservableProperty]
    private double _depthStart;

    [ObservableProperty]
    private double _depthEnd;

    [ObservableProperty]
    private string _development = string.Empty;

    [ObservableProperty]
    private double _lengthCm;

    [ObservableProperty]
    private string _basis = string.Empty;

    [ObservableProperty]
    private int _jointCount;

    [ObservableProperty]
    private double _avgSpacingCm;
}
