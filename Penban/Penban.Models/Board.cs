namespace Penban.Models;

/// <summary>A Kanban board containing an ordered set of columns.</summary>
public class Board : SyncableEntity
{
    public string Title { get; set; } = string.Empty;

    public List<BoardColumn> Columns { get; set; } = new();

    /// <summary>
    /// The board's own note, written from the board header. Unlike a card it is not a Kanban item:
    /// it is the single note that stands for the whole board, shown on the board card of the
    /// overview and on the home screen widget. Empty until the user writes one, and a board without
    /// one looks exactly as it did before the note existed.
    /// </summary>
    public List<InkStroke> NoteStrokes { get; set; } = new();

    /// <summary>
    /// Paper colour the user picked for <see cref="NoteStrokes"/>, or <c>null</c> while the colour is
    /// still derived from <see cref="SyncableEntity.Id"/> - the same rule a card's
    /// <see cref="Card.NoteColorIndex"/> follows.
    /// </summary>
    public int? NoteColorIndex { get; set; }
}
