namespace RockCore.Core.Models;

public class AnalysisMetrics
{
    public int Id { get; set; }
    public int CorePhotoId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public string MetricValue { get; set; } = string.Empty;
    public string MetricUnit { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public CorePhoto? CorePhoto { get; set; }
}
