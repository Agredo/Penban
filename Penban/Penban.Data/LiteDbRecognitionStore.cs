using LiteDB;
using Penban.Recognition;

namespace Penban.Data;

/// <summary>
/// LiteDB-backed implementation of <see cref="IRecognitionStore"/>.
/// <para>
/// The search runs in memory rather than in the database, which is deliberate. LiteDB has no
/// full-text search in this version, and its <c>Contains</c> is translated to a <c>LIKE</c> whose
/// argument is not escaped: a measured probe against 5.0.21 shows a query of <c>%</c> matching every
/// row and <c>a_b</c> also matching <c>axb</c>. Both are ordinary things to write on a note, so a
/// search built on it would return wrong results for real input. Reading the rows and comparing with
/// <see cref="string.Contains(string, StringComparison)"/> is exact, and at the size this corpus can
/// reach - recognition costs the better part of a second per line, so a store holds hundreds of rows,
/// not millions - it is also fast enough.
/// </para>
/// </summary>
public class LiteDbRecognitionStore : IRecognitionStore
{
    private readonly ILiteCollection<RecognitionRow> recognitions;

    public LiteDbRecognitionStore(LiteDbContext context)
    {
        // No index on the text: nothing here is looked up by it. The id is the card id and LiteDB
        // indexes it by itself, which is the one lookup that happens on every card.
        recognitions = context.Database.GetCollection<RecognitionRow>("recognitions");
    }

    /// <inheritdoc />
    public Task<StoredRecognition?> TryGetAsync(
        Guid cardId,
        string inkHash,
        RecognizerProfile profile,
        CancellationToken cancellationToken = default)
        => DatabaseWork.RunAsync(() =>
        {
            var row = recognitions.FindById(cardId);

            return row is not null && row.Matches(inkHash, profile) ? row.ToStored() : null;
        }, cancellationToken);

    /// <inheritdoc />
    public Task SaveAsync(StoredRecognition recognition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recognition);

        return DatabaseWork.RunAsync(
            () => recognitions.Upsert(RecognitionRow.From(recognition)),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(Guid cardId, CancellationToken cancellationToken = default)
        => DatabaseWork.RunAsync(() => recognitions.Delete(cardId), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRecognition>> SearchAsync(
        string normalizedQuery,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedQuery);

        if (normalizedQuery.Length == 0 || limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<StoredRecognition>>([]);
        }

        return DatabaseWork.RunAsync<IReadOnlyList<StoredRecognition>>(
            () => recognitions.FindAll()
                // Ordinal, because both sides have already been through TextNormalizer: comparing
                // culture-sensitively here would fold them a second time, in a different way.
                .Where(row => row.NormalizedText.Contains(normalizedQuery, StringComparison.Ordinal))
                // Most recently recognised first. Recognition happens after the ink was last written,
                // so this reads as "the notes I touched last come first", which is the order that
                // helps while a card is still being worked on. Ranking by relevance needs the board
                // and the match position, which this store does not have.
                .OrderByDescending(row => row.UpdatedAtUtc)
                .Take(limit)
                .Select(row => row.ToStored())
                .ToList(),
            cancellationToken);
    }
}
