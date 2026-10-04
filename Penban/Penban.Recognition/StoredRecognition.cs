namespace Penban.Recognition;

/// <summary>
/// A recognised text that is stored for a card, in both the raw and the searchable form.
/// <para>
/// A card can carry two kinds of text and both are searched, so a row holds both: the text that was
/// read off the ink, and the text the user typed. They are independent - writing one leaves the other
/// as it was, so a note that carries both stays findable either way, and erasing the ink does not take
/// the typed text out of the search with it.
/// </para>
/// </summary>
/// <param name="CardId">The card the text belongs to.</param>
/// <param name="InkHash">The ink this text was produced from; the text is stale as soon as the ink changes.</param>
/// <param name="Profile">The conventions it was produced under.</param>
/// <param name="RawText">Model output as decoded, for display.</param>
/// <param name="NormalizedText">Searchable form, produced by <see cref="TextNormalizer"/>.</param>
/// <param name="MeanConfidence">Mean confidence over all lines, <c>0</c> when nothing was emitted.</param>
/// <param name="UpdatedAtUtc">When the text was written.</param>
/// <param name="TypedText">Text the user typed on the card, as written, for display.</param>
/// <param name="NormalizedTypedText">Searchable form of <paramref name="TypedText"/>.</param>
public sealed record StoredRecognition(
    Guid CardId,
    string InkHash,
    RecognizerProfile Profile,
    string RawText,
    string NormalizedText,
    float MeanConfidence,
    DateTime UpdatedAtUtc,
    string TypedText,
    string NormalizedTypedText)
{
    /// <summary>
    /// Whether it is the typed text of this card that <paramref name="normalizedQuery"/> matched,
    /// which is what decides which of the two texts a result shows. The query must already have been
    /// through <see cref="TextNormalizer"/>, like the text it is compared with.
    /// </summary>
    public bool TypedTextMatches(string normalizedQuery) =>
        NormalizedTypedText.Length > 0
        && NormalizedTypedText.Contains(normalizedQuery, StringComparison.Ordinal);
}
