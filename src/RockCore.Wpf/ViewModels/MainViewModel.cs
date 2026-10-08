using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RockCore.Core.Enums;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Core.Services;

namespace RockCore.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IProjectRepository _projectRepository;
    private readonly IBoreholeRepository _boreholeRepository;
    private readonly ICorePhotoRepository _corePhotoRepository;
    private readonly IBoreholeIntegritySegmentRepository _boreholeIntegritySegmentRepository;
    private readonly IStructuralPlaneRepository _structuralPlaneRepository;
    private readonly IAnalysisMetricsRepository _analysisMetricsRepository;
    private readonly IClassificationSegmentRepository _classificationSegmentRepository;
    private readonly IClassificationService _classificationService;
    private readonly ProjectStatisticsService _projectStatisticsService;

    [ObservableProperty]
    private ObservableCollection<ProjectViewModel> _projects = new();

    [ObservableProperty]
    private ProjectViewModel? _selectedProject;

    [ObservableProperty]
    private BoreholeViewModel? _selectedBorehole;

    /// <summary>
    /// 钻孔信息保存后触发，通知订阅者（如三维视图）需要重新生成场景。
    /// 使用事件而非"翻转 bool"的方式，语义更清晰且不依赖属性变更检测。
    /// </summary>
    public event EventHandler? BoreholeSaved;

    /// <summary>
    /// 触发 BoreholeSaved 事件。供"保存钻孔"与"导入照片后孔深变化"等入口统一调用。
    /// </summary>
    public void NotifyBoreholeSaved() => BoreholeSaved?.Invoke(this, EventArgs.Empty);

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private double _analysisProgress;

    [ObservableProperty]
    private bool _analysisProgressVisible;

    // ===== 岩心信息段（由阶段三分析结果自动聚合生成） =====
    [ObservableProperty]
    private ObservableCollection<BoreholeIntegritySegment> _boreholeIntegritySegments = new();

    public RockHardnessLevel[] HardnessLevelOptions { get; } = {
        RockHardnessLevel.StrongRock,
        RockHardnessLevel.MediumHardRock,
        RockHardnessLevel.RelativelySoftRock,
        RockHardnessLevel.SoftRock
    };

    public RockHomogeneity[] HomogeneityOptions { get; } = {
        RockHomogeneity.HomogeneousNoWeakLayer,
        RockHomogeneity.HasWeakLayer
    };

    public GroundwaterCondition[] GroundwaterOptions { get; } = {
        GroundwaterCondition.Dry,
        GroundwaterCondition.Damp,
        GroundwaterCondition.Wet
    };

    private List<CaveAxisOptionViewModel>? _caveAxisOptionsCache;
    private double _caveAxisCacheThreshold = -1;

    public List<CaveAxisOptionViewModel> CaveAxisOptions
    {
        get
        {
            double threshold = _specificationService.GetCaveAxisThreshold();
            if (_caveAxisOptionsCache != null && Math.Abs(_caveAxisCacheThreshold - threshold) < 0.001)
                return _caveAxisOptionsCache;

            _caveAxisOptionsCache = new List<CaveAxisOptionViewModel>
            {
                new CaveAxisOptionViewModel { Text = "未设置", Value = null },
                new CaveAxisOptionViewModel { Text = $"小于{threshold:F0}°", Value = true },
                new CaveAxisOptionViewModel { Text = $"大于等于{threshold:F0}°", Value = false }
            };
            _caveAxisCacheThreshold = threshold;
            return _caveAxisOptionsCache;
        }
    }

    // ===== 图像分析结果展示 =====
    [ObservableProperty]
    private ObservableCollection<CorePhotoViewModel> _analyzedPhotos = new();

    [ObservableProperty]
    private CorePhotoViewModel? _selectedAnalysisPhoto;

    [ObservableProperty]
    private ObservableCollection<StructuralPlane> _structuralPlanes = new();

    [ObservableProperty]
    private ObservableCollection<AnalysisMetrics> _analysisMetricsList = new();

    [ObservableProperty]
    private string _analysisDetail = string.Empty;

    [ObservableProperty]
    private ObservableCollection<IntegritySegmentViewModel> _integritySegments = new();

    [ObservableProperty]
    private ObservableCollection<DevelopmentSegmentViewModel> _developmentSegments = new();

    // ===== 阶段五：围岩分类与项目统计 =====
    [ObservableProperty]
    private ObservableCollection<ClassificationSegmentViewModel> _classificationSegments = new();

    [ObservableProperty]
    private string _currentBoreholeClassSummary = string.Empty;

    [ObservableProperty]
    private ProjectStatisticsViewModel _projectStatistics = new();

    // ===== 标注图像显示 =====
    [ObservableProperty]
    private BitmapImage? _annotatedImage;

    [ObservableProperty]
    private string _annotatedImagePath = string.Empty;

    [ObservableProperty]
    private bool _isAnnotatedImageLoading;

    public MainViewModel(
        IProjectRepository projectRepository,
        IBoreholeRepository boreholeRepository,
        ICorePhotoRepository corePhotoRepository,
        IBoreholeIntegritySegmentRepository boreholeIntegritySegmentRepository,
        IStructuralPlaneRepository structuralPlaneRepository,
        IAnalysisMetricsRepository analysisMetricsRepository,
        IClassificationSegmentRepository classificationSegmentRepository,
        IClassificationService classificationService,
        ProjectStatisticsService projectStatisticsService,
        ISpecificationService specificationService)
    {
        _projectRepository = projectRepository;
        _boreholeRepository = boreholeRepository;
        _corePhotoRepository = corePhotoRepository;
        _boreholeIntegritySegmentRepository = boreholeIntegritySegmentRepository;
        _structuralPlaneRepository = structuralPlaneRepository;
        _analysisMetricsRepository = analysisMetricsRepository;
        _classificationSegmentRepository = classificationSegmentRepository;
        _classificationService = classificationService;
        _projectStatisticsService = projectStatisticsService;
        _specificationService = specificationService;
    }

    private readonly ISpecificationService _specificationService;

    public async Task LoadProjectsAsync()
    {
        var projects = await _projectRepository.GetAllAsync();
        Projects.Clear();
        foreach (var project in projects)
        {
            var projectVm = new ProjectViewModel(project, _boreholeRepository, _corePhotoRepository);
            await projectVm.LoadBoreholesAsync();
            Projects.Add(projectVm);
        }
        StatusMessage = $"已加载 {Projects.Count} 个项目";
    }

    public async Task LoadCorePhotosForSelectedBoreholeAsync()
    {
        if (SelectedBorehole == null) return;

        var photos = await _corePhotoRepository.GetByBoreholeIdAsync(SelectedBorehole.Id);
        SelectedBorehole.CorePhotos.Clear();
        foreach (var p in photos.OrderBy(x => x.DepthStart))
        {
            SelectedBorehole.CorePhotos.Add(new CorePhotoViewModel(p));
        }

        SelectedBorehole.CorePhotosCount = SelectedBorehole.CorePhotos.Count;
        StatusMessage = $"已加载 {SelectedBorehole.CorePhotos.Count} 条物理段 (钻孔 {SelectedBorehole.Number})";
    }

    // 加载当前钻孔下已分析的照片列表
    public void LoadAnalyzedPhotos()
    {
        AnalyzedPhotos.Clear();
        if (SelectedBorehole == null) return;

        var analyzed = SelectedBorehole.CorePhotos
            .Where(p => p.AnalysisStatus == "已分析")
            .OrderBy(p => p.DepthStart)
            .ToList();

        foreach (var p in analyzed)
        {
            AnalyzedPhotos.Add(p);
        }

        StatusMessage = $"当前钻孔有 {AnalyzedPhotos.Count} 张已分析照片";
    }

    // 加载选中照片的详细分析结果
    public async Task LoadAnalysisDetailAsync(CorePhotoViewModel? photo)
    {
        StructuralPlanes.Clear();
        AnalysisMetricsList.Clear();
        AnalysisDetail = string.Empty;

        if (photo == null) return;

        // 加载结构面
        var planes = await _structuralPlaneRepository.GetByCorePhotoIdAsync(photo.Id);
        foreach (var plane in planes.OrderBy(p => p.DepthInPhoto))
        {
            StructuralPlanes.Add(plane);
        }

        // 加载分析指标
        var metrics = await _analysisMetricsRepository.GetByCorePhotoIdAsync(photo.Id);
        foreach (var m in metrics)
        {
            AnalysisMetricsList.Add(m);
        }

        // 生成摘要文本
        var integrity = photo.IntegrityLevel;
        string integrityText = (integrity == IntegrityLevel.UserOverride || integrity == IntegrityLevel.Unknown) ? "未评定" : integrity.ToString();
        AnalysisDetail =
            $"照片: {photo.FileName}\n" +
            $"深度范围: {photo.DepthStart:F2} m - {photo.DepthEnd:F2} m\n" +
            $"段长: {photo.Length:F2} m\n" +
            $"岩心盒编号: {photo.BoxNumber}\n" +
            $"----------------------------------------\n" +
            $"完整性等级: {integrityText}\n" +
            $"节理数: {photo.JointCount}\n" +
            $"平均节理间距: {(photo.AvgJointSpacingCm > 0 ? $"{photo.AvgJointSpacingCm:F1} cm" : "未设置")}\n" +
            $"----------------------------------------\n" +
            $"结构面数量: {StructuralPlanes.Count}\n" +
            $"分析指标数量: {AnalysisMetricsList.Count}\n" +
            $"分析状态: {photo.AnalysisStatus}";

        StatusMessage = $"已加载照片 {photo.FileName} 的分析详情";
    }

    public ICorePhotoRepository CorePhotoRepository => _corePhotoRepository;
    public IStructuralPlaneRepository StructuralPlaneRepository => _structuralPlaneRepository;
    public IAnalysisMetricsRepository AnalysisMetricsRepository => _analysisMetricsRepository;

    // ===== 阶段三分析完成后：自动聚合到完整性等级分组 =====
    // 从所有已分析照片的完整性分段中，按完整性等级聚合并入库
    // 刷新分组是只读操作，不覆盖用户已填写的数据
    public async Task AggregateIntegritySegmentsAsync()
    {
        if (SelectedBorehole == null) return;

        // 1. 从所有已分析照片中提取 IntegritySegments
        var allSegments = new List<(double start, double end, IntegrityLevel level)>();

        foreach (var photo in SelectedBorehole.CorePhotos.Where(p => p.AnalysisStatus == "已分析"))
        {
            if (string.IsNullOrEmpty(photo.AnalysisResultJson)) continue;

            try
            {
                var result = System.Text.Json.JsonSerializer.Deserialize<ImageAnalysisResult>(photo.AnalysisResultJson);
                if (result?.IntegritySegments == null) continue;

                foreach (var seg in result.IntegritySegments)
                {
                    allSegments.Add((seg.DepthStart, seg.DepthEnd, seg.Level));
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                System.Diagnostics.Trace.WriteLine($"[RockCore] 照片 {photo.FileName} 的分析结果 JSON 解析失败，已跳过: {ex.Message}");
            }
        }

        if (allSegments.Count == 0)
        {
            StatusMessage = "无可聚合的完整性分段数据";
            return;
        }

        // 2. 按深度排序
        allSegments = allSegments.OrderBy(s => s.start).ToList();

        // 3. 模式一：同等级且深度相邻合并
        var mergedByAdjacent = MergeAdjacentSegments(allSegments);

        // 4. 模式二：同等级全部合并
        var mergedAllSameLevel = MergeAllSameLevel(allSegments);

        const double epsilon = 0.001;

        // 5. 读取旧数据
        var oldSegments = (await _boreholeIntegritySegmentRepository.GetByBoreholeIdAsync(SelectedBorehole.Id)).ToList();

        // 6. 精确匹配：新段和旧段按（深度+等级）一一对应
        // 匹配上的旧段直接保留（不动数据，不动Id）
        // 新段中匹配不上的才新增
        // 旧段中匹配不上的（已不存在的段）才删除
        var newSegmentsToAdd = new List<BoreholeIntegritySegment>();
        var matchedOldIds = new HashSet<int>();
        int exactMatched = 0;

        foreach (var s in mergedByAdjacent)
        {
            // 精确匹配：同等级 + 深度起点相同 + 深度终点相同
            var match = oldSegments.FirstOrDefault(o =>
                o.IntegrityLevel == s.level &&
                Math.Abs(o.DepthStart - s.start) < epsilon &&
                Math.Abs(o.DepthEnd - s.end) < epsilon);

            if (match != null)
            {
                matchedOldIds.Add(match.Id);
                exactMatched++;
            }
            else
            {
                // 新段，新增
                var newSeg = new BoreholeIntegritySegment
                {
                    BoreholeId = SelectedBorehole.Id,
                    DepthStart = s.start,
                    DepthEnd = s.end,
                    IntegrityLevel = s.level,
                    RockType = RockType.NotSet,
                    RockStructureType = RockStructureType.NotSet,
                    RockHardnessLevel = RockHardnessLevel.NotSet,
                    RockHomogeneity = RockHomogeneity.NotSet,
                    GroundwaterCondition = GroundwaterCondition.NotSet,
                    CaveAxisAngleLessThan30 = null,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                newSegmentsToAdd.Add(newSeg);
            }
        }

        // 旧段中匹配不上的，需要删除
        var oldSegmentsToDelete = oldSegments.Where(o => !matchedOldIds.Contains(o.Id)).ToList();

        // 7. 执行数据库操作：先删再增，匹配上的不动
        foreach (var old in oldSegmentsToDelete)
        {
            await _boreholeIntegritySegmentRepository.DeleteAsync(old.Id);
        }
        if (newSegmentsToAdd.Count > 0)
        {
            await _boreholeIntegritySegmentRepository.AddRangeAsync(newSegmentsToAdd);
        }

        // 8. 重新加载
        await LoadBoreholeIntegritySegmentsAsync();

        StatusMessage = $"已刷新 {mergedByAdjacent.Count} 个完整性分段（保留 {exactMatched} 个已有设置，新增 {newSegmentsToAdd.Count} 个，删除 {oldSegmentsToDelete.Count} 个）(钻孔 {SelectedBorehole.Number})";
    }

    // 模式一：同等级且深度相邻合并
    private List<(double start, double end, IntegrityLevel level)> MergeAdjacentSegments(
        List<(double start, double end, IntegrityLevel level)> segments)
    {
        if (segments.Count == 0) return new List<(double, double, IntegrityLevel)>();

        var result = new List<(double start, double end, IntegrityLevel level)>();
        var current = (segments[0].start, segments[0].end, segments[0].level);

        for (int i = 1; i < segments.Count; i++)
        {
            var seg = segments[i];
            // 相邻条件：前一段终点 == 后一段起点 且 等级相同
            if (Math.Abs(current.end - seg.start) < 0.001 && current.level == seg.level)
            {
                // 合并
                current = (current.start, seg.end, current.level);
            }
            else
            {
                result.Add(current);
                current = (seg.start, seg.end, seg.level);
            }
        }
        result.Add(current);
        return result;
    }

    // 模式二：同等级全部合并（跨间隙也合并）
    private List<(double start, double end, IntegrityLevel level)> MergeAllSameLevel(
        List<(double start, double end, IntegrityLevel level)> segments)
    {
        if (segments.Count == 0) return new List<(double, double, IntegrityLevel)>();

        return segments
            .GroupBy(s => s.level)
            .Select(g =>
            {
                var first = g.First();
                return (first.start, g.Max(s => s.end), g.Key);
            })
            .OrderBy(s => s.Item1)
            .ToList();
    }

    // 根据地下水埋深自动填充各分段的地下水条件，并处理跨埋深段
    public async Task HandleCrossGroundwaterSegmentsAsync()
    {
        if (SelectedBorehole == null || SelectedBorehole.GroundwaterDepth <= 0) return;

        var gwDepth = SelectedBorehole.GroundwaterDepth;
        var toUpdate = new List<BoreholeIntegritySegment>();

        // 先对不跨埋深的段直接自动填充
        foreach (var seg in BoreholeIntegritySegments.ToList())
        {
            if (seg.DepthEnd <= gwDepth && seg.GroundwaterCondition != GroundwaterCondition.Dry)
            {
                seg.GroundwaterCondition = GroundwaterCondition.Dry;
                toUpdate.Add(seg);
            }
            else if (seg.DepthStart >= gwDepth && seg.GroundwaterCondition != GroundwaterCondition.Wet)
            {
                seg.GroundwaterCondition = GroundwaterCondition.Wet;
                toUpdate.Add(seg);
            }
        }

        if (toUpdate.Count > 0)
        {
            foreach (var seg in toUpdate)
            {
                seg.UpdatedAt = DateTime.Now;
            }
            await _boreholeIntegritySegmentRepository.UpdateRangeAsync(toUpdate);
        }

        // 再处理跨埋深段：询问是否拆分
        var crossingSegments = BoreholeIntegritySegments
            .Where(s => s.DepthStart < gwDepth && s.DepthEnd > gwDepth)
            .ToList();

        foreach (var seg in crossingSegments)
        {
            var result = MessageBox.Show(
                $"段 {seg.DepthStart:F2}m - {seg.DepthEnd:F2}m 跨越地下水埋深 " +
                $"({gwDepth:F2}m)，是否拆分为两段？\n\n" +
                $"是：拆分为 {seg.DepthStart:F2}m-{gwDepth:F2}m（无地下水）" +
                $" 和 {gwDepth:F2}m-{seg.DepthEnd:F2}m（有地下水）\n" +
                $"否：整段按有地下水处理",
                "地下水埋深拆分询问", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var upperSegment = CloneSegment(seg);
                upperSegment.Id = 0;
                upperSegment.DepthEnd = gwDepth;
                upperSegment.GroundwaterCondition = GroundwaterCondition.Dry;
                upperSegment.CaveAxisAngleLessThan30 = null;

                var lowerSegment = CloneSegment(seg);
                lowerSegment.Id = 0;
                lowerSegment.DepthStart = gwDepth;
                lowerSegment.GroundwaterCondition = GroundwaterCondition.Wet;
                lowerSegment.CaveAxisAngleLessThan30 = null;

                await _boreholeIntegritySegmentRepository.DeleteAsync(seg.Id);
                await _boreholeIntegritySegmentRepository.AddAsync(upperSegment);
                await _boreholeIntegritySegmentRepository.AddAsync(lowerSegment);

                BoreholeIntegritySegments.Remove(seg);
                BoreholeIntegritySegments.Add(upperSegment);
                BoreholeIntegritySegments.Add(lowerSegment);
            }
            else
            {
                seg.GroundwaterCondition = GroundwaterCondition.Wet;
                seg.UpdatedAt = DateTime.Now;
                await _boreholeIntegritySegmentRepository.UpdateAsync(seg);
            }
        }

        await LoadBoreholeIntegritySegmentsAsync();
    }

    private static BoreholeIntegritySegment CloneSegment(BoreholeIntegritySegment source)
    {
        return new BoreholeIntegritySegment
        {
            BoreholeId = source.BoreholeId,
            DepthStart = source.DepthStart,
            DepthEnd = source.DepthEnd,
            IntegrityLevel = source.IntegrityLevel,
            RockType = source.RockType,
            RockStructureType = source.RockStructureType,
            RockHardnessLevel = source.RockHardnessLevel,
            RockHomogeneity = source.RockHomogeneity,
            GroundwaterCondition = source.GroundwaterCondition,
            CaveAxisAngleLessThan30 = source.CaveAxisAngleLessThan30,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    // 加载完整性等级分组数据
    public async Task LoadBoreholeIntegritySegmentsAsync()
    {
        if (SelectedBorehole == null) return;

        var segments = await _boreholeIntegritySegmentRepository.GetByBoreholeIdAsync(SelectedBorehole.Id);
        BoreholeIntegritySegments.Clear();
        foreach (var seg in segments.OrderBy(s => s.DepthStart))
        {
            BoreholeIntegritySegments.Add(seg);
        }
        StatusMessage = $"已加载 {BoreholeIntegritySegments.Count} 个完整性分段 (钻孔 {SelectedBorehole.Number})";
    }

    // 保存单个分段（用户编辑后）
    [RelayCommand]
    public async Task UpdateSegmentAsync(BoreholeIntegritySegment segment)
    {
        ValidateRocksConsistency(segment);
        segment.UpdatedAt = DateTime.Now;
        await _boreholeIntegritySegmentRepository.UpdateAsync(segment);
        StatusMessage = $"已保存分段 {segment.DepthStart:F2}m - {segment.DepthEnd:F2}m";
    }

    /// <summary>
    /// 保存当前界面上所有完整性分段的设置到数据库。
    /// 覆盖现有数据（Update），不删除重建，确保用户填写的内容被持久化。
    /// </summary>
    [RelayCommand]
    public async Task SaveAllIntegritySegmentsAsync()
    {
        if (SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var segments = BoreholeIntegritySegments.ToList();
        if (segments.Count == 0)
        {
            MessageBox.Show("当前没有完整性分段数据可保存", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 校验所有分段的岩质-岩体结构一致性，并更新时间戳
        foreach (var seg in segments)
        {
            ValidateRocksConsistency(seg);
            seg.UpdatedAt = DateTime.Now;
        }

        await _boreholeIntegritySegmentRepository.UpdateRangeAsync(segments);

        // 保存后重新加载，确保内存与数据库一致
        await LoadBoreholeIntegritySegmentsAsync();

        // 统计保存结果
        int withRockType = segments.Count(s => s.RockType != RockType.NotSet);
        int withStructure = segments.Count(s => s.RockStructureType != RockStructureType.NotSet);
        int total = segments.Count;

        StatusMessage = $"已保存 {total} 个完整性分段设置（岩质已填 {withRockType}/{total}，结构已填 {withStructure}/{total}）";
        MessageBox.Show(
            $"已成功保存 {total} 个完整性分段设置到数据库。\n\n" +
            $"岩质类型已填写：{withRockType}/{total}\n" +
            $"岩体结构已填写：{withStructure}/{total}\n" +
            $"未填写岩质：{total - withRockType} 个",
            "保存成功",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // 批量更新多个分段（多选后批量设置属性）
    [RelayCommand]
    public async Task BatchUpdateSegmentsAsync(IEnumerable<BoreholeIntegritySegment> segments)
    {
        foreach (var seg in segments)
        {
            ValidateRocksConsistency(seg);
            seg.UpdatedAt = DateTime.Now;
        }
        await _boreholeIntegritySegmentRepository.UpdateRangeAsync(segments);
        await LoadBoreholeIntegritySegmentsAsync();
        StatusMessage = $"已批量更新 {segments.Count()} 个分段";
    }

    // 按深度区间和指定等级批量设置分段参数
    public async Task<int> ApplySettingsToDepthRangeAsync(IntegrityLevel targetLevel, double depthStart, double depthEnd,
        RockType? rockType = null, RockStructureType? rockStructure = null,
        RockHardnessLevel? hardness = null, RockHomogeneity? homogeneity = null,
        GroundwaterCondition? groundwater = null, bool? caveAxisAngleLessThan30 = null)
    {
        var toUpdate = BoreholeIntegritySegments
            .Where(s => s.IntegrityLevel == targetLevel
                && s.DepthStart >= depthStart - 0.001
                && s.DepthEnd <= depthEnd + 0.001)
            .ToList();

        if (toUpdate.Count == 0) return 0;

        // 检查是否有分段已设置了参数，提醒用户将覆盖
        int alreadySet = toUpdate.Count(s => s.RockType != RockType.NotSet ||
                                               s.RockStructureType != RockStructureType.NotSet ||
                                               s.RockHardnessLevel != RockHardnessLevel.NotSet ||
                                               s.RockHomogeneity != RockHomogeneity.NotSet ||
                                               s.GroundwaterCondition != GroundwaterCondition.NotSet ||
                                               s.CaveAxisAngleLessThan30 != null);
        if (alreadySet > 0)
        {
            var result = MessageBox.Show(
                $"在深度 {depthStart:F2}m - {depthEnd:F2}m 范围内，有 {alreadySet} 个分段已设置了参数。\n\n" +
                "继续操作将覆盖这些分段的设置。\n\n是否继续？",
                "覆盖确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return 0;
        }

        foreach (var seg in toUpdate)
        {
            if (rockType.HasValue) seg.RockType = rockType.Value;
            if (rockStructure.HasValue) seg.RockStructureType = rockStructure.Value;
            if (hardness.HasValue) seg.RockHardnessLevel = hardness.Value;
            if (homogeneity.HasValue) seg.RockHomogeneity = homogeneity.Value;
            if (groundwater.HasValue) seg.GroundwaterCondition = groundwater.Value;
            if (caveAxisAngleLessThan30.HasValue) seg.CaveAxisAngleLessThan30 = caveAxisAngleLessThan30.Value;
            ValidateRocksConsistency(seg);
        }

        // 批量检查特殊条件：洞轴线夹角
        double threshold = _specificationService.GetCaveAxisThreshold();
        var needCaveAxisPrompt = toUpdate
            .Where(s => s.RockType == RockType.HardRock &&
                        s.RockStructureType == RockStructureType.Interbedded &&
                        s.CaveAxisAngleLessThan30 == null)
            .ToList();
        if (needCaveAxisPrompt.Count > 0)
        {
            var result = MessageBox.Show(
                $"有 {needCaveAxisPrompt.Count} 个分段为互层状结构。\n\n" +
                $"洞轴线与岩层走向夹角是否小于{threshold:F0}°？\n" +
                $"（选择\"是\"可能将围岩类别从Ⅳ类降为Ⅲ类）\n\n" +
                "选择将应用于所有符合条件的分段。",
                "洞轴线夹角询问", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var seg in needCaveAxisPrompt)
                    seg.CaveAxisAngleLessThan30 = true;
            }
            else if (result == MessageBoxResult.No)
            {
                foreach (var seg in needCaveAxisPrompt)
                    seg.CaveAxisAngleLessThan30 = false;
            }
        }

        // 批量检查特殊条件：软弱夹层
        var needWeakLayerPrompt = toUpdate
            .Where(s => s.RockType == RockType.HardRock &&
                        s.RockStructureType == RockStructureType.ThinLayered &&
                        s.IntegrityLevel == IntegrityLevel.Poor &&
                        s.RockHomogeneity == RockHomogeneity.NotSet)
            .ToList();
        if (needWeakLayerPrompt.Count > 0)
        {
            var result = MessageBox.Show(
                $"有 {needWeakLayerPrompt.Count} 个分段为薄层状结构且完整性差。\n\n" +
                "岩质是否均一，有无软弱夹层？\n" +
                "（选择\"有软弱夹层\"时，围岩类别按Ⅳ类处理）\n\n" +
                "选择将应用于所有符合条件的分段。",
                "软弱夹层询问", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var seg in needWeakLayerPrompt)
                    seg.RockHomogeneity = RockHomogeneity.HasWeakLayer;
            }
            else if (result == MessageBoxResult.No)
            {
                foreach (var seg in needWeakLayerPrompt)
                    seg.RockHomogeneity = RockHomogeneity.HomogeneousNoWeakLayer;
            }
        }

        foreach (var seg in toUpdate)
        {
            seg.UpdatedAt = DateTime.Now;
        }

        await _boreholeIntegritySegmentRepository.UpdateRangeAsync(toUpdate);
        await LoadBoreholeIntegritySegmentsAsync();
        StatusMessage = $"已更新 {toUpdate.Count} 个分段 ({targetLevel}, {depthStart:F2}m-{depthEnd:F2}m)";
        return toUpdate.Count;
    }

    // 保存前校验：岩体结构类型必须属于当前岩质类型 + 完整性等级的组合
    private void ValidateRocksConsistency(BoreholeIntegritySegment segment)
    {
        if (segment.RockType == RockType.NotSet) return;
        var validStructures = RockStructureMapping.GetStructures(segment.RockType, segment.IntegrityLevel);
        if (segment.RockStructureType != RockStructureType.NotSet && !validStructures.Contains(segment.RockStructureType))
        {
            segment.RockStructureType = RockStructureType.NotSet;
        }
    }

    // 检查并触发洞轴线夹角/软弱夹层弹框（当用户设置了特定岩质+结构组合时）
    public void CheckAndPromptSpecialConditions(BoreholeIntegritySegment segment)
    {
        double threshold = _specificationService.GetCaveAxisThreshold();
        
        // 洞轴线夹角弹框：硬质岩 + 互层状结构
        if (segment.RockType == RockType.HardRock &&
            segment.RockStructureType == RockStructureType.Interbedded &&
            segment.CaveAxisAngleLessThan30 == null)
        {
            var result = MessageBox.Show(
                $"该段岩体结构为互层状结构。\n\n" +
                $"洞轴线与岩层走向夹角是否小于{threshold:F0}°？\n" +
                $"（选择\"是\"可能将围岩类别从Ⅳ类降为Ⅲ类）",
                "洞轴线夹角询问", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                segment.CaveAxisAngleLessThan30 = true;
            else if (result == MessageBoxResult.No)
                segment.CaveAxisAngleLessThan30 = false;
            // Cancel 则保持 null
        }

        // 软弱夹层弹框：硬质岩 + 薄层状结构 + 完整性差
        if (segment.RockType == RockType.HardRock &&
            segment.RockStructureType == RockStructureType.ThinLayered &&
            segment.IntegrityLevel == IntegrityLevel.Poor &&
            segment.RockHomogeneity == RockHomogeneity.NotSet)
        {
            var result = MessageBox.Show(
                "该段岩体结构为薄层状结构且完整性差。\n\n" +
                "岩质是否均一，有无软弱夹层？\n" +
                "（选择\"有软弱夹层\"时，围岩类别按Ⅳ类处理）",
                "软弱夹层询问", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                segment.RockHomogeneity = RockHomogeneity.HasWeakLayer;
            else if (result == MessageBoxResult.No)
                segment.RockHomogeneity = RockHomogeneity.HomogeneousNoWeakLayer;
        }
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        var project = new Project
        {
            Name = "新项目",
            Phase = "",
            CaveAxisAzimuth = 0,
            CreatedAt = DateTime.Now
        };

        var created = await _projectRepository.AddAsync(project);
        var projectVm = new ProjectViewModel(created, _boreholeRepository, _corePhotoRepository);
        Projects.Insert(0, projectVm);
        SelectedProject = projectVm;
        StatusMessage = "已创建新项目";
    }

    [RelayCommand]
    private async Task DeleteProjectAsync()
    {
        if (SelectedProject == null) return;

        await _projectRepository.DeleteAsync(SelectedProject.Id);
        Projects.Remove(SelectedProject);
        SelectedProject = null;
        SelectedBorehole = null;
        ClearBoreholeData();
        StatusMessage = "已删除项目";
    }

    /// <summary>
    /// 清空所有与"当前钻孔"绑定的界面数据集合，
    /// 避免删除钻孔/项目后网格仍显示已删除钻孔的旧数据。
    /// </summary>
    private void ClearBoreholeData()
    {
        BoreholeIntegritySegments.Clear();
        IntegritySegments.Clear();
        DevelopmentSegments.Clear();
        ClassificationSegments.Clear();
        AnalyzedPhotos.Clear();
        StructuralPlanes.Clear();
        AnalysisMetricsList.Clear();
        AnnotatedImage = null;
        AnnotatedImagePath = string.Empty;
        CurrentBoreholeClassSummary = string.Empty;
        AnalysisDetail = string.Empty;
    }

    [RelayCommand]
    private async Task CreateBoreholeAsync()
    {
        if (SelectedProject == null) return;

        var borehole = new Borehole
        {
            ProjectId = SelectedProject.Id,
            Number = $"ZK-{SelectedProject.Boreholes.Count + 1:D3}",
            TotalDepth = 0,
            GroundwaterDepth = 0,
            Azimuth = 0,
            InclinationAngle = 90,
            CreatedAt = DateTime.Now
        };

        var created = await _boreholeRepository.AddAsync(borehole);
        var boreholeVm = new BoreholeViewModel(created);
        SelectedProject.Boreholes.Add(boreholeVm);
        SelectedBorehole = boreholeVm;
        StatusMessage = $"已创建钻孔 {borehole.Number}";
    }

    [RelayCommand]
    private async Task DeleteBoreholeAsync()
    {
        if (SelectedBorehole == null || SelectedProject == null) return;

        await _boreholeRepository.DeleteAsync(SelectedBorehole.Id);
        SelectedProject.Boreholes.Remove(SelectedBorehole);
        SelectedBorehole = null;
        ClearBoreholeData();
        StatusMessage = "已删除钻孔";
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (SelectedProject == null) return;

        await _projectRepository.UpdateAsync(SelectedProject.ToModel());

        if (SelectedBorehole != null)
        {
            await LoadCorePhotosForSelectedBoreholeAsync();
            LoadAnalyzedPhotos();
        }

        StatusMessage = "项目已保存";
    }

    [RelayCommand]
    private async Task SaveBoreholeAsync()
    {
        if (SelectedBorehole == null) return;

        await _boreholeRepository.UpdateAsync(SelectedBorehole.ToModel());

        await LoadCorePhotosForSelectedBoreholeAsync();
        await LoadBoreholeIntegritySegmentsAsync();
        LoadAnalyzedPhotos();

        NotifyBoreholeSaved();
        StatusMessage = "钻孔已保存";
    }

    [RelayCommand]
    public async Task RefreshAnalysisResultAsync()
    {
        if (SelectedBorehole == null) return;

        await LoadCorePhotosForSelectedBoreholeAsync();
        LoadAnalyzedPhotos();
        StatusMessage = "分析结果已刷新";
    }

    // ===== 阶段五：围岩分类 =====

    /// <summary>
    /// 加载当前钻孔已保存的分析段。
    /// </summary>
    public async Task LoadClassificationSegmentsAsync()
    {
        if (SelectedBorehole == null) return;

        var segments = await _classificationSegmentRepository.GetByBoreholeIdAsync(SelectedBorehole.Id);
        ClassificationSegments.Clear();
        foreach (var seg in segments.OrderBy(s => s.DepthStart))
        {
            ClassificationSegments.Add(new ClassificationSegmentViewModel(seg));
        }
    }

    /// <summary>
    /// 对当前钻孔执行三段融合并生成/刷新围岩分类。
    /// </summary>
    [RelayCommand]
    public async Task ClassifySelectedBoreholeAsync()
    {
        if (SelectedBorehole == null)
        {
            MessageBox.Show("请先选择钻孔", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var borehole = await _boreholeRepository.GetByIdAsync(SelectedBorehole.Id);
        if (borehole == null) return;

        var photos = await _corePhotoRepository.GetByBoreholeIdAsync(SelectedBorehole.Id);
        // 使用界面上当前显示的分段数据（用户已填写并可见的数据），而非重新从数据库加载
        var segments = BoreholeIntegritySegments.ToList();

        if (!segments.Any())
        {
            MessageBox.Show(
                "当前钻孔没有完整性分段数据。\n\n请先在「岩心编辑器」中点击「刷新分组」并填写岩质/岩体结构等参数。",
                "无法分类", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var result = await _classificationService.ClassifyBoreholeAsync(borehole, photos, segments);
            await LoadClassificationSegmentsAsync();

            if (result.UnclassifiedSegments.Count > 0)
            {
                var hints = string.Join("\n", result.UnclassifiedSegments.Take(10).Select(u => u.Hint));
                var more = result.UnclassifiedSegments.Count > 10 ? $"\n... 等共 {result.UnclassifiedSegments.Count} 处未分类" : string.Empty;
                var levelStats = string.Join("，", result.MissingRockTypeByLevel.Select(kv => $"{kv.Key}: {kv.Value}个"));
                var diagnosis = $"\n\n【诊断信息】信息段总数：{result.TotalInfoSegments}，已填岩质：{result.InfoSegmentsWithRockType}，已填结构：{result.InfoSegmentsWithStructure}，未填写：{result.TotalInfoSegments - result.InfoSegmentsWithRockType}\n未填岩质段按等级分布：{levelStats}";
                MessageBox.Show(
                    $"已生成 {result.ClassifiedSegments.Count} 个分析段，以下段落因信息缺失未分类，请补充后重新生成分类：\n\n{hints}{more}{diagnosis}",
                    "围岩分类完成（部分未分类）",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            StatusMessage = $"围岩分类完成：{result.ClassifiedSegments.Count} 个分析段，{result.UnclassifiedSegments.Count} 处未分类 (钻孔 {SelectedBorehole.Number})";
            UpdateCurrentBoreholeClassSummary(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"围岩分类失败：{ex.Message}";
            MessageBox.Show($"分类过程中发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateCurrentBoreholeClassSummary(ClassificationBoreholeResult result)
    {
        if (SelectedBorehole == null || result.ClassifiedSegments.Count == 0)
        {
            CurrentBoreholeClassSummary = string.Empty;
            return;
        }

        double totalLength = result.ClassifiedSegments.Sum(s => s.DepthEnd - s.DepthStart);
        if (totalLength <= 0)
        {
            CurrentBoreholeClassSummary = string.Empty;
            return;
        }

        var classGroups = result.ClassifiedSegments
            .GroupBy(s => s.RockClass)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                double len = g.Sum(s => s.DepthEnd - s.DepthStart);
                double ratio = len / totalLength;
                return $"{RockClassText.WithSuffix(g.Key)}：{ratio:P1}";
            });

        CurrentBoreholeClassSummary = $"{SelectedBorehole.Number}围岩分类：{string.Join("；", classGroups)}";
    }

    /// <summary>
    /// 刷新当前项目的多钻孔围岩统计。
    /// </summary>
    [RelayCommand]
    public async Task RefreshProjectStatisticsAsync()
    {
        if (SelectedProject == null)
        {
            MessageBox.Show("请先选择项目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var project = await _projectRepository.GetByIdAsync(SelectedProject.Id);
        if (project == null) return;

        var boreholes = await _boreholeRepository.GetByProjectIdAsync(SelectedProject.Id);
        var segments = await _classificationSegmentRepository.GetByProjectIdAsync(SelectedProject.Id);

        var result = _projectStatisticsService.Compute(project, boreholes, segments);
        ProjectStatistics.Update(result);

        StatusMessage = $"项目统计已刷新：已分类总长度 {result.TotalClassifiedLength:F2}m";
    }
}
