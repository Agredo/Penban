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
}
