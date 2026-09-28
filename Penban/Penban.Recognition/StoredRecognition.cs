namespace Penban.Recognition;

/// <summary>
/// A recognised text that is stored for a card, in both the raw and the searchable form.
/// </summary>
/// <param name="CardId">The card the text belongs to.</param>
/// <param name="InkHash">The ink this text was produced from; the text is stale as soon as the ink changes.</param>
/// <param name="Profile">The conventions it was produced under.</param>
/// <param name="RawText">Model output as decoded, for display.</param>
/// <param name="NormalizedText">Searchable form, produced by <see cref="TextNormalizer"/>.</param>
/// <param name="MeanConfidence">Mean confidence over all lines, <c>0</c> when nothing was emitted.</param>
/// <param name="UpdatedAtUtc">When the text was written.</param>
public sealed record StoredRecognition(
    Guid CardId,
    string InkHash,
    RecognizerProfile Profile,
    string RawText,
    string NormalizedText,
    float MeanConfidence,
    DateTime UpdatedAtUtc);
