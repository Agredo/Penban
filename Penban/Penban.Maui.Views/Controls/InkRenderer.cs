using Penban.Models;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Draws ink from <see cref="InkDocument"/> space onto a canvas, the one way the app renders a note
/// away from the editor.
/// <para>
/// This is a reduced rendering on purpose - it uses one width per stroke rather than following the
/// pressure along it, and it ignores pen tilt. The note editor shows the real thing.
/// </para>
/// </summary>
public static class InkRenderer
{
    /// <summary>A preview stroke never renders thinner than this many device-independent units,
    /// so a much-reduced note still shows its pen marks.</summary>
    private const float MinimumStrokeSize = 1f;

    /// <summary>
    /// Maps the whole document onto <paramref name="area"/> with a single uniform scale, so a note
    /// looks the same wherever it is drawn: the drawing keeps its position and its size relative to
    /// the sheet instead of being enlarged and centred in it.
    /// </summary>
    public static void Draw(ICanvas canvas, IEnumerable<InkStroke>? strokes, RectF area)
    {
        if (strokes is null)
        {
            return;
        }

        var targetWidth = Math.Max(area.Width, 1f);
        var targetHeight = Math.Max(area.Height, 1f);
        var scale = Math.Min(targetWidth / InkDocument.Size, targetHeight / InkDocument.Size);

        // Thicknesses are in document units; the canvas is scaled, so they are divided by the scale to
        // land on the wanted size on screen. Only a lower bound is needed - nothing is scaled up any
        // more, so a stroke can only come out too thin to see, never too thick.
        var minimumInkStroke = MinimumStrokeSize / scale;

        canvas.SaveState();
        canvas.Translate(area.X, area.Y);
        canvas.Scale(scale, scale);

        foreach (var stroke in strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            var thickness = Math.Max(stroke.Thickness, minimumInkStroke);

            canvas.StrokeColor = Color.FromArgb(stroke.Color);
            canvas.StrokeSize = thickness;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            if (stroke.Points.Count == 1)
            {
                var p = stroke.Points[0];
                canvas.FillColor = Color.FromArgb(stroke.Color);
                canvas.FillCircle(p.X, p.Y, thickness * 0.5f);
                continue;
            }

            for (var i = 1; i < stroke.Points.Count; i++)
            {
                var a = stroke.Points[i - 1];
                var b = stroke.Points[i];
                canvas.DrawLine(a.X, a.Y, b.X, b.Y);
            }
        }

        canvas.RestoreState();
    }
}
