using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>
/// Repository for cards. Cards are stored independently from their parent
/// <see cref="Board"/>/<see cref="BoardColumn"/> documents (only referenced via
/// <see cref="Card.ColumnId"/>), since a card's ink content can grow large and should
/// not bloat every board read.
/// </summary>
public interface ICardRepository
{
    Task<List<Card>> GetByColumnAsync(Guid columnId);

    Task SaveAsync(Card card);

    /// <summary>Soft-deletes the card with the given id.</summary>
    Task DeleteAsync(Guid cardId);

    /// <summary>
    /// Removes every card in the given column from the database, including cards that are already
    /// marked as deleted. Called when the column itself disappears: a card is only ever reachable
    /// through its <see cref="Card.ColumnId"/>, so anything left behind could never be opened again
    /// and would grow the file with every deleted board or column.
    /// </summary>
    Task DeleteByColumnAsync(Guid columnId);

    /// <summary>
    /// Writes every given card exactly as it is - id, column, order, timestamps and flags untouched,
    /// replacing any card that already carries the same id. Used when importing a file, where a
    /// card's <see cref="Card.ColumnId"/> has to keep pointing at the column it arrived with.
    /// </summary>
    Task SaveAllAsync(IReadOnlyList<Card> cards);

    /// <summary>
    /// Removes every card from the database, including soft-deleted ones. Used when an imported
    /// backup replaces the current content.
    /// </summary>
    Task ClearAsync();
}
