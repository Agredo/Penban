// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;
using Penban.Recognition;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.Services;

/// <summary>Default, platform-agnostic implementation of <see cref="ICardService"/>.</summary>
public class CardService : ICardService
{
    private readonly ICardRepository repository;
    private readonly IPreferences preferences;
    private readonly IRecognitionQueue recognition;
    private readonly IRecognitionStore recognitionStore;

    public CardService(
        ICardRepository repository,
        IPreferences preferences,
        IRecognitionQueue recognition,
        IRecognitionStore recognitionStore)
    {
        this.repository = repository;
        this.preferences = preferences;
        this.recognition = recognition;
        this.recognitionStore = recognitionStore;
    }

    public async Task<List<Card>> GetCardsAsync(Guid columnId)
    {
        var cards = await repository.GetByColumnAsync(columnId);

        // Ink used to be stored in whatever surface pixels the renderer had; cards written before
        // document space existed have to be converted once. Migrate is idempotent and marks the card,
        // so this is a no-op from the second load on.
        foreach (var card in cards)
        {
            if (!InkDocument.NeedsMigration(card))
            {
                continue;
            }

            InkDocument.Migrate(card);
            await repository.SaveAsync(card);
        }

        return cards;
    }

    public async Task<Card> CreateCardAsync(Guid columnId)
    {
        var existing = await repository.GetByColumnAsync(columnId);
        var card = new Card
        {
            ColumnId = columnId,
            SortOrder = existing.Count,

            // A brand new note is already in document space; without this it would look like a card
            // from before the change and be run through the migration on its first load.
            InkSpaceVersion = InkDocument.CurrentSpaceVersion,

            // A new note starts in the colour the last one was given, so a run of notes does not
            // have to be recoloured one by one. Without a stored choice it stays null and
            // CardViewModel falls back to the colour derived from the card's id.
            NoteColorIndex = ReadLastNoteColor(),
        };

        await repository.SaveAsync(card);
        return card;
    }

    public Task SaveCardAsync(Card card)
    {
        // The ink goes to the queue rather than through it: reading a card takes the better part of a
        // second per line, and the card is closed by the user before this is called. Waiting here
        // would put that time on the screen for nothing. A card with no ink is queued as well - that
        // is how the text of a note the user erased is taken back out of the search.
        recognition.Enqueue(card.Id, card.Strokes ?? []);
        return repository.SaveAsync(card);
    }

    public async Task DeleteCardAsync(Guid cardId)
    {
        // Both halves of the derived data go: a queued card would be read for a card that no longer
        // exists, and stored text would keep a deleted note in the search results.
        recognition.Forget(cardId);
        await repository.DeleteAsync(cardId);
        await recognitionStore.RemoveAsync(cardId);
    }

    public async Task ReorderCardsAsync(Guid columnId, IReadOnlyList<Guid> orderedCardIds)
    {
        var cards = await repository.GetByColumnAsync(columnId);
        for (var i = 0; i < orderedCardIds.Count; i++)
        {
            var card = cards.FirstOrDefault(c => c.Id == orderedCardIds[i]);
            if (card is not null)
            {
                card.SortOrder = i;
                await repository.SaveAsync(card);
            }
        }
    }

    public async Task MoveCardAsync(Guid cardId, Guid sourceColumnId, Guid targetColumnId, int newIndex)
    {
        var sourceCards = await repository.GetByColumnAsync(sourceColumnId);
        var card = sourceCards.FirstOrDefault(c => c.Id == cardId);
        if (card is null)
        {
            return;
        }

        sourceCards.Remove(card);

        var targetCards = sourceColumnId == targetColumnId
            ? sourceCards
            : await repository.GetByColumnAsync(targetColumnId);

        newIndex = Math.Clamp(newIndex, 0, targetCards.Count);
        targetCards.Insert(newIndex, card);
        card.ColumnId = targetColumnId;

        for (var i = 0; i < targetCards.Count; i++)
        {
            targetCards[i].SortOrder = i;
            await repository.SaveAsync(targetCards[i]);
        }

        if (sourceColumnId != targetColumnId)
        {
            for (var i = 0; i < sourceCards.Count; i++)
            {
                sourceCards[i].SortOrder = i;
                await repository.SaveAsync(sourceCards[i]);
            }
        }
    }

    /// <summary>
    /// The paper colour the user last picked in the ink editor, or null if they never picked one
    /// or the stored value no longer matches the palette.
    /// </summary>
    private int? ReadLastNoteColor() =>
        int.TryParse(preferences.Get(PreferenceKeys.NoteLastColorIndex, string.Empty), out var index)
        && index >= 0
        && index < NoteStyle.PaperCount
            ? index
            : null;
}
