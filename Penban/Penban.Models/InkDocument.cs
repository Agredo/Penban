namespace Penban.Models;

/// <summary>
/// The fixed coordinate space that ink coordinates are stored in.
/// <para>
/// Ink used to be stored in whatever surface pixels the renderer happened to have, which meant the
/// same stroke was drawn at a different position and scale in the note editor (a large canvas) and on
/// the board (a small preview) - the preview fitted the bounding box of the strokes into the note, so
/// a drawing in the top left corner appeared enlarged and centred on the board.
/// </para>
/// <para>
/// Every <see cref="InkPoint"/> is now expressed relative to a fixed square document of
/// <see cref="Size"/> units per side, so a stroke occupies the same relative position and the same
/// relative size everywhere it is rendered. Renderers map the document onto their surface with a
/// single uniform scale; the note is square, so the aspect ratio is preserved.
/// </para>
/// <para>
/// Stroke thickness and eraser radius are document units as well, which makes them independent of the
/// screen density and of the size of the surface they are drawn on.
/// </para>
/// </summary>
public static class InkDocument
{
    /// <summary>Width and height of the document space, in document units.</summary>
    public const float Size = 1000f;

    /// <summary>
    /// The document space version currently written by the app. Cards carry the version their strokes
    /// were captured in (<see cref="Card.InkSpaceVersion"/>) so strokes recorded in surface pixels can
    /// be converted exactly once.
    /// </summary>
    public const int CurrentSpaceVersion = 1;

    /// <summary>
    /// True while <paramref name="card"/> still holds strokes captured in the old, surface-relative
    /// coordinate space. Cards without strokes are only marked, they are not converted.
    /// </summary>
    public static bool NeedsMigration(Card card) => card.InkSpaceVersion < CurrentSpaceVersion;

    /// <summary>
    /// Bounding box of <paramref name="strokes"/> in the unit the points are currently in.
    /// Returns <c>false</c> when there is no point to measure.
    /// </summary>
    public static bool TryGetBounds(IReadOnlyList<InkStroke> strokes, out InkBounds bounds)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        foreach (var stroke in strokes)
        {
            foreach (var point in stroke.Points)
            {
                if (point.X < minX) minX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.X > maxX) maxX = point.X;
                if (point.Y > maxY) maxY = point.Y;
            }
        }

        if (minX > maxX || minY > maxY)
        {
            bounds = default;
            return false;
        }

        bounds = new InkBounds(minX, minY, maxX, maxY);
        return true;
    }

    /// <summary>
    /// Converts a card's strokes from the old surface-relative space into document space, in place.
    /// <para>
    /// The original surface size was never stored, so the bounding box of the strokes is used as the
    /// reference size: the drawing keeps its aspect ratio, is scaled to fill the document and is
    /// centred in it. That is exactly what the board preview used to do, so an existing board looks
    /// the way it did before - the difference is that the editor now agrees with it instead of
    /// stretching the same strokes across the whole note.
    /// </para>
    /// <para>
    /// The conversion is idempotent (a converted card measures <see cref="Size"/> on its longest side
    /// and maps onto itself), but the card is marked anyway so it does not run again on every load.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> when coordinates were changed. The version marker is set either way, so a caller
    /// that asked <see cref="NeedsMigration"/> first can persist the card unconditionally.
    /// </returns>
    public static bool Migrate(Card card)
    {
        if (!NeedsMigration(card))
        {
            return false;
        }

        var converted = false;

        if (TryGetBounds(card.Strokes, out var bounds))
        {
            var extent = MathF.Max(bounds.MaxExtent, 1f);
            var scale = Size / extent;
            var offsetX = (Size - (bounds.Width * scale)) / 2f;
            var offsetY = (Size - (bounds.Height * scale)) / 2f;

            foreach (var stroke in card.Strokes)
            {
                for (var i = 0; i < stroke.Points.Count; i++)
                {
                    var point = stroke.Points[i];
                    point.X = ((point.X - bounds.MinX) * scale) + offsetX;
                    point.Y = ((point.Y - bounds.MinY) * scale) + offsetY;
                    stroke.Points[i] = point;
                }
            }

            converted = true;
        }

        card.InkSpaceVersion = CurrentSpaceVersion;
        return converted;
    }
}

/// <summary>An axis-aligned bounding box, in whatever unit its points were measured in.</summary>
public readonly struct InkBounds
{
    public InkBounds(float minX, float minY, float maxX, float maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public float MinX { get; }

    public float MinY { get; }

    public float MaxX { get; }

    public float MaxY { get; }

    public float Width => MaxX - MinX;

    public float Height => MaxY - MinY;

    /// <summary>The longer side of the box; the size a square document has to fit the content.</summary>
    public float MaxExtent => MathF.Max(Width, Height);
}
