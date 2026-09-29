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
    /// <summary>
    /// Returns the card with the given id, or <c>null</c> if there is no such card or it has been
    /// deleted. Cards are otherwise only ever read through their column; this is what finds the card
    /// that a recognised text belongs to.
    /// </summary>
    Task<Card?> GetAsync(Guid cardId);

    Task<List<Card>> GetByColumnAsync(Guid columnId);

    /// <summary>
    /// Returns one entry per card in the column, in board order, reading no ink. This is what
    /// anything that only counts cards, dates them or picks a few of them to draw should use: which
    /// of them were written on can only be told from the ink itself, so those few have to be read
    /// whole, see <see cref="GetManyAsync"/>.
    /// </summary>
    Task<List<CardHeader>> GetHeadersByColumnAsync(Guid columnId);

    /// <summary>
    /// Returns the cards with the given ids, whole, in the order they were asked for. Ids that have
    /// no card behind them, or only a deleted one, are left out.
    /// </summary>
    Task<List<Card>> GetManyAsync(IReadOnlyList<Guid> cardIds);

    /// <summary>
    /// Returns every card that has not been deleted, in no particular order. Cards are otherwise only
    /// ever read through their column; this is what reaches the notes written before the text
    /// recognition was switched on, without reading the whole database board by board.
    /// </summary>
    Task<List<Card>> GetAllAsync();

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
