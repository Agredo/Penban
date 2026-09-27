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
    /// Maps the part of the document given by <paramref name="source"/> - the whole document by
    /// default - onto <paramref name="area"/> with a single uniform scale and centres it there, so a
    /// note looks the same wherever it is drawn: the drawing keeps its position and its size relative
    /// to the sheet instead of being enlarged to fill the surface it lands on.
    /// <para>
    /// A cropped source is what lets a note that only takes up the space its ink needs show that same
    /// drawing at its usual size, just without the empty paper around it.
    /// </para>
    /// </summary>
    public static void Draw(ICanvas canvas, IEnumerable<InkStroke>? strokes, RectF area, RectF? source = null)
    {
        if (strokes is null)
        {
            return;
        }

        var view = source is { Width: > 0f, Height: > 0f } crop
            ? crop
            : new RectF(0f, 0f, InkDocument.Size, InkDocument.Size);

        var targetWidth = Math.Max(area.Width, 1f);
        var targetHeight = Math.Max(area.Height, 1f);
        var scale = Math.Min(targetWidth / view.Width, targetHeight / view.Height);

        // Thicknesses are in document units; the canvas is scaled, so they are divided by the scale to
        // land on the wanted size on screen. Only a lower bound is needed - nothing is scaled up any
        // more, so a stroke can only come out too thin to see, never too thick.
        var minimumInkStroke = MinimumStrokeSize / scale;

        canvas.SaveState();
        canvas.Translate(
            area.X + ((targetWidth - (view.Width * scale)) / 2f),
            area.Y + ((targetHeight - (view.Height * scale)) / 2f));
        canvas.Scale(scale, scale);
        canvas.Translate(-view.X, -view.Y);

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
