using RockCore.Core.Enums;

namespace RockCore.Core.Models;

/// <summary>
/// 围岩初步分类判定结果。
/// </summary>
public class ClassificationResult
{
    /// <summary>
    /// 是否判定成功。当输入信息不完整时为 false，此时 RockClass 无意义，
    /// 应查看 MissingFields 提示用户补充缺失信息。
    /// </summary>
    public bool Success { get; set; }

    public RockClass RockClass { get; set; }

    /// <summary>
    /// 人类可读的判定依据文本。
    /// </summary>
    public string Basis { get; set; } = string.Empty;

    /// <summary>
    /// 当规范给出区间（如 II~III）时，记录原始区间说明。
    /// </summary>
    public string? RangeHint { get; set; }

    /// <summary>
    /// 判定失败时，列出缺少的字段名称，用于提示用户补充。
    /// </summary>
    public List<string> MissingFields { get; set; } = new();
}
