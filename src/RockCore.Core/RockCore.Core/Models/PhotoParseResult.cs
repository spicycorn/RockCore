namespace RockCore.Core.Models;

/// <summary>
/// 照片文件名解析结果。仅用于阶段 2 照片导入预览与校验。
/// </summary>
public class PhotoParseResult
{
    public string FileName { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public int BoxNumber { get; set; }

    public double DepthStart { get; set; }

    public double DepthEnd { get; set; }

    public bool ParseSucceeded { get; set; }

    public string ParseMessage { get; set; } = string.Empty;

    /// <summary>
    /// 解析后进行区间重叠 / 越界校验时产生的警告信息。
    /// </summary>
    public string? ValidationWarning { get; set; }
}
