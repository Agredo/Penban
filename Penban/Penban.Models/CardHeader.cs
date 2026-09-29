namespace Penban.Models;

/// <summary>
/// The part of a <see cref="Card"/> that can be read without its ink. Reading a card whole means
/// deserialising every stroke and every point it holds, and a board that has been written on holds
/// hundreds of thousands of them; counting its cards, dating it and picking a note to draw need none
/// of that. Kept apart from <see cref="Card"/> rather than added to it, so an ink-free read of this
/// type cannot accidentally be widened into a full one.
/// </summary>
public class CardHeader
{
    public Guid Id { get; set; }

    /// <summary>Position of the card among its siblings, the order the board shows.</summary>
    public int SortOrder { get; set; }

    /// <summary>UTC timestamp of the last modification, see <see cref="SyncableEntity.UpdatedAtUtc"/>.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
