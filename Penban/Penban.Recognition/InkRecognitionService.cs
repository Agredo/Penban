using Penban.Models;

namespace Penban.Recognition;

/// <inheritdoc />
public sealed class InkRecognitionService : IInkRecognitionService
{
    /// <summary>Default number of matches a search returns; the UI pages beyond this only if it must.</summary>
    public const int DefaultSearchLimit = 50;

    private readonly IInkTextRecognizer _recognizer;
    private readonly IRecognitionStore _store;

    public InkRecognitionService(IInkTextRecognizer recognizer, IRecognitionStore store)
    {
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(store);

        _recognizer = recognizer;
        _store = store;
    }

    /// <inheritdoc />
    public async Task<StoredRecognition> RecognizeAndStoreAsync(
        Guid cardId,
        IReadOnlyList<InkStroke> strokes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strokes);

        var inkHash = InkHash.Compute(strokes);
        var profile = _recognizer.Profile;

        var cached = await _store
            .TryGetAsync(cardId, inkHash, profile, cancellationToken)
            .ConfigureAwait(false);

        if (cached is not null)
        {
            return cached;
        }

        var result = await _recognizer
            .RecognizeAsync(strokes, cancellationToken)
            .ConfigureAwait(false);

        var rawText = result.ToPlainText();
        var meanConfidence = MeanConfidence(result);

        var stored = new StoredRecognition(
            cardId,
            inkHash,
            profile,
            rawText,
            TextNormalizer.Normalize(rawText),
            meanConfidence,
            DateTime.UtcNow);

        await _store.SaveAsync(stored, cancellationToken).ConfigureAwait(false);

        return stored;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredRecognition>> SearchAsync(
        string query,
        int limit = DefaultSearchLimit,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = TextNormalizer.Normalize(query);

        if (normalizedQuery.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<StoredRecognition>>([]);
        }

        return _store.SearchAsync(normalizedQuery, limit, cancellationToken);
    }

    /// <summary>
    /// Mean confidence over the lines that produced text. Lines that emitted nothing are left out
    /// rather than counted as zero, which would make a card with one drawing and one written line look
    /// half as reliable as the same written line alone.
    /// </summary>
    private static float MeanConfidence(InkRecognitionResult result)
    {
        var sum = 0f;
        var count = 0;

        foreach (var line in result.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            sum += line.MeanConfidence;
            count++;
        }

        return count == 0 ? 0f : sum / count;
    }
}
