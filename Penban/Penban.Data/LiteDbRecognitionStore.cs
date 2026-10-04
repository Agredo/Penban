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
    private readonly LiteDbContext context;

    public LiteDbRecognitionStore(LiteDbContext context)
    {
        // No index on the text: nothing here is looked up by it. The id is the card id and LiteDB
        // indexes it by itself, which is the one lookup that happens on every card.
        this.context = context;
    }

    /// <summary>
    /// Asked for per call, not kept: the collection belongs to the database instance of the moment,
    /// and that one is closed when the app is left (see <see cref="LiteDbContext.Suspend"/>).
    /// </summary>
    private ILiteCollection<RecognitionRow> Recognitions => context.Collection<RecognitionRow>("recognitions");

    /// <inheritdoc />
    public Task<StoredRecognition?> TryGetAsync(
        Guid cardId,
        string inkHash,
        RecognizerProfile profile,
        CancellationToken cancellationToken = default)
        => DatabaseWork.RunAsync(() =>
        {
            var row = Recognitions.FindById(cardId);

            return row is not null && row.Matches(inkHash, profile) ? row.ToStored() : null;
        }, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// What the user typed on the card is not part of a reading and is carried over from the row that
    /// is already there: a card that carries both kinds of text is read again and again as its ink
    /// changes, and none of those readings may take the typed text out of the search.
    /// </remarks>
    public Task SaveAsync(StoredRecognition recognition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recognition);

        return DatabaseWork.RunAsync(
            () =>
            {
                var row = RecognitionRow.From(recognition);

                if (Recognitions.FindById(recognition.CardId) is { } existing)
                {
                    row.TypedText = existing.TypedText;
                    row.NormalizedTypedText = existing.NormalizedTypedText;
                }

                Recognitions.Upsert(row);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The other way round from <see cref="SaveAsync"/>: here the read text is the one that is carried
    /// over. A card that was never written in ink - the whole point of typed text - has no reading at
    /// all yet, and gets its row from this call.
    /// </remarks>
    public Task SaveTextAsync(
        Guid cardId,
        string typedText,
        string normalizedTypedText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typedText);
        ArgumentNullException.ThrowIfNull(normalizedTypedText);

        return DatabaseWork.RunAsync(
            () =>
            {
                var row = Recognitions.FindById(cardId) ?? new RecognitionRow { Id = cardId };

                row.TypedText = typedText;
                row.NormalizedTypedText = normalizedTypedText;
                row.UpdatedAtUtc = DateTime.UtcNow;

                Recognitions.Upsert(row);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(Guid cardId, CancellationToken cancellationToken = default)
        => DatabaseWork.RunAsync(() => Recognitions.Delete(cardId), cancellationToken);

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
            () => Recognitions.FindAll()
                // Ordinal, because both sides have already been through TextNormalizer: comparing
                // culture-sensitively here would fold them a second time, in a different way.
                // The null tests are not defensiveness: LiteDB keeps an empty string as a null, so a
                // card that nothing was read from comes back with a null in the first of the two, and
                // a card that was never typed on has a null in the second. One such card - a doodle,
                // a single dot, a card whose lines all decoded to nothing - would otherwise make
                // every search throw.
                //
                // A card is a hit on either of its two texts. Which of them matched is not decided
                // here: the result carries both, and the caller asks the match.
                .Where(row => (row.NormalizedText is not null
                        && row.NormalizedText.Contains(normalizedQuery, StringComparison.Ordinal))
                    || (row.NormalizedTypedText is not null
                        && row.NormalizedTypedText.Contains(normalizedQuery, StringComparison.Ordinal)))
                // Most recently written first. Recognition happens after the ink was last written and
                // typing stamps the row as it is saved, so this reads as "the notes I touched last
                // come first", which is the order that helps while a card is still being worked on.
                // Ranking by relevance needs the board and the match position, which this store does
                // not have.
                .OrderByDescending(row => row.UpdatedAtUtc)
                .Take(limit)
                .Select(row => row.ToStored())
                .ToList(),
            cancellationToken);
    }
}
