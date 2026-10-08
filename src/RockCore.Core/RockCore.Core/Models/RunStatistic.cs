using RockCore.Core.Enums;

namespace RockCore.Core.Models;

/// <summary>
/// 回次统计记录（Excel 导入的原始回次数据 + 软件重算的派生指标）。
/// 派生口径：n = ≥10cm段数 + 碎屑数；J = n − 1；S = 进尺×100÷n (cm)；RQD = ≥10cm总长÷(进尺×100)×100。
/// 完整性等级由表 F.0.4 判定引擎按 (J, S) 判定。
/// </summary>
public class RunStatistic
{
    public int Id { get; set; }

    public int BoreholeId { get; set; }

    public int ImportBatchId { get; set; }

    /// <summary>Excel 中的行号（溯源）</summary>
    public int RowNumber { get; set; }

    /// <summary>回次号（选填）</summary>
    public int? RunNo { get; set; }

    /// <summary>回次进尺（m）</summary>
    public double RunLengthM { get; set; }

    /// <summary>&lt;10cm 碎屑数（只计数）</summary>
    public int FragCount { get; set; }

    /// <summary>≥10cm 岩心段数</summary>
    public int PieceCount { get; set; }

    /// <summary>≥10cm 岩心段总长（cm）</summary>
    public double TotalLengthCm { get; set; }

    /// <summary>RQD（%），进尺无效时为 null</summary>
    public double? Rqd { get; set; }

    public double DepthStart { get; set; }

    public double DepthEnd { get; set; }

    /// <summary>节理数 J = n − 1</summary>
    public int JointCount { get; set; }

    /// <summary>平均间距 S（cm）</summary>
    public double AvgSpacingCm { get; set; }

    /// <summary>按 F.0.4 判定的完整性等级</summary>
    public IntegrityLevel IntegrityLevel { get; set; }

    public DateTime CreatedAt { get; set; }
}
