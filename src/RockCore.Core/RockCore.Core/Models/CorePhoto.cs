using RockCore.Core.Enums;

namespace RockCore.Core.Models;

public class CorePhoto
{
    public int Id { get; set; }
    public int BoreholeId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public double DepthStart { get; set; }
    public double DepthEnd { get; set; }
    public int BoxNumber { get; set; }
    public IntegrityLevel IntegrityLevel { get; set; } = IntegrityLevel.UserOverride;
    public double IntegrityIndex { get; set; }
    public double RQD { get; set; }
    public int JointCount { get; set; }
    public double AvgJointSpacingCm { get; set; }
    public bool IsFragmented { get; set; }
    public string AnalysisStatus { get; set; } = "未分析";
    public bool IsUserModified { get; set; }
    public string? AnalysisResultJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public Borehole? Borehole { get; set; }
}
