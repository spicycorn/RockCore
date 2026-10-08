using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using RockCore.Core.Enums;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Core.Services;
using RockCore.Infrastructure.ImageAnalysis;
using RockCore.Infrastructure.Services;
using RockCore.Wpf.ViewModels;

namespace RockCore.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ThreeDimViewModel _threeDimViewModel;
    private readonly PhotoImportService _photoImportService;
    private readonly RuleEngineImageAnalyzer _ruleEngineAnalyzer;
    private readonly IStructuralPlaneRepository _structuralPlaneRepository;
    private readonly IAnalysisMetricsRepository _analysisMetricsRepository;
    private readonly ImageAnnotationService _annotationService;
    private readonly ExcelImportService _excelImportService;

    // 图像缩放和拖动相关字段
    private bool _isImageDragging = false;
    private Point _dragStartPoint = new Point(0, 0);
    private const double MinZoom = 0.2;
    private const double MaxZoom = 8.0;

    public MainWindow(
        MainViewModel viewModel,
        ThreeDimViewModel threeDimViewModel,
        PhotoImportService photoImportService,
        RuleEngineImageAnalyzer ruleEngineAnalyzer,
        IStructuralPlaneRepository structuralPlaneRepository,
        IAnalysisMetricsRepository analysisMetricsRepository,
        ImageAnnotationService annotationService,
        ExcelImportService excelImportService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _threeDimViewModel = threeDimViewModel;
        _photoImportService = photoImportService;
        _ruleEngineAnalyzer = ruleEngineAnalyzer;
        _structuralPlaneRepository = structuralPlaneRepository;
        _analysisMetricsRepository = analysisMetricsRepository;
        _annotationService = annotationService;
        _excelImportService = excelImportService;
        DataContext = _viewModel;

        // 设置 ThreeDimView 的 DataContext，确保使用同一个 ViewModel 实例
        if (ThreeDimViewControl != null)
            ThreeDimViewControl.DataContext = _threeDimViewModel;

        // 等级参数下拉：5 个完整性等级（按严重程度排序）
        LevelSettingsCombo.ItemsSource = new[]
        {
            IntegrityLevel.Intact, IntegrityLevel.RelativelyIntact, IntegrityLevel.Poor,
            IntegrityLevel.RelativelyBroken, IntegrityLevel.Broken
        };
        LevelSettingsCombo.SelectedIndex = 0;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        VersionTextBlock.Text = $"RockCore v{GetVersionText()}";
        await _viewModel.LoadProjectsAsync();
    }

    /// <summary>
    /// 从程序集读取版本号（csproj &lt;Version&gt; 统一维护），去掉末尾修订号 0（1.0.1.0 → 1.0.1）。
    /// </summary>
    private static string GetVersionText()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (v == null) return "1.0.1";
        return v.Revision == 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : v.ToString();
    }

    private async void ProjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ProjectViewModel projectVm)
        {
            _viewModel.SelectedProject = projectVm;
            _viewModel.SelectedBorehole = null;
        }
        else if (e.NewValue is BoreholeViewModel boreholeVm)
        {
            await SelectBoreholeAndLoadAsync(boreholeVm);
        }
    }

    /// <summary>
    /// 选中钻孔并加载其全部关联数据（照片/完整性分段/分类/已分析照片），
    /// 并刷新完整性卡片网格。所有"选中钻孔"的入口统一走这里，避免行为不一致。
    /// </summary>
    private async Task SelectBoreholeAndLoadAsync(BoreholeViewModel boreholeVm)
    {
        _viewModel.SelectedBorehole = boreholeVm;
        await _viewModel.LoadCorePhotosForSelectedBoreholeAsync();
        await _viewModel.LoadBoreholeIntegritySegmentsAsync();
        await _viewModel.LoadClassificationSegmentsAsync();
        _viewModel.LoadAnalyzedPhotos();
        // 刷新完整性卡片DataGrids
        RefreshIntegrityGrids();
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.CreateProjectCommand.ExecuteAsync(null);
    }

    private async void NewBorehole_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProject == null)
        {
            MessageBox.Show("请先选择项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await _viewModel.CreateBoreholeCommand.ExecuteAsync(null);
    }

    private async void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProject == null)
        {
            MessageBox.Show("请先选择项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"确定要删除项目 '{_viewModel.SelectedProject.Name}' 吗？",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteProjectCommand.ExecuteAsync(null);
            RefreshIntegrityGrids();
        }
    }

    private async void DeleteBorehole_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"确定要删除钻孔 '{_viewModel.SelectedBorehole.Number}' 吗？",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteBoreholeCommand.ExecuteAsync(null);
            RefreshIntegrityGrids();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveProjectCommand.ExecuteAsync(null);
        await _viewModel.SaveBoreholeCommand.ExecuteAsync(null);
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveProjectCommand.ExecuteAsync(null);
    }

    private async void SaveBorehole_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveBoreholeCommand.ExecuteAsync(null);
    }

    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProject == null)
        {
            MessageBox.Show("请先选择项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MainTabControl.SelectedIndex = 0;
    }

    private void EditBorehole_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MainTabControl.SelectedIndex = 1;
    }

    private async void SelectBorehole_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is BoreholeViewModel boreholeVm)
        {
            await SelectBoreholeAndLoadAsync(boreholeVm);
            MainTabControl.SelectedIndex = 1;
        }
    }

    private async void BoreholeItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is BoreholeViewModel boreholeVm)
        {
            await SelectBoreholeAndLoadAsync(boreholeVm);
        }
    }

    private void ImportPhotos_Click(object sender, RoutedEventArgs e)
    {
        // 与工具栏「导入照片」按钮保持一致：只需选中钻孔（选中钻孔必然已选中其所属项目）
        if (_viewModel.SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenPhotoImportDialog();
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("报告导出功能 - 阶段7后实现", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 保存单张照片的标注分析结果（照片字段 + 结构面 + 分析指标）。
    /// 供「单张分析 / 批量分析 / 重新标注」三处共用，避免重复代码。
    /// 返回 true 表示保存成功；失败时仅记录日志，由调用方决定如何向用户提示。
    /// </summary>
    private async Task<bool> SaveAnalysisResultAsync(CorePhotoViewModel photo, ImageAnalysisResult analysisResult)
    {
        try
        {
            int photoId = photo.Id;
            photo.IntegrityLevel = analysisResult.IntegrityLevel;
            photo.JointCount = analysisResult.JointCount;
            photo.AvgJointSpacingCm = analysisResult.AvgJointSpacingCm;
            photo.AnalysisStatus = "已分析";
            photo.IsUserModified = true;
            photo.AnalysisResultJson = System.Text.Json.JsonSerializer.Serialize(analysisResult);

            await _viewModel.CorePhotoRepository.UpdateAsync(photo.ToModel());

            // 删除旧的结构面数据后写入新数据
            await _structuralPlaneRepository.DeleteByCorePhotoIdAsync(photoId);
            if (analysisResult.StructuralPlanes.Count > 0)
            {
                foreach (var plane in analysisResult.StructuralPlanes)
                {
                    plane.CorePhotoId = photoId;
                    plane.CreatedAt = DateTime.Now;
                }
                await _structuralPlaneRepository.AddRangeAsync(analysisResult.StructuralPlanes);
            }

            // 删除旧的指标数据后写入新数据
            await _analysisMetricsRepository.DeleteByCorePhotoIdAsync(photoId);
            string integrityText = analysisResult.IntegrityLevel switch
            {
                Core.Enums.IntegrityLevel.Unknown => "未评定",
                Core.Enums.IntegrityLevel.Intact => "完整",
                Core.Enums.IntegrityLevel.RelativelyIntact => "较完整",
                Core.Enums.IntegrityLevel.Poor => "完整性差",
                Core.Enums.IntegrityLevel.RelativelyBroken => "较破碎",
                Core.Enums.IntegrityLevel.Broken => "破碎",
                _ => analysisResult.IntegrityLevel.ToString()
            };
            var metrics = new List<AnalysisMetrics>
            {
                new() { CorePhotoId = photoId, MetricKey = "岩体完整程度", MetricValue = integrityText, MetricUnit = string.Empty, CreatedAt = DateTime.Now },
                new() { CorePhotoId = photoId, MetricKey = "结构面发育程度", MetricValue = analysisResult.StructuralDevelopment, MetricUnit = string.Empty, CreatedAt = DateTime.Now }
            };
            await _analysisMetricsRepository.AddRangeAsync(metrics);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RockCore] 保存分析结果失败（{photo.FileName}）: {ex.Message}");
            return false;
        }
    }

    private async void AnalyzeSinglePhoto_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = CorePhotosDataGrid.SelectedItem as CorePhotoViewModel;
        if (selected == null)
        {
            MessageBox.Show("请先在列表中选择一张照片。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var photoPath = selected.RelativePath;
        if (!File.Exists(photoPath))
        {
            MessageBox.Show($"照片文件不存在: {photoPath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 打开人工标注窗口
        var annotationWindow = new ManualAnnotationWindow(photoPath, selected.DepthStart, selected.DepthEnd, selected);
        annotationWindow.Owner = this;

        bool? dr = null;
        try { dr = annotationWindow.ShowDialog(); }
        catch (Exception ex)
        {
            MessageBox.Show($"打开标注窗口出错: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (dr != true || annotationWindow.Result == null)
            return;  // 用户取消或未完成标注

        var analysisResult = annotationWindow.Result;

        // --- 保存到数据库（共用辅助方法）---
        var fileName = selected.FileName;
        if (!await SaveAnalysisResultAsync(selected, analysisResult))
        {
            MessageBox.Show("保存分析结果失败，详情请查看日志。", "保存错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // --- 更新 UI ---
        _viewModel.LoadAnalyzedPhotos();
        UpdateWorkflowSteps();
        _viewModel.StatusMessage = $"人工标注完成: {fileName} - {analysisResult.IntegrityLevel}";

        string integrityText = analysisResult.IntegrityLevel switch
        {
            Core.Enums.IntegrityLevel.Unknown => "未评定",
            Core.Enums.IntegrityLevel.Intact => "完整",
            Core.Enums.IntegrityLevel.RelativelyIntact => "较完整",
            Core.Enums.IntegrityLevel.Poor => "完整性差",
            Core.Enums.IntegrityLevel.RelativelyBroken => "较破碎",
            Core.Enums.IntegrityLevel.Broken => "破碎",
            _ => analysisResult.IntegrityLevel.ToString()
        };

        var segmentsText = "";
        if (analysisResult.IntegritySegments != null && analysisResult.IntegritySegments.Count > 0)
        {
            segmentsText = "\n\n完整性分段:\n";
            foreach (var seg in analysisResult.IntegritySegments)
                segmentsText += $"  {seg}\n";
        }

        MessageBox.Show(
            $"人工标注完成！\n\n" +
            $"分析器: 人工标注\n" +
            $"照片: {fileName}\n" +
            $"岩心段: {analysisResult.CorePieces.Count} 段\n" +
            $"节理数: {analysisResult.JointCount} 条\n" +
            $"平均间距: {(analysisResult.AvgJointSpacingCm > 0 ? $"{analysisResult.AvgJointSpacingCm:F1} cm" : "无比例尺")}\n" +
            $"岩体完整程度: {integrityText}\n" +
            $"结构面发育程度: {analysisResult.StructuralDevelopment}\n" +
            $"像素/厘米: {(analysisResult.PixelPerCm > 0 ? $"{analysisResult.PixelPerCm:F2} px/cm" : "未设置")}" +
            segmentsText,
            "人工标注完成",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void AnalyzeBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_viewModel.SelectedBorehole.CorePhotos.Count == 0)
        {
            MessageBox.Show("当前钻孔没有照片，无法分析", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 人工标注模式下，没有"批量自动识别"无法保证准确性。
        // 此处改为提示，并提供逐张打开标注——点击确定后，依次循环打开 ManualAnnotationWindow
        var dr = MessageBox.Show(
            "当前使用人工标注模式，不支持一键批量自动识别。\n\n" +
            "是否按顺序逐张打开标注窗口？\n" +
            "（在每个照片中将比例尺、岩心段、节理线手动框选，最后点击完成才会保存结果）",
            "批量标注提示", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (dr != MessageBoxResult.OK) return;

        int successCount = 0;
        int skipped = 0;
        string? firstFailedName = null;

        // 依次循环打开
        foreach (var photo in _viewModel.SelectedBorehole.CorePhotos)
        {
            if (string.IsNullOrEmpty(photo.RelativePath) || !File.Exists(photo.RelativePath))
            {
                skipped++;
                continue;
            }

            var win = new ManualAnnotationWindow(
                photo.RelativePath, photo.DepthStart, photo.DepthEnd, photo);
            win.Owner = this;
            win.Title = $"标注 ({successCount + skipped + 1}/{_viewModel.SelectedBorehole.CorePhotos.Count}) — " +
                        Path.GetFileName(photo.RelativePath);

            bool? res;
            try { res = win.ShowDialog(); }
            catch (Exception ex)
            {
                if (firstFailedName == null) firstFailedName = photo.FileName + "（异常：" + ex.Message + "）";
                continue;
            }

            if (res != true || win.Result == null)
            {
                skipped++;
                continue;
            }

            var analysisResult = win.Result;

            // 保存到数据库（共用辅助方法）
            if (await SaveAnalysisResultAsync(photo, analysisResult))
            {
                successCount++;
            }
            else if (firstFailedName == null)
            {
                firstFailedName = photo.FileName + "（保存失败，详情见日志）";
            }
        }

        _viewModel.LoadAnalyzedPhotos();
        UpdateWorkflowSteps();
        MessageBox.Show(
            $"批量标注完成。\n\n成功: {successCount} 张\n跳过/取消: {skipped} 张" +
            (firstFailedName != null ? $"\n\n首个问题: {firstFailedName}" : ""),
            "批量标注结果", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void Reanalyze_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = CorePhotosDataGrid.SelectedItem as CorePhotoViewModel;
        if (selected == null)
        {
            MessageBox.Show("请先在列表中选择一张照片，以便重新标注。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrEmpty(selected.RelativePath) || !File.Exists(selected.RelativePath))
        {
            MessageBox.Show("照片文件不存在，无法重新标注。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var win = new ManualAnnotationWindow(
            selected.RelativePath, selected.DepthStart, selected.DepthEnd, selected);
        win.Owner = this;
        win.Title = "重新标注 — " + Path.GetFileName(selected.RelativePath);

        if (win.ShowDialog() != true || win.Result == null)
            return;

        var analysisResult = win.Result;

        if (!await SaveAnalysisResultAsync(selected, analysisResult))
        {
            MessageBox.Show("保存重新标注结果时出错，详情请查看日志。",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        _viewModel.LoadAnalyzedPhotos();
        UpdateWorkflowSteps();
    }

    private void ViewCoreEditor_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedIndex = 1;
    }

    private void ViewAnalysisResult_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedIndex = 2;
    }

    private void View3D_Click(object sender, RoutedEventArgs e)
    {
        MainTabControl.SelectedIndex = 4;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadProjectsAsync();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        // 版本号从程序集读取（在 csproj 的 <Version> 中统一维护），避免硬编码导致与实际版本不一致
        MessageBox.Show(
            $"RockCore 岩心围岩评价软件 v{GetVersionText()}\n\n" +
            "适用于 DL/T 5894-2025《压气储能电站工程地质勘察规范》\n\n" +
            "压缩空气储能电站岩心围岩类别评价",
            "关于", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void SpecificationSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SpecificationSettingsWindow(
            App.Services.GetRequiredService<ISpecificationService>());
        if (dlg.ShowDialog() == true)
        {
            // 配置已保存，刷新相关 UI（如岩体结构下拉选项）
            // 由于使用了动态绑定，大部分会自动刷新
            // 这里强制刷新 DataGrid 以确保词汇更新生效
            RefreshIntegrityGrids();
            _viewModel.StatusMessage = "规范设置已更新";
        }
    }

    // ---- 物理段工具栏处理 ----

    private void ImportPhotosToolbarButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenPhotoImportDialog();
    }

    private async void RefreshPhotosButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadCorePhotosForSelectedBoreholeAsync();
    }

    private async void EditPhotoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null) return;

        var selected = CorePhotosDataGrid.SelectedItem as CorePhotoViewModel;
        if (selected == null)
        {
            MessageBox.Show("请先在列表中选择一条物理段。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new CorePhotoEditWindow(selected) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.WasModified)
        {
            await _viewModel.CorePhotoRepository.UpdateAsync(selected.ToModel());
            await _viewModel.LoadCorePhotosForSelectedBoreholeAsync();
        }
    }

    private async void DeletePhotoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null) return;

        var selected = CorePhotosDataGrid.SelectedItem as CorePhotoViewModel;
        if (selected == null)
        {
            MessageBox.Show("请先在列表中选择一条物理段。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"确定要删除物理段「{selected.FileName}」吗？",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        await _viewModel.CorePhotoRepository.DeleteAsync(selected.Id);
        await _viewModel.LoadCorePhotosForSelectedBoreholeAsync();
    }

    private async void DeleteAllPhotosButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null) return;

        if (_viewModel.SelectedBorehole.CorePhotos.Count == 0)
        {
            MessageBox.Show("当前钻孔没有物理段。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"确定要清空钻孔「{_viewModel.SelectedBorehole.Number}」的全部 {_viewModel.SelectedBorehole.CorePhotos.Count} 条物理段吗？",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        await _viewModel.CorePhotoRepository.DeleteByBoreholeIdAsync(_viewModel.SelectedBorehole.Id);
        await _viewModel.LoadCorePhotosForSelectedBoreholeAsync();
    }

    private void CorePhotosDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        EditPhotoButton_Click(sender, e);
    }

    private async void OpenPhotoImportDialog()
    {
        if (_viewModel.SelectedBorehole == null) return;

        var boreholeModel = _viewModel.SelectedBorehole.ToModel();
        var dlg = new PhotoImportDialogWindow(
            boreholeModel,
            _photoImportService,
            _viewModel.CorePhotoRepository)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true)
        {
            // 根据物理段更新钻孔总孔深
            var maxDepth = await _viewModel.CorePhotoRepository.GetMaxDepthEndAsync(_viewModel.SelectedBorehole.Id);
            if (maxDepth.HasValue && maxDepth.Value > 0)
            {
                _viewModel.SelectedBorehole.TotalDepth = maxDepth.Value;
                await App.Services.GetRequiredService<IBoreholeRepository>()
                    .UpdateAsync(_viewModel.SelectedBorehole.ToModel());
                // 孔深已变化，通知三维视图需要重新生成场景
                _viewModel.NotifyBoreholeSaved();
            }

            await _viewModel.LoadCorePhotosForSelectedBoreholeAsync();
            UpdateWorkflowSteps();
        }
    }

    private async void AnalyzedPhotosListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var listView = sender as ListView;
        if (listView == null) return;

        var selected = listView.SelectedItem as CorePhotoViewModel;
        await _viewModel.LoadAnalysisDetailAsync(selected);

        // 清空旧的完整性分段、发育程度分段与标注图像
        _viewModel.IntegritySegments.Clear();
        _viewModel.DevelopmentSegments.Clear();
        _viewModel.AnnotatedImage = null;

        // 生成标注图像 + 加载完整性分段数据（来源于人工标注计算结果，不伪造）
        if (selected != null && File.Exists(selected.RelativePath))
        {
            try
            {
                var photo = await _viewModel.CorePhotoRepository.GetByIdAsync(selected.Id);
                if (photo?.AnalysisResultJson != null)
                {
                    var analysisResult = System.Text.Json.JsonSerializer.Deserialize<ImageAnalysisResult>(photo.AnalysisResultJson);
                    if (analysisResult != null)
                    {
                        // 生成标注图像
                        string annotatedPath = _annotationService.DrawAnnotations(
                            selected.RelativePath,
                            analysisResult,
                            selected.DepthStart,
                            selected.DepthEnd);

                        _viewModel.AnnotatedImagePath = annotatedPath;
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(annotatedPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        _viewModel.AnnotatedImage = bitmap;

                        // 重置缩放与平移
                        if (ImageScale != null)
                        {
                            ImageScale.ScaleX = 1.0;
                            ImageScale.ScaleY = 1.0;
                            if (ZoomControls != null)
                                ZoomControls.Content = "🔍 100%";
                        }
                        if (ImageTranslate != null)
                        {
                            ImageTranslate.X = 0;
                            ImageTranslate.Y = 0;
                        }

                        // 加载完整性分段（完全来源于人工标注计算结果，不伪造默认值）
                        if (analysisResult.IntegritySegments != null)
                        {
                            foreach (var seg in analysisResult.IntegritySegments)
                            {
                                _viewModel.IntegritySegments.Add(new IntegritySegmentViewModel
                                {
                                    DepthStart = seg.DepthStart,
                                    DepthEnd = seg.DepthEnd,
                                    Level = seg.Level,
                                    LengthCm = seg.LengthCm,
                                    Basis = seg.Basis,
                                    Development = seg.Development
                                });
                            }
                        }

                        // 加载结构面发育程度分段（与完整性等级独立计算）
                        if (analysisResult.DevelopmentSegments != null)
                        {
                            foreach (var seg in analysisResult.DevelopmentSegments)
                            {
                                _viewModel.DevelopmentSegments.Add(new DevelopmentSegmentViewModel
                                {
                                    DepthStart = seg.DepthStart,
                                    DepthEnd = seg.DepthEnd,
                                    Development = seg.Development,
                                    LengthCm = seg.LengthCm,
                                    Basis = seg.Basis,
                                    JointCount = seg.JointCount,
                                    AvgSpacingCm = seg.AvgSpacingCm
                                });
                            }
                        }

                        _viewModel.StatusMessage = $"已加载: {Path.GetFileName(annotatedPath)}（完整性 {_viewModel.IntegritySegments.Count} 段，发育程度 {_viewModel.DevelopmentSegments.Count} 段）";
                    }
                }
            }
            catch (Exception ex)
            {
                _viewModel.StatusMessage = $"加载分析结果失败: {ex.Message}";
            }
        }
    }

    private void ImageContainer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel?.AnnotatedImage == null || ImageScale == null) return;

        double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
        double newScale = ImageScale.ScaleX * zoomFactor;

        if (newScale >= MinZoom && newScale <= MaxZoom)
        {
            ImageScale.ScaleX = newScale;
            ImageScale.ScaleY = newScale;

            if (ZoomControls != null)
                ZoomControls.Content = $"🔍 {(newScale * 100):F0}%";

            e.Handled = true;
        }
    }

    private void ImageContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel?.AnnotatedImage == null) return;
        if (ImageTranslate == null || ImageScale == null) return;

        _isImageDragging = true;
        _dragStartPoint = new Point(e.GetPosition(ImageContainer).X - ImageTranslate.X,
                                     e.GetPosition(ImageContainer).Y - ImageTranslate.Y);
        ImageContainer.CaptureMouse();
    }

    private void ImageContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isImageDragging)
        {
            _isImageDragging = false;
            ImageContainer.ReleaseMouseCapture();
        }
    }

    private void ImageContainer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isImageDragging || ImageTranslate == null) return;

        Point currentPos = e.GetPosition(ImageContainer);
        ImageTranslate.X = currentPos.X - _dragStartPoint.X;
        ImageTranslate.Y = currentPos.Y - _dragStartPoint.Y;
    }

    private void ImageContainer_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isImageDragging)
        {
            _isImageDragging = false;
            ImageContainer.ReleaseMouseCapture();
        }
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e)
    {
        if (ImageScale != null)
        {
            ImageScale.ScaleX = 1.0;
            ImageScale.ScaleY = 1.0;
            if (ZoomControls != null)
                ZoomControls.Content = "🔍 100%";
        }
        if (ImageTranslate != null)
        {
            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
        }
    }

    // ===== 完整性等级分组 事件处理 =====
    private async void RefreshIntegritySegments_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await _viewModel.AggregateIntegritySegmentsAsync();
        // 聚合完成后检查跨地下水埋深段
        await _viewModel.HandleCrossGroundwaterSegmentsAsync();
        // 刷新5个完整性卡片DataGrids
        RefreshIntegrityGrids();
    }

    private async void IntegritySegmentRockType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not RockType newRockType) return;

        if (sender is ComboBox combo && combo.DataContext is BoreholeIntegritySegment seg)
        {
            // DataGrid 编辑模式下，SelectionChanged 触发时数据绑定可能尚未写入源，
            // 显式赋值确保源对象属性为最新值，再保存。
            seg.RockType = newRockType;
            _viewModel.CheckAndPromptSpecialConditions(seg);
            await _viewModel.UpdateSegmentCommand.ExecuteAsync(seg);
        }
    }

    // 完整性分段岩体结构改变时，触发特殊条件检查并保存
    private async void IntegritySegmentRockStructure_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not RockStructureType newStructure) return;

        if (sender is ComboBox combo && combo.DataContext is BoreholeIntegritySegment seg)
        {
            seg.RockStructureType = newStructure;
            _viewModel.CheckAndPromptSpecialConditions(seg);
            await _viewModel.UpdateSegmentCommand.ExecuteAsync(seg);
        }
    }

    // 坚硬程度、岩质均一性等字段变化后保存，并在满足条件时触发特殊条件弹窗
    private async void IntegritySegmentProperty_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;

        if (sender is ComboBox combo && combo.DataContext is BoreholeIntegritySegment seg)
        {
            var newValue = e.AddedItems[0];
            // 通过枚举类型区分是哪个属性
            if (newValue is RockHardnessLevel h)
            {
                seg.RockHardnessLevel = h;
            }
            else if (newValue is RockHomogeneity hm)
            {
                seg.RockHomogeneity = hm;
            }
            _viewModel.CheckAndPromptSpecialConditions(seg);
            await _viewModel.UpdateSegmentCommand.ExecuteAsync(seg);
        }
    }

    private async void IntegritySegmentGroundwater_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not GroundwaterCondition newGw) return;

        if (sender is ComboBox combo && combo.DataContext is BoreholeIntegritySegment seg)
        {
            seg.GroundwaterCondition = newGw;
            await _viewModel.UpdateSegmentCommand.ExecuteAsync(seg);
        }
    }

    private async void IntegritySegmentCaveAxis_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;

        if (sender is ComboBox combo && combo.DataContext is BoreholeIntegritySegment seg)
        {
            // 洞轴夹角列使用 SelectedValueBinding，ItemsSource 是 CaveAxisOptionViewModel 列表
            if (e.AddedItems[0] is CaveAxisOptionViewModel opt)
            {
                seg.CaveAxisAngleLessThan30 = opt.Value;
            }
            await _viewModel.UpdateSegmentCommand.ExecuteAsync(seg);
        }
    }

    // 等级参数：对下拉选中的完整性等级按深度区间批量设置参数
    private async void LevelSettings_Click(object sender, RoutedEventArgs e)
    {
        if (LevelSettingsCombo.SelectedItem is not IntegrityLevel targetLevel)
        {
            MessageBox.Show("请先选择完整性等级", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SegmentSettingsDialog(targetLevel);
        if (dlg.ShowDialog() == true)
        {
            int count = await _viewModel.ApplySettingsToDepthRangeAsync(
                targetLevel, dlg.DepthStart, dlg.DepthEnd,
                dlg.SelectedRockType, dlg.SelectedRockStructureType,
                dlg.SelectedHardnessLevel, dlg.SelectedHomogeneity,
                dlg.SelectedGroundwaterCondition, dlg.CaveAxisAngleLessThan30);

            RefreshIntegrityGrids();

            if (count == 0)
                MessageBox.Show("在指定深度区间内未找到匹配的分段", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                _viewModel.StatusMessage = $"已更新 {count} 个分段的参数";
        }
    }

    // ===== 完整性分段：统一表 + 等级筛选 =====

    // 当前筛选等级（null = 全部）
    private IntegrityLevel? _segmentFilterLevel;

    /// <summary>
    /// 刷新统一完整性分段表：绑定数据源、应用等级筛选、更新筛选 chip 汇总。
    /// </summary>
    private void RefreshIntegrityGrids()
    {
        SegmentsDataGrid.ItemsSource = _viewModel.BoreholeIntegritySegments;
        ApplySegmentFilter();
        UpdateTotalLengths();
        UpdateWorkflowSteps();
    }

    // ===== 工作流步骤条 =====

    private void WorkflowStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            // 标签页索引：0 项目概览 / 1 岩心编辑器 / 2 分析结果 / 3 围岩分类 / 4 三维可视化 / 5 报告
            MainTabControl.SelectedIndex = tag switch
            {
                "classify" => 3,
                "3d" => 4,
                _ => 1
            };
        }
    }

    /// <summary>
    /// 根据当前钻孔数据状态刷新 4 个步骤的完成态显示。
    /// ①导入数据（已有导入分段）→ ②分段填写（F.0.2 全填）→ ③围岩分类 → ④三维查看
    /// </summary>
    private void UpdateWorkflowSteps()
    {
        var hasSegments = _viewModel.BoreholeIntegritySegments.Count > 0;
        bool segmentsFilled = hasSegments && _viewModel.BoreholeIntegritySegments.All(s =>
            s.RockType != RockType.NotSet && s.RockStructureType != RockStructureType.NotSet);
        bool classified = !string.IsNullOrEmpty(_viewModel.CurrentBoreholeClassSummary);

        SetStepVisual(Step1Dot, Step1Num, Step1Label, hasSegments, 1);
        SetStepVisual(Step2Dot, Step2Num, Step2Label, segmentsFilled, 2);
        SetStepVisual(Step3Dot, Step3Num, Step3Label, classified, 3);
        SetStepVisual(Step4Dot, Step4Num, Step4Label, false, 4); // 三维为查看动作，无完成态
    }

    private void SetStepVisual(Border dot, TextBlock num, TextBlock label, bool done, int index)
    {
        var primary = (Brush)FindResource("PrimaryBrush");
        var gray = (Brush)FindResource("BorderLightBrush");
        var textPrimary = (Brush)FindResource("TextPrimaryBrush");
        var textSecondary = (Brush)FindResource("TextSecondaryBrush");

        dot.Background = done ? primary : gray;
        num.Text = done ? "✓" : index.ToString();
        num.Foreground = done ? Brushes.White : textSecondary;
        label.Foreground = done ? textPrimary : textSecondary;
        label.FontWeight = done ? FontWeights.SemiBold : FontWeights.Normal;
    }

    // ===== 数据导入（Excel 模板 v4）=====

    private async void ImportData_Click(object sender, RoutedEventArgs e)
    {
        var project = _viewModel.SelectedProject;
        if (project == null)
        {
            MessageBox.Show(this, "请先在左侧选择项目", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new ExcelImportDialogWindow(_excelImportService, project.Boreholes, _viewModel.SelectedBorehole)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.StatusMessage = $"已导入岩心数据表（钻孔 {dialog.ImportedBoreholeNumber}）";
            if (dialog.ImportedBoreholeId != null &&
                _viewModel.SelectedBorehole != null &&
                dialog.ImportedBoreholeId == _viewModel.SelectedBorehole.Id)
            {
                await _viewModel.LoadBoreholeIntegritySegmentsAsync();
                RefreshIntegrityGrids();
            }
        }
    }

    private async void DataSourceManager_Click(object sender, RoutedEventArgs e)
    {
        var borehole = _viewModel.SelectedBorehole;
        if (borehole == null)
        {
            MessageBox.Show(this, "请先在左侧选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new DataSourceManagerWindow(_excelImportService, borehole) { Owner = this };
        dialog.ShowDialog();
        if (dialog.DeletedAnything)
        {
            await _viewModel.LoadBoreholeIntegritySegmentsAsync();
            RefreshIntegrityGrids();
        }
    }

    private void ApplySegmentFilter()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_viewModel.BoreholeIntegritySegments);
        if (view != null)
        {
            view.Filter = o => _segmentFilterLevel == null ||
                               (o is BoreholeIntegritySegment s && s.IntegrityLevel == _segmentFilterLevel);
        }
    }

    private void FilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { Tag: string tag })
        {
            _segmentFilterLevel = tag == "All" ? null : Enum.Parse<IntegrityLevel>(tag);
            ApplySegmentFilter();
        }
    }

    /// <summary>
    /// 更新筛选 chip 上的分段数量与累计长度汇总。
    /// </summary>
    private void UpdateTotalLengths()
    {
        ChipAll.Content = $"全部 {_viewModel.BoreholeIntegritySegments.Count}段";
        ChipIntact.Content = FormatLevelChip(IntegrityLevel.Intact, "完整");
        ChipRelativelyIntact.Content = FormatLevelChip(IntegrityLevel.RelativelyIntact, "较完整");
        ChipPoor.Content = FormatLevelChip(IntegrityLevel.Poor, "完整性差");
        ChipRelativelyBroken.Content = FormatLevelChip(IntegrityLevel.RelativelyBroken, "较破碎");
        ChipBroken.Content = FormatLevelChip(IntegrityLevel.Broken, "破碎");
    }

    private string FormatLevelChip(IntegrityLevel level, string label)
    {
        var segs = _viewModel.BoreholeIntegritySegments.Where(s => s.IntegrityLevel == level).ToList();
        if (segs.Count == 0) return label;
        return $"{label} {segs.Count}段/{segs.Sum(s => s.DepthEnd - s.DepthStart):F1}m";
    }

    // 刷新分析结果（从图像分析结果Tab的工具栏按钮触发）
    private async void RefreshAnalysisResult_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAnalysisResultCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// 在视觉树中递归查找指定类型的子元素。
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    /// <summary>
    /// 获取 DataGrid 指定行和列的单元格。
    /// </summary>
    private static DataGridCell? GetCell(DataGrid grid, DataGridRow row, int columnIndex)
    {
        var presenter = FindVisualChild<DataGridCellsPresenter>(row);
        if (presenter == null)
            return null;
        return presenter.ItemContainerGenerator.ContainerFromIndex(columnIndex) as DataGridCell;
    }

    /// <summary>
    /// 强制将界面上所有 DataGrid 中当前显示的 ComboBox 值同步到对应的 ViewModel 对象。
    /// 绕过 DataGrid 编辑模式可能导致的绑定延迟问题，确保内存中的值与 UI 一致。
    /// </summary>
    private void SyncUItoViewModel()
    {
        var grids = new[] { SegmentsDataGrid };

        foreach (var grid in grids)
        {
            foreach (var item in grid.Items)
            {
                if (item is not BoreholeIntegritySegment seg)
                    continue;

                var row = grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                if (row == null)
                    continue;

                for (int colIndex = 0; colIndex < grid.Columns.Count; colIndex++)
                {
                    var column = grid.Columns[colIndex];
                    if (column is not DataGridComboBoxColumn comboColumn)
                        continue;

                    var cell = GetCell(grid, row, colIndex);
                    if (cell == null)
                        continue;

                    var combo = FindVisualChild<ComboBox>(cell);
                    if (combo?.SelectedItem == null)
                        continue;

                    // 根据列的绑定路径判断是哪个属性
                    var bindingPath = (comboColumn.SelectedItemBinding as System.Windows.Data.Binding)?.Path?.Path;
                    switch (bindingPath)
                    {
                        case "RockType":
                            if (combo.SelectedItem is RockType rt)
                                seg.RockType = rt;
                            break;
                        case "RockStructureType":
                            if (combo.SelectedItem is RockStructureType st)
                                seg.RockStructureType = st;
                            break;
                        case "RockHardnessLevel":
                            if (combo.SelectedItem is RockHardnessLevel rh)
                                seg.RockHardnessLevel = rh;
                            break;
                        case "RockHomogeneity":
                            if (combo.SelectedItem is RockHomogeneity hm)
                                seg.RockHomogeneity = hm;
                            break;
                        case "GroundwaterCondition":
                            if (combo.SelectedItem is GroundwaterCondition gw)
                                seg.GroundwaterCondition = gw;
                            break;
                        case "CaveAxisAngleLessThan30":
                            if (combo.SelectedItem is CaveAxisOptionViewModel opt)
                                seg.CaveAxisAngleLessThan30 = opt.Value;
                            break;
                    }
                }
            }
        }
    }

    // 保存当前界面上所有完整性分段的设置到数据库
    private async void SaveIntegritySegments_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 结束所有 DataGrid 的编辑
        var grids = new[] { SegmentsDataGrid };

        foreach (var grid in grids)
        {
            try
            {
                if (grid.SelectedItem != null)
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch
            {
                // 忽略
            }
        }

        // 强制同步：直接从 UI 读取 ComboBox 当前值写回 ViewModel，
        // 避免 DataGrid 编辑模式绑定延迟导致的数据不一致。
        SyncUItoViewModel();

        await _viewModel.SaveAllIntegritySegmentsCommand.ExecuteAsync(null);
        UpdateWorkflowSteps();
    }

    // ===== 阶段五：围岩分类事件处理 =====
    private async void ClassifyBorehole_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ClassifySelectedBoreholeCommand.ExecuteAsync(null);
        UpdateWorkflowSteps();
    }

    private async void RefreshProjectStatistics_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshProjectStatisticsCommand.ExecuteAsync(null);
    }
}

