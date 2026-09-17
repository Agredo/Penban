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

    /// <summary>
    /// Paper colour the user picked for this note, or <c>null</c> while the colour is still derived
    /// from <see cref="SyncableEntity.Id"/> - which is how cards created before the colour picker
    /// keep looking exactly as they did.
    /// </summary>
    public int? NoteColorIndex { get; set; }
}
