using Penban.Models;

namespace Penban.Recognition;

/// <summary>
/// Recognises card ink on demand, caches the result and answers text searches.
/// <para>
/// This is the only entry point the UI needs. It owns the two rules that keep recognition affordable
/// and honest: work is never repeated for ink that has not changed, and a result is only reused when
/// both the ink and the conventions match.
/// </para>
/// </summary>
public interface IInkRecognitionService
{
    /// <summary>
    /// Returns the recognised text of a card, recognising it first if needed. Reuses the stored result
    /// when the ink and the profile are unchanged, so calling this after every save is cheap.
    /// </summary>
    Task<StoredRecognition> RecognizeAndStoreAsync(
        Guid cardId,
        IReadOnlyList<InkStroke> strokes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cards whose recognised text matches <paramref name="query"/>. The query is folded
    /// with <see cref="TextNormalizer"/> before matching, so it is compared in the same form as the
    /// stored text.
    /// </summary>
    Task<IReadOnlyList<StoredRecognition>> SearchAsync(
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default);
}
