using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>Business logic for cards (CRUD + reordering) within a column.</summary>
public interface ICardService
{
    Task<List<Card>> GetCardsAsync(Guid columnId);

    Task<Card> CreateCardAsync(Guid columnId);

    /// <summary>Persists the card, e.g. after the ink canvas is closed. No explicit "save" action is exposed to the user.</summary>
    Task SaveCardAsync(Card card);

    Task DeleteCardAsync(Guid cardId);

    /// <summary>Persists a new card order after the user reorders cards via touch drag.</summary>
    Task ReorderCardsAsync(Guid columnId, IReadOnlyList<Guid> orderedCardIds);

    /// <summary>
    /// Moves a card into <paramref name="targetColumnId"/> at <paramref name="newIndex"/>, re-persisting
    /// the SortOrder of every remaining card in both the source and target columns. Used when the user
    /// drags a card across columns (or reorders it within the same column).
    /// </summary>
    Task MoveCardAsync(Guid cardId, Guid sourceColumnId, Guid targetColumnId, int newIndex);
}
