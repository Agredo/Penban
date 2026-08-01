using LiteDB;
using Penban.Models;
using Penban.Services.Abstractions;

namespace Penban.Data;

/// <summary>LiteDB-backed implementation of <see cref="ICardRepository"/>.</summary>
public class LiteDbCardRepository : ICardRepository
{
    private readonly ILiteCollection<Card> cards;

    public LiteDbCardRepository(LiteDbContext context)
    {
        cards = context.Database.GetCollection<Card>("cards");
        cards.EnsureIndex(c => c.ColumnId);
    }

    public Task<List<Card>> GetByColumnAsync(Guid columnId)
        => Task.FromResult(cards.Find(c => c.ColumnId == columnId && !c.IsDeleted)
            .OrderBy(c => c.SortOrder)
            .ToList());

    public Task SaveAsync(Card card)
    {
        card.UpdatedAtUtc = DateTimeOffset.UtcNow;
        cards.Upsert(card);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid cardId)
    {
        var card = cards.FindById(cardId);
        if (card is null)
        {
            return Task.CompletedTask;
        }

        card.IsDeleted = true;
        card.UpdatedAtUtc = DateTimeOffset.UtcNow;
        cards.Update(card);
        return Task.CompletedTask;
    }
}
