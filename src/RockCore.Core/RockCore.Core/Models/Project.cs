using System.ComponentModel;

namespace RockCore.Core.Models;

public class Project
{
    public int Id { get; set; }
    public string ProjectNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public double CaveAxisAzimuth { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}
