using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RockCore.Core.Enums;
using RockCore.Core.Models;
using RockCore.Wpf.ThreeDim;

namespace RockCore.Wpf.ViewModels;

public partial class ThreeDimViewModel : ObservableObject
{
    private readonly IThreeDimSceneService _sceneService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private ModelVisual3D? _sceneContent;

    [ObservableProperty]
    private ColorScheme _colorScheme = ColorScheme.ByRockClass;

    [ObservableProperty]
    private bool _showStructuralPlanes = true;

    [ObservableProperty]
    private bool _showGroundwater = true;

    [ObservableProperty]
    private bool _showDepthLabels = true;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private double _progressOffset = 0.0;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _legendTitle = "围岩类别图例";

    [ObservableProperty]
    private ObservableCollection<LegendItem> _legendItems = new();

    [ObservableProperty]
    private double _sceneDepth;

    [ObservableProperty]
    private double _depthTickInterval = 0;

    [ObservableProperty]
    private double _gridSpacing = 0;

    [ObservableProperty]
    private Borehole? _selectedBorehole;

    [ObservableProperty]
    private bool _needsRegeneration = false;

    public IReadOnlyList<ColorSchemeInfo> ColorSchemeOptions { get; } =
    [
        new ColorSchemeInfo(ColorScheme.ByRockClass, "按围岩类别"),
        new ColorSchemeInfo(ColorScheme.ByIntegrityLevel, "按完整性等级"),
        new ColorSchemeInfo(ColorScheme.ByRockType, "按岩质类型")
    ];

    public ThreeDimViewModel(IThreeDimSceneService sceneService, MainViewModel mainViewModel)
    {
        _sceneService = sceneService;
        _mainViewModel = mainViewModel;

        if (_mainViewModel.SelectedBorehole?.ToModel() is Borehole bh)
            SelectedBorehole = bh;

        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        _mainViewModel.BoreholeSaved += OnBoreholeSaved;
        UpdateLegendItems();
    }

