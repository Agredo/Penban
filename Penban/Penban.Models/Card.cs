namespace Penban.Models;

/// <summary>
/// A Kanban card. Card content is intentionally freehand/handwriting only,
/// represented as a list of ink strokes rather than typed text.
/// </summary>
public class Card : SyncableEntity
{
    public Guid ColumnId { get; set; }

    /// <summary>Determines the display order of this card relative to its siblings.</summary>
    public int SortOrder { get; set; }

    /// <summary>The entire visible content of the card: the handwritten ink strokes.</summary>
    public List<InkStroke> Strokes { get; set; } = new();
}
