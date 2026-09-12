namespace RockCore.Core.Models;

public class Borehole
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Number { get; set; } = string.Empty;
    public double OrificeElevation { get; set; }
    public double TotalDepth { get; set; }
    public double GroundwaterDepth { get; set; }
    public double Azimuth { get; set; }
    public double InclinationAngle { get; set; }
    public double OrificeX { get; set; }
    public double OrificeY { get; set; }
    public double OrificeZ { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public Project? Project { get; set; }
}
