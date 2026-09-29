using Penban.Models;

namespace Penban.Recognition;

/// <summary>
/// Draws ink strokes into a single-channel raster.
/// <para>
/// There is exactly one definition of what a stroke looks like, because the same geometry has to
/// serve two purposes: the row profile that finds the lines, and the line images the model reads.
/// If those two disagreed about where a stroke ends, the lines would be cut in one place and rendered
/// from another, and a line box would no longer describe the image it belongs to.
/// </para>
/// <para>
/// A stroke is drawn as a capsule - a swept disc - so it has round caps and round joins. The shape
/// matters more than it looks: a square cap would extend the stroke by half its width along the
/// writing direction, which shifts the measured top and bottom of a line and therefore which rows
/// count as ink. Handwriting is mostly short segments meeting at sharp angles, which is exactly where
/// square caps and mitre joins distort the most.
/// </para>
/// </summary>
public static class InkRasterizer
{
    /// <summary>Ink, i.e. a pixel the stroke covers.</summary>
    public const byte Ink = 1;

    /// <summary>Paper, i.e. a pixel the stroke leaves alone.</summary>
    public const byte Paper = 0;

    /// <summary>
    /// Stamps one stroke into <paramref name="pixels"/>, or-ing it in, so overlapping strokes stay ink.
    /// </summary>
    /// <param name="pixels">Row-major canvas of <paramref name="width"/> * <paramref name="height"/> bytes.</param>
    /// <param name="scale">Document units to canvas pixels.</param>
    /// <param name="offsetX">Canvas x of document x = 0.</param>
    /// <param name="offsetY">Canvas y of document y = 0.</param>
    /// <param name="thicknessPixels">
    /// Stroke width in canvas pixels. When <c>null</c> the stroke's own thickness is scaled and
    /// rounded, which is the convention the reference implementation measured with.
    /// </param>
    public static void StampStroke(
        byte[] pixels,
        int width,
        int height,
        InkStroke stroke,
        float scale,
        float offsetX,
        float offsetY,
        float? thicknessPixels = null)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(stroke);

        if (stroke.Points.Count == 0)
        {
            return;
        }

        var thickness = thicknessPixels ?? MathF.Max(1f, MathF.Round(stroke.Thickness * scale));
        var radius = thickness / 2f;

        var points = stroke.Points;

        if (points.Count == 1)
        {
            var point = points[0];
            StampDisc(
                pixels, width, height,
                (point.X * scale) + offsetX,
                (point.Y * scale) + offsetY,
                radius);

            return;
        }

        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];

            StampCapsule(
                pixels, width, height,
                (a.X * scale) + offsetX,
                (a.Y * scale) + offsetY,
                (b.X * scale) + offsetX,
                (b.Y * scale) + offsetY,
                radius);
        }
    }

    /// <summary>
    /// Marks every pixel whose centre lies inside the capsule around segment <c>a</c>-<c>b</c> with
    /// radius <paramref name="radius"/>.
    /// <para>
    /// Testing pixel centres rather than pixel areas is what keeps a stroke the width it claims to be.
    /// Covering every pixel the shape merely touches would make a one-unit stroke two units wide, and
    /// the extra ink is not uniform - it depends on how the stroke is angled, so it shifts line
    /// boundaries in some places and not others.
    /// </para>
    /// </summary>
    private static void StampCapsule(
        byte[] pixels,
        int width,
        int height,
        float ax,
        float ay,
        float bx,
        float by,
        float radius)
    {
        var r = MathF.Max(radius, 0.5f);

        var left = Math.Max(0, (int)MathF.Ceiling(MathF.Min(ax, bx) - r - 0.5f));
        var right = Math.Min(width - 1, (int)MathF.Floor(MathF.Max(ax, bx) + r - 0.5f));
        var top = Math.Max(0, (int)MathF.Ceiling(MathF.Min(ay, by) - r - 0.5f));
        var bottom = Math.Min(height - 1, (int)MathF.Floor(MathF.Max(ay, by) + r - 0.5f));

        if (right < left || bottom < top)
        {
            return;
        }

        var abx = bx - ax;
        var aby = by - ay;
        var lengthSquared = (abx * abx) + (aby * aby);
        var radiusSquared = r * r;

        for (var y = top; y <= bottom; y++)
        {
            var py = y + 0.5f - ay;
            var row = y * width;

            for (var x = left; x <= right; x++)
            {
                var px = x + 0.5f - ax;

                var t = lengthSquared <= 0f
                    ? 0f
                    : Math.Clamp(((px * abx) + (py * aby)) / lengthSquared, 0f, 1f);

                var dx = px - (t * abx);
                var dy = py - (t * aby);

                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    pixels[row + x] = Ink;
                }
            }
        }
    }

    /// <summary>Marks every pixel whose centre lies inside the disc around a centre point.</summary>
    private static void StampDisc(
        byte[] pixels,
        int width,
        int height,
        float cx,
        float cy,
        float radius)
    {
        var r = MathF.Max(radius, 0.5f);

        var left = Math.Max(0, (int)MathF.Ceiling(cx - r - 0.5f));
        var right = Math.Min(width - 1, (int)MathF.Floor(cx + r - 0.5f));
        var top = Math.Max(0, (int)MathF.Ceiling(cy - r - 0.5f));
        var bottom = Math.Min(height - 1, (int)MathF.Floor(cy + r - 0.5f));

        if (right < left || bottom < top)
        {
            return;
        }

        var radiusSquared = r * r;

        for (var y = top; y <= bottom; y++)
        {
            var dy = y + 0.5f - cy;
            var row = y * width;

            for (var x = left; x <= right; x++)
            {
                var dx = x + 0.5f - cx;

                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    pixels[row + x] = Ink;
                }
            }
        }
    }
}
