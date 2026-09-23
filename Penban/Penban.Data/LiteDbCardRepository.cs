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
        => DatabaseWork.RunAsync(() => cards.Find(c => c.ColumnId == columnId && !c.IsDeleted)
            .OrderBy(c => c.SortOrder)
            .ToList());

    public Task SaveAsync(Card card) => DatabaseWork.RunAsync(() =>
    {
        card.UpdatedAtUtc = DateTimeOffset.UtcNow;
        cards.Upsert(card);
    });

    public Task DeleteAsync(Guid cardId) => DatabaseWork.RunAsync(() =>
    {
        var card = cards.FindById(cardId);
        if (card is null)
        {
            return;
        }

        card.IsDeleted = true;
        card.UpdatedAtUtc = DateTimeOffset.UtcNow;
        cards.Update(card);
    });

    public Task DeleteByColumnAsync(Guid columnId) => DatabaseWork.RunAsync(() =>
    {
        cards.DeleteMany(c => c.ColumnId == columnId);
    });

    public Task SaveAllAsync(IReadOnlyList<Card> imported) => DatabaseWork.RunAsync(() =>
    {
        // Upsert rather than Insert, for the same reason as in LiteDbBoardRepository: write the file's
        // cards verbatim, and let a duplicate id in a malformed file overwrite instead of throwing.
        if (imported.Count > 0)
        {
            cards.Upsert(imported);
        }
    });

    public Task ClearAsync() => DatabaseWork.RunAsync(() =>
    {
        cards.DeleteAll();
    });
}
