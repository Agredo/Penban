// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;
using Penban.Services.Abstractions;

namespace Penban.Services;

/// <summary>Default, platform-agnostic implementation of <see cref="ICardService"/>.</summary>
public class CardService : ICardService
{
    private readonly ICardRepository repository;

    public CardService(ICardRepository repository)
    {
        this.repository = repository;
    }

    public Task<List<Card>> GetCardsAsync(Guid columnId) => repository.GetByColumnAsync(columnId);

    public async Task<Card> CreateCardAsync(Guid columnId)
    {
        var existing = await repository.GetByColumnAsync(columnId);
        var card = new Card
        {
            ColumnId = columnId,
            SortOrder = existing.Count,
        };

        await repository.SaveAsync(card);
        return card;
    }

    public Task SaveCardAsync(Card card) => repository.SaveAsync(card);

    public Task DeleteCardAsync(Guid cardId) => repository.DeleteAsync(cardId);

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
}
