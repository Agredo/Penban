namespace Penban.Recognition;

/// <summary>
/// Persists recognised text and finds it again.
/// <para>
/// The store is keyed by card, and every read states the ink hash and profile it is looking for. That
/// is what makes staleness impossible to miss: a card whose ink changed no longer matches its stored
/// hash, and text produced under a different render convention no longer matches its profile. Both
/// cases read as "not recognised yet" and are re-run, rather than returning text that looks valid but
/// describes different ink.
/// </para>
/// </summary>
public interface IRecognitionStore
{
    /// <summary>
    /// Returns the stored text for <paramref name="cardId"/> only when it was produced from exactly
    /// this ink under exactly this profile; otherwise <c>null</c>.
    /// </summary>
    Task<StoredRecognition?> TryGetAsync(
        Guid cardId,
        string inkHash,
        RecognizerProfile profile,
        CancellationToken cancellationToken = default);

    /// <summary>Writes or replaces the stored text for a card.</summary>
    Task SaveAsync(StoredRecognition recognition, CancellationToken cancellationToken = default);

    /// <summary>Drops the stored text of a card, e.g. when the card is deleted.</summary>
    Task RemoveAsync(Guid cardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the stored texts whose searchable form contains <paramref name="normalizedQuery"/>.
    /// The query must already have been through <see cref="TextNormalizer"/>.
    /// </summary>
    Task<IReadOnlyList<StoredRecognition>> SearchAsync(
        string normalizedQuery,
        int limit,
        CancellationToken cancellationToken = default);
}
