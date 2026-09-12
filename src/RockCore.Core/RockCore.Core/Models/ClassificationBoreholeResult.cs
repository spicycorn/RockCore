namespace RockCore.Core.Models;

/// <summary>
/// 单个钻孔的三段融合与围岩分类结果。
/// </summary>
public class ClassificationBoreholeResult
{
    /// <summary>
    /// 成功分类的分析段。
    /// </summary>
    public List<ClassificationSegment> ClassifiedSegments { get; set; } = new();

    /// <summary>
    /// 因信息缺失而无法分类的区间及提示。
    /// </summary>
    public List<UnclassifiedSegment> UnclassifiedSegments { get; set; } = new();

    /// <summary>
    /// 诊断信息：信息段总数。
    /// </summary>
    public int TotalInfoSegments { get; set; }

    /// <summary>
    /// 诊断信息：已填写岩质类型的信息段数量。
    /// </summary>
    public int InfoSegmentsWithRockType { get; set; }

    /// <summary>
    /// 诊断信息：已填写岩体结构类型的信息段数量。
    /// </summary>
    public int InfoSegmentsWithStructure { get; set; }

    /// <summary>
    /// 诊断信息：未填岩质的段按完整性等级统计。
    /// </summary>
    public Dictionary<string, int> MissingRockTypeByLevel { get; set; } = new();
}

/// <summary>
/// 因信息缺失而无法分类的区间。
/// </summary>
public class UnclassifiedSegment
{
    public int BoreholeId { get; set; }

    public double DepthStart { get; set; }

    public double DepthEnd { get; set; }

    /// <summary>
    /// 提示用户补充缺失字段的文本。
    /// </summary>
    public string Hint { get; set; } = string.Empty;

    /// <summary>
    /// 缺失的字段名称列表。
    /// </summary>
    public List<string> MissingFields { get; set; } = new();
}
