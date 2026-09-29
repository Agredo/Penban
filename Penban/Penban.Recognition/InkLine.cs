using Penban.Models;

namespace Penban.Recognition;

/// <summary>
/// One text line found in a card's ink: the strokes that belong to it together with their bounding
/// box in document space.
/// <para>
/// A line is the unit a recognizer works on. Which strokes form a line is decided by
/// <see cref="InkLineSegmenter"/>, not by the recognizer, because the engines differ: a line-image
/// model has to be handed one line at a time, while <c>InkAnalyzer</c>, ML Kit Digital Ink and
/// Vision segment the strokes themselves.
/// </para>
/// </summary>
public sealed class InkLine
{
    public InkLine(IReadOnlyList<InkStroke> strokes, InkBounds bounds)
    {
        Strokes = strokes;
        Bounds = bounds;
    }

    public IReadOnlyList<InkStroke> Strokes { get; }

    public InkBounds Bounds { get; }
}
