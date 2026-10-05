using Penban.Models;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Draws ink from <see cref="InkDocument"/> space onto a canvas, the one way the app renders a note
/// away from the editor.
/// <para>
/// This is a reduced rendering on purpose - it uses one width per stroke rather than following the
/// pressure along it, and it ignores pen tilt. The one width is measured off the pressures the stroke
/// was written with, so a note reads at the same weight here as it does in the editor - see
/// <see cref="WidthShareOf"/>. The note editor shows the real thing, down to the last segment.
/// </para>
/// </summary>
public static class InkRenderer
{
    /// <summary>A preview stroke never renders thinner than this many device-independent units,
    /// so a much-reduced note still shows its pen marks.</summary>
    private const float MinimumStrokeSize = 1f;

    /// <summary>
    /// The share of its width a stroke keeps when the pen pressed no harder than nothing at all - the
    /// same share the editor draws its segments with, so the two agree on how thin a light line is.
    /// </summary>
    private const float MinPressureWidthShare = 0.4f;

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

            var thickness = Math.Max(stroke.Thickness * WidthShareOf(stroke), minimumInkStroke);

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

            // One path per stroke rather than one line per pair of points: the round caps and joins
            // make the two look the same, and a board full of notes costs about half as much to draw.
            var path = new PathF();
            path.MoveTo(stroke.Points[0].X, stroke.Points[0].Y);
            for (var i = 1; i < stroke.Points.Count; i++)
            {
                path.LineTo(stroke.Points[i].X, stroke.Points[i].Y);
            }

            canvas.DrawPath(path);
        }

        canvas.RestoreState();
    }

    /// <summary>
    /// How much of the pen's width the stroke was written with, as a share of it.
    /// <para>
    /// The editor follows the pressure from point to point; here a single width has to stand for the
    /// whole stroke, so it is read as the mean of the shares the editor would draw its segments with -
    /// including the rule that a segment whose two ends together report no pressure at all is read as
    /// full pressure, which is what a finger or a mouse reports. A line written with a hard press
    /// therefore comes out as thick on the board as it does in the note, and a light one as thin, which
    /// is what was missing while this took the pen's width and nothing else: the editor had already
    /// narrowed the stroke, so every touched-down line looked thicker here than the one that was
    /// written with no pressure at all.
    /// </para>
    /// <para>
    /// A stroke drawn with the pressure setting turned off says so and keeps the full width of its pen.
    /// One from before the stroke carried that setting says nothing about it and is read the way the
    /// editor reads it, as one that follows the pressure - which is also the way the setting starts.
    /// </para>
    /// </summary>
    private static float WidthShareOf(InkStroke stroke)
    {
        if (stroke.PressureSensitiveWidth is false)
        {
            return 1f;
        }

        var points = stroke.Points;

        // A single point is drawn as a dot, and the editor draws a dot at the width of the pen.
        if (points.Count < 2)
        {
            return 1f;
        }

        var shares = 0f;
        for (var i = 1; i < points.Count; i++)
        {
            var pressure = (points[i - 1].Pressure + points[i].Pressure) / 2f;
            pressure = pressure > 0f ? Math.Clamp(pressure, 0f, 1f) : 1f;
            shares += MinPressureWidthShare + ((1f - MinPressureWidthShare) * pressure);
        }

        return shares / (points.Count - 1);
    }
}
