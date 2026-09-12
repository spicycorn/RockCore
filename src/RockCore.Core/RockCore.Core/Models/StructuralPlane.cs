namespace RockCore.Core.Models;

public class StructuralPlane
{
    public int Id { get; set; }
    public int CorePhotoId { get; set; }
    public string PlaneType { get; set; } = string.Empty;
    public double? Strike { get; set; }
    public double? Dip { get; set; }
    public double? DepthInPhoto { get; set; }
    public double? GlobalDepth { get; set; }
    public double ApertureWidthCm { get; set; }
    public string Roughness { get; set; } = string.Empty;
    public double ImageX { get; set; }
    public double ImageY { get; set; }
    public bool IsUserModified { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public CorePhoto? CorePhoto { get; set; }
}
