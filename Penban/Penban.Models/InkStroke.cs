namespace Penban.Models;

/// <summary>A single freehand stroke, composed of an ordered list of points.</summary>
public class InkStroke
{
    public string Color { get; set; } = "#000000";

    public float Thickness { get; set; }

    public List<InkPoint> Points { get; set; } = new();
}
