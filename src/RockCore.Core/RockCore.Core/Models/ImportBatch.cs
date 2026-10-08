namespace RockCore.Core.Models;

/// <summary>
/// 数据导入批次（Excel 数据表导入的溯源记录）。
/// 同一钻孔重复导入时，旧批次及其生成的完整性分段会被替换。
/// </summary>
public class ImportBatch
{
    public int Id { get; set; }

    public int BoreholeId { get; set; }

    /// <summary>来源文件名（不含路径）</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>来源类型：Excel（预留孔内电视/声波等扩展）</summary>
    public string SourceType { get; set; } = "Excel";

    /// <summary>本批次导入的回次数</summary>
    public int RowCount { get; set; }

    /// <summary>备注（如"跳过 2 个警告行"）</summary>
    public string? Note { get; set; }

    public DateTime ImportedAt { get; set; }
}
