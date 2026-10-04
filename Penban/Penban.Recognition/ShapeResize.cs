using Penban.Models;

namespace Penban.Recognition;

/// <summary>
/// A shape that is still under the pen that drew it: the pen was left standing on the stroke long
/// enough for it to be read - see <see cref="ShapeRecognizer"/> - and is now pulling the shape into
/// the size it is to have, so the size is chosen while the shape is being drawn rather than by drawing
/// it again.
/// <para>
/// The corner of the shape's box furthest from where the pen stands stays where it is, and the pen
/// moves the rest: the part of the shape under the pen follows the pen, and the two directions are
/// scaled on their own, so a circle pulled sideways becomes the ellipse it was pulled into. A line has
/// no box worth pulling - the end the pen is at is simply moved to where the pen is, so the line can be
/// turned and stretched in one movement.
/// </para>
/// <para>
/// The shape is held in the frame it was read in, because that is the only frame its box is its own:
/// pulling an upright box around a slanted rectangle would shear it into a parallelogram instead of
/// resizing it.
/// </para>
/// </summary>
public sealed class ShapeResize
{
    /// <summary>
    /// How far the shape may be pulled in, as a share of the size it was read at, before it stops
    /// following the pen. A shape pulled past this would be a line, or nothing at all, and there is no
    /// way back from either of those by moving the pen.
    /// </summary>
    private const float MinimumScale = 0.15f;

    /// <summary>And how far it may be pulled out, which only has to be a bound rather than a size.</summary>
    private const float MaximumScale = 20f;

    /// <summary>
    /// How far the part of the shape under the pen has to stand from the corner that stays put before
    /// the pull is measured against it. Below this the pen is pulling from the corner itself, where
    /// the smallest movement would be a wild change of size.
    /// </summary>
    private const float MinimumReach = 1f;

    private readonly RecognizedShape shape;

    /// <summary>The frame the shape was read in, which is the one it is pulled in.</summary>
    private readonly float angle;

    /// <summary>Where the pen stood when the shape was read, in that frame.</summary>
    private readonly float startX;
    private readonly float startY;

    /// <summary>The corner that stays put, in that frame.</summary>
    private readonly float anchorX;
    private readonly float anchorY;

    private ShapeResize(RecognizedShape shape, float angle, float startX, float startY, float anchorX, float anchorY)
    {
        this.shape = shape;
        this.angle = angle;
        this.startX = startX;
        this.startY = startY;
        this.anchorX = anchorX;
        this.anchorY = anchorY;
    }

    /// <summary>
    /// Starts pulling <paramref name="shape"/> from where the pen stands at
    /// <paramref name="penX"/>/<paramref name="penY"/>, which is where the pen was when the shape was
    /// read: the shape is the size it was read at until the pen moves again. Returns <c>null</c> when
    /// the shape is too small to have a box to pull.
    /// </summary>
    public static ShapeResize? Begin(RecognizedShape shape, float penX, float penY)
    {
        // A line is not pulled into a size but moved at one end - see At - so it needs none of this.
        if (shape.Shape == InkShape.Line)
        {
            return new ShapeResize(shape, 0f, 0f, 0f, 0f, 0f);
        }

        var pen = Turned(penX, penY, shape.Angle);
        var box = ShapeRecognizer.Box(ShapeRecognizer.Turned(shape.Points, shape.Angle));

        if (box.Width < ShapeRecognizer.MinimumExtent || box.Height < ShapeRecognizer.MinimumExtent)
        {
            return null;
        }

        // The pen is somewhere on the shape it has just drawn, so the corner of the box across the
        // middle from it is the one that stays put. Pulling the shape away from that corner moves the
        // ink under the pen, and only the ink under the pen.
        var anchorX = pen.X < (box.MinX + box.MaxX) / 2f ? box.MaxX : box.MinX;
        var anchorY = pen.Y < (box.MinY + box.MaxY) / 2f ? box.MaxY : box.MinY;

        return new ShapeResize(shape, shape.Angle, pen.X, pen.Y, anchorX, anchorY);
    }

    /// <summary>
    /// The points the shape is drawn from with the pen at <paramref name="penX"/>/<paramref name="penY"/>,
    /// in the note's own frame.
    /// </summary>
    public IReadOnlyList<InkPoint> At(float penX, float penY)
    {
        var first = shape.Points[0];

        // The line is the one shape whose points are not a box: it was read as the very points the pen
        // was put down on and lifted from, so the pen that is still on it is holding its far end.
        if (shape.Shape == InkShape.Line)
        {
            var last = shape.Points[^1];
            return [first, ShapeRecognizer.With(last, penX, penY, last.Pressure, last.TimestampMs)];
        }

        var pen = Turned(penX, penY, angle);
        var scaleX = Scale(pen.X, startX, anchorX);
        var scaleY = Scale(pen.Y, startY, anchorY);
        var turned = ShapeRecognizer.Turned(shape.Points, angle);
        var pulled = new List<InkPoint>(turned.Count);

        foreach (var point in turned)
        {
            pulled.Add(ShapeRecognizer.With(
                point,
                anchorX + ((point.X - anchorX) * scaleX),
                anchorY + ((point.Y - anchorY) * scaleY),
                point.Pressure,
                point.TimestampMs));
        }

        return ShapeRecognizer.Turned(pulled, -angle);
    }

    /// <summary>
    /// How far the shape is pulled along one of the two directions of its box: the pen moves the part
    /// of the shape it stands on, so what the pen has travelled from where it started is measured
    /// against how far that part stood from the corner that stays put.
    /// </summary>
    private static float Scale(float pen, float start, float anchor)
    {
        var reach = start - anchor;

        if (MathF.Abs(reach) < MinimumReach)
        {
            return 1f;
        }

        return Math.Clamp(1f + ((pen - start) / reach), MinimumScale, MaximumScale);
    }

    /// <summary>A point of the note in the frame the shape was read in.</summary>
    private static (float X, float Y) Turned(float x, float y, float angle)
    {
        if (angle == 0f)
        {
            return (x, y);
        }

        var sin = MathF.Sin(angle);
        var cos = MathF.Cos(angle);
        return ((x * cos) - (y * sin), (x * sin) + (y * cos));
    }
}