    private void OnMainViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedBorehole))
        {
            var vm = _mainViewModel.SelectedBorehole?.ToModel();
            if (vm != null)
            {
                bool idChanged = vm.Id != SelectedBorehole?.Id;
                SelectedBorehole = vm;

                if (idChanged)
                {
                    NeedsRegeneration = false;
                }
            }
        }
    }

    private void OnBoreholeSaved(object? sender, EventArgs e)
    {
        var vm = _mainViewModel.SelectedBorehole?.ToModel();
        if (vm != null && vm.Id == SelectedBorehole?.Id)
        {
            SelectedBorehole = vm;
            NeedsRegeneration = true;
            StatusMessage = "钻孔信息已更新，请重新生成三维场景";
        }
    }

    partial void OnColorSchemeChanged(ColorScheme value)
    {
        UpdateLegendItems();
        TriggerSceneReload();
    }

    partial void OnShowStructuralPlanesChanged(bool value) => TriggerSceneReload();
    partial void OnShowGroundwaterChanged(bool value) => TriggerSceneReload();
    partial void OnShowDepthLabelsChanged(bool value) => TriggerSceneReload();
    partial void OnDepthTickIntervalChanged(double value) => TriggerSceneReload();
    partial void OnGridSpacingChanged(double value) => TriggerSceneReload();

    partial void OnSelectedBoreholeChanged(Borehole? value)
    {
        if (value != null)
            _ = ReloadSceneAsync();
    }

    private void TriggerSceneReload()
    {
        if (SelectedBorehole != null)
        {
            NeedsRegeneration = false;
            _ = ReloadSceneAsync();
        }
    }

    private void UpdateLegendItems()
    {
        LegendItems.Clear();
        switch (ColorScheme)
        {
            case ColorScheme.ByRockClass:
                LegendTitle = "围岩类别图例";
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByRockClass(RockClass.I)), Label = "I 类 (优质)" });
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByRockClass(RockClass.II)), Label = "II 类 (良好)" });
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByRockClass(RockClass.III)), Label = "III 类 (一般)" });
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByRockClass(RockClass.IV)), Label = "IV 类 (较差)" });
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByRockClass(RockClass.V)), Label = "V 类 (差)" });
                break;
            case ColorScheme.ByIntegrityLevel:
                LegendTitle = "完整性等级图例";
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByIntegrityLevel(IntegrityLevel.Intact)), Label = "完整" });
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByIntegrityLevel(IntegrityLevel.RelativelyIntact)), Label = "较完整" });
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByIntegrityLevel(IntegrityLevel.Poor)), Label = "完整性差" });
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByIntegrityLevel(IntegrityLevel.RelativelyBroken)), Label = "较破碎" });
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByIntegrityLevel(IntegrityLevel.Broken)), Label = "破碎" });
                break;
            case ColorScheme.ByRockType:
                LegendTitle = "岩质类型图例";
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByRockType(RockType.HardRock)), Label = "硬质岩" });
                LegendItems.Add(new LegendItem { Color = new SolidColorBrush(ColorMapper.GetColorByRockType(RockType.SoftRock)), Label = "软质岩" });
                break;
        }
    }

    [RelayCommand]
    private async Task ReloadSceneAsync()
    {
        var borehole = SelectedBorehole;

        if (borehole == null)
        {
            SceneContent = null;
            StatusMessage = "未选择钻孔，请在左侧项目树中点击选择一个钻孔";
            ProgressOffset = 0.0;
            return;
        }

        try
        {
            IsLoading = true;
            ProgressOffset = 0.1;
            StatusMessage = $"正在生成 {borehole.Number} 的三维场景...";

            await Task.Delay(50);

            ProgressOffset = 0.3;
            var segments = await _sceneService.GetClassificationSegmentsAsync(borehole.Id);

            if (segments == null || !segments.Any())
            {
                SceneContent = null;
                ProgressOffset = 0.0;
                StatusMessage = $"警告：钻孔 {borehole.Number} 没有围岩分类数据，请先运行三段融合算法+围岩分类分析";
                IsLoading = false;
                return;
            }

            var segmentList = segments.ToList();
            var depthRange = $"{segmentList.Min(s => s.DepthStart):F1}m ~ {segmentList.Max(s => s.DepthEnd):F1}m";
            StatusMessage = $"找到 {segmentList.Count} 个分类段 ({depthRange})";

            await Task.Delay(50);

            ProgressOffset = 0.6;
            var planes = await _sceneService.GetStructuralPlanesAsync(borehole.Id);

            ProgressOffset = 0.8;
            StatusMessage = "正在构建三维模型...";

            double? tickInterval = DepthTickInterval > 0 ? DepthTickInterval : null;
            var modelVisual3D = await _sceneService.BuildBoreholeSceneAsync(
                borehole,
                ColorScheme,
                tickInterval,
                ShowStructuralPlanes,
                ShowGroundwater,
                ShowDepthLabels,
                GridSpacing);

            if (modelVisual3D == null)
            {
                SceneContent = null;
                ProgressOffset = 0.0;
                StatusMessage = "错误：场景构建返回空对象";
                IsLoading = false;
                return;
            }

            SceneContent = modelVisual3D;

            var actualDepth = segmentList.Max(s => s.DepthEnd);
            SceneDepth = actualDepth;

            ProgressOffset = 1.0;
            StatusMessage = $"已完成 {borehole.Number} 三维场景 (深度{actualDepth:F1}m, {segmentList.Count}个分类段)";
            NeedsRegeneration = false;
        }
        catch (Exception ex)
        {
            SceneContent = null;
            ProgressOffset = 0.0;
            StatusMessage = $"加载失败: {ex.GetType().Name}: {ex.Message}";
            System.Diagnostics.Debug.WriteLine("=== ThreeDimViewModel 异常 ===");
            System.Diagnostics.Debug.WriteLine(ex.ToString());
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public sealed class LegendItem
{
    public required Brush Color { get; init; }
    public required string Label { get; init; }
}

public sealed class ColorSchemeInfo
{
    public ColorScheme Value { get; }
    public string DisplayName { get; }

    public ColorSchemeInfo(ColorScheme value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}