using RockCore.Core.Enums;

namespace RockCore.Core.Models;

public class ClassificationSegment
{
    public int Id { get; set; }
    public int BoreholeId { get; set; }
    public double DepthStart { get; set; }
    public double DepthEnd { get; set; }
    public RockClass RockClass { get; set; }
    public IntegrityLevel IntegrityLevel { get; set; }
    public RockType RockType { get; set; }
    public RockStructureType RockStructureType { get; set; }
    public GroundwaterCondition GroundwaterCondition { get; set; }
    public double ConfidenceScore { get; set; }
    public string Basis { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public Borehole? Borehole { get; set; }
}
