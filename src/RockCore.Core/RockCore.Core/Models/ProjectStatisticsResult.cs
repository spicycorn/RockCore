using RockCore.Core.Enums;

namespace RockCore.Core.Models;

/// <summary>
/// 项目级围岩统计结果。
/// </summary>
public class ProjectStatisticsResult
{
    public int ProjectId { get; set; }

    /// <summary>
    /// 项目岩芯总长度（米），以各钻孔 TotalDepth 之和为准。
    /// </summary>
    public double TotalCoreLength { get; set; }

    /// <summary>
    /// 已分类岩芯总长度（米）。
    /// </summary>
    public double TotalClassifiedLength { get; set; }

    /// <summary>
    /// 未分类岩芯总长度（米）。
    /// </summary>
    public double TotalUnclassifiedLength { get; set; }

    /// <summary>
    /// 各围岩类别统计。
    /// </summary>
    public List<RockClassStatistics> ClassStatistics { get; set; } = new();

    /// <summary>
    /// 各钻孔统计。
    /// </summary>
    public List<BoreholeStatistics> BoreholeStatistics { get; set; } = new();

    /// <summary>
    /// IV/V 类薄弱段列表。
    /// </summary>
    public List<WeakSection> WeakSections { get; set; } = new();

    /// <summary>
    /// 项目总体围岩分类占比文本（分母 = 钻孔总深度之和），如 "Ⅰ类：25%；Ⅱ类：20%……"。
    /// </summary>
    public string OverallClassSummary { get; set; } = string.Empty;

    /// <summary>
    /// 项目总体围岩分类占比文本（分母 = 已分析岩芯长度之和）。
    /// </summary>
    public string OverallClassSummaryByAnalyzed { get; set; } = string.Empty;
}

public class RockClassStatistics
{
    public RockClass RockClass { get; set; }

    public string ClassDescription { get; set; } = string.Empty;

    /// <summary>
    /// 累计长度（米）。
    /// </summary>
    public double TotalLength { get; set; }

    /// <summary>
    /// 占比（0~1），分母 = 项目岩芯总长度（钻孔总深度之和）。
    /// </summary>
    public double Ratio { get; set; }

    /// <summary>
    /// 占比（0~1），分母 = 项目已分析岩芯长度之和。
    /// </summary>
    public double RatioByAnalyzed { get; set; }

    /// <summary>
    /// 出现该类别的钻孔数。
    /// </summary>
    public int BoreholeCount { get; set; }

    /// <summary>
    /// 深度分布区间列表（米）。
    /// </summary>
    public List<DepthRange> DepthRanges { get; set; } = new();
}

public class BoreholeStatistics
{
    public int BoreholeId { get; set; }

    public string BoreholeNumber { get; set; } = string.Empty;

    /// <summary>
    /// 钻孔岩芯总长度（米）。
    /// </summary>
    public double TotalLength { get; set; }

    /// <summary>
    /// 已分类长度（米）。
    /// </summary>
    public double ClassifiedLength { get; set; }

    /// <summary>
    /// 未分类长度（米）。
    /// </summary>
    public double UnclassifiedLength { get; set; }

    public Dictionary<RockClass, double> ClassLengths { get; set; } = new();

    /// <summary>
    /// 各类别占比（分母 = 钻孔总深度）。
    /// </summary>
    public Dictionary<RockClass, double> ClassRatios { get; set; } = new();

    /// <summary>
    /// 各类别占比（分母 = 该钻孔已分析岩芯长度）。
    /// </summary>
    public Dictionary<RockClass, double> ClassRatiosByAnalyzed { get; set; } = new();

    /// <summary>
    /// 该钻孔围岩分类占比文本（分母 = 钻孔总深度）。
    /// </summary>
    public string ClassSummary { get; set; } = string.Empty;

    /// <summary>
    /// 该钻孔围岩分类占比文本（分母 = 已分析岩芯长度）。
    /// </summary>
    public string ClassSummaryByAnalyzed { get; set; } = string.Empty;
}

public class WeakSection
{
    public int BoreholeId { get; set; }

    public string BoreholeNumber { get; set; } = string.Empty;

    public double DepthStart { get; set; }

    public double DepthEnd { get; set; }

    public RockClass RockClass { get; set; }

    public double Length { get; set; }
}

public class DepthRange
{
    public double Start { get; set; }

    public double End { get; set; }
}
