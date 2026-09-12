namespace RockCore.Core.Models;

public class ProjectSummaryStatistics
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public double TotalCoreLength { get; set; }
    public string StatisticsJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public Project? Project { get; set; }
}
