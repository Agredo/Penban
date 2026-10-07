namespace Penban.Models;

/// <summary>A Kanban board containing an ordered set of columns.</summary>
public class Board : SyncableEntity
{
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Where the board sits on the overview, ascending. Only the overview writes it, when a board is
    /// dragged to another place. A board that was never moved carries the default <c>0</c> and is
    /// placed by the date it was last written to instead, so a database written before this field
    /// existed keeps the order it had.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// The project the board belongs to, or <c>null</c> for a board that belongs to none. Every board
    /// of a database written before projects existed reads as <c>null</c>, which is exactly what
    /// "without a project" means - nothing has to be migrated.
    /// </summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Free-text labels the user gave the board, trimmed and without duplicates (see
    /// <c>TagList.Normalize</c>). Empty until the extended mode of the create dialog or the board's
    /// details page is used, so a board that predates them looks exactly as it did.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>First day the board runs, or <c>null</c> while the user has not said.</summary>
    public DateTimeOffset? StartDate { get; set; }

    /// <summary>Last day the board runs, or <c>null</c> while the user has not said.</summary>
    public DateTimeOffset? EndDate { get; set; }

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
    /// <see cref="Card.NoteColorIndex"/> follows. A picked colour is kept when the note is emptied
    /// again; only dropping the note gives it up.
    /// </summary>
    public int? NoteColorIndex { get; set; }
}
