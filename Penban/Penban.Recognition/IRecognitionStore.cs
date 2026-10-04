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

    /// <summary>Writes or replaces the read text for a card, leaving its typed text as it is.</summary>
    Task SaveAsync(StoredRecognition recognition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes or replaces the text the user typed on a card, leaving the read text as it is.
    /// <para>
    /// Typed text needs no model and no queue: it is searchable the moment it is written, which is
    /// why it comes in through this door rather than through the recognition service. Both forms have
    /// to be handed in, and they are folded by the same <see cref="TextNormalizer"/> the read text
    /// goes through - a query is compared against the two in one and the same form.
    /// </para>
    /// </summary>
    Task SaveTextAsync(
        Guid cardId,
        string typedText,
        string normalizedTypedText,
        CancellationToken cancellationToken = default);

    /// <summary>Drops the stored text of a card, e.g. when the card is deleted.</summary>
    Task RemoveAsync(Guid cardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the stored texts whose searchable form contains <paramref name="normalizedQuery"/> -
    /// the form read off the ink, or the form that was typed, whichever matches.
    /// The query must already have been through <see cref="TextNormalizer"/>.
    /// </summary>
    Task<IReadOnlyList<StoredRecognition>> SearchAsync(
        string normalizedQuery,
        int limit,
        CancellationToken cancellationToken = default);
}
