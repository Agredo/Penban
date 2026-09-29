using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>Business logic for cards (CRUD + reordering) within a column.</summary>
public interface ICardService
{
    Task<List<Card>> GetCardsAsync(Guid columnId);

    /// <summary>
    /// Returns the card with the given id, or <c>null</c> if it no longer exists. Used to find out
    /// which board a recognised text came from, so that a search result can be opened.
    /// </summary>
    Task<Card?> GetCardAsync(Guid cardId);

    /// <summary>
    /// Queues the given cards to be read. Called when a board is opened: a note written before the
    /// text recognition existed has no stored text and would otherwise only become findable once it
    /// had been edited. A card whose ink has not changed costs a hash and nothing more.
    /// </summary>
    void QueueRecognition(IReadOnlyList<Card> cards);

    /// <summary>
    /// Queues every note in the app to be read, for the notes that were written before the text
    /// recognition was switched on. Reading otherwise only happens when a note is saved or when its
    /// board is opened, so a board nobody opens again would never become findable. A card whose ink
    /// has not changed since it was last read costs a hash and nothing more, which makes running this
    /// again cheap.
    /// </summary>
    /// <returns>How many notes were handed to the queue.</returns>
    Task<int> QueueAllRecognitionAsync();

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
