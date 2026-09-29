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

    public Task<Card?> GetAsync(Guid cardId) => DatabaseWork.RunAsync(() =>
    {
        var card = cards.FindById(cardId);
        return card is not null && !card.IsDeleted ? card : null;
    });

    public Task<List<Card>> GetByColumnAsync(Guid columnId)
        => DatabaseWork.RunAsync(() => cards.Find(c => c.ColumnId == columnId && !c.IsDeleted)
            .OrderBy(c => c.SortOrder)
            .ToList());

    public Task<List<CardHeader>> GetHeadersByColumnAsync(Guid columnId)
        => DatabaseWork.RunAsync(() => cards.Query()
            .Where(c => c.ColumnId == columnId && !c.IsDeleted)
            .OrderBy(c => c.SortOrder)
            // A projection, not a Find: LiteDB evaluates it over the stored document and hands back
            // only the three fields named here. Reading the cards whole instead would deserialise
            // every point of every note on the board, which on a board that has been written on is
            // the difference between milliseconds and seconds.
            .Select(c => new CardHeader
            {
                Id = c.Id,
                SortOrder = c.SortOrder,
                UpdatedAtUtc = c.UpdatedAtUtc,
            })
            .ToList());

    public Task<List<Card>> GetManyAsync(IReadOnlyList<Guid> cardIds) => DatabaseWork.RunAsync(() =>
    {
        var found = new List<Card>(cardIds.Count);
        foreach (var id in cardIds)
        {
            var card = cards.FindById(id);
            if (card is not null && !card.IsDeleted)
            {
                found.Add(card);
            }
        }

        // In the order they were asked for, so that the caller can let the order of the ids carry
        // the order the cards are shown in.
        return found;
    });

    public Task<List<Card>> GetAllAsync()
        => DatabaseWork.RunAsync(() => cards.Find(c => !c.IsDeleted).ToList());

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
