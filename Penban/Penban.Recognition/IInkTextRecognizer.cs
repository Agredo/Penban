using Penban.Models;

namespace Penban.Recognition;

/// <summary>
/// Turns the strokes of a card into text.
/// <para>
/// The contract takes strokes and returns lines, with nothing in between, because that is the only
/// shape all the engines can honour. A line-image model has to be handed one line at a time and
/// therefore has to find the lines itself, while <c>InkAnalyzer</c>, ML Kit Digital Ink and Apple's
/// Vision take the strokes and decide the layout internally. Putting segmentation in this interface
/// would force every caller to run a segmenter that some engines would then ignore - so it stays an
/// implementation detail behind this boundary.
/// </para>
/// <para>
/// Implementations must be safe to call concurrently and must not mutate the strokes they are given.
/// </para>
/// </summary>
public interface IInkTextRecognizer
{
    /// <summary>
    /// Stable identifier of the engine, used in the recognition cache key. It must change whenever the
    /// engine would produce different text for the same ink, so a renamed or retrained model gets a
    /// new identifier rather than silently reusing old results.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The rendering and normalisation convention this engine produces text under. Combined with
    /// <see cref="Id"/> and the ink hash it forms the cache key.
    /// </summary>
    RecognizerProfile Profile { get; }

    /// <summary>
    /// Recognises the text of one card. Returns an empty result - not <c>null</c> - when the ink
    /// contains nothing to read. Cancellation must leave no partial state behind.
    /// </summary>
    Task<InkRecognitionResult> RecognizeAsync(
        IReadOnlyList<InkStroke> strokes,
        CancellationToken cancellationToken = default);
}
