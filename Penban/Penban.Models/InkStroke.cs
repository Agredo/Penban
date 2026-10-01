namespace Penban.Models;

/// <summary>A single freehand stroke, composed of an ordered list of points.</summary>
public class InkStroke
{
    public string Color { get; set; } = "#000000";

    public float Thickness { get; set; }

    /// <summary>
    /// Whether the width of this stroke follows the pressure recorded along it. It belongs to the
    /// stroke and not to the setting of the moment: that setting only ever decides how the next stroke
    /// is drawn, and a note whose strokes take on a different width when a switch is flipped is a note
    /// that changes itself. <c>null</c> means the stroke was drawn before it recorded this; such a
    /// stroke follows the setting, which is what it did when it was drawn.
    /// </summary>
    public bool? PressureSensitiveWidth { get; set; }

    public List<InkPoint> Points { get; set; } = new();
}
