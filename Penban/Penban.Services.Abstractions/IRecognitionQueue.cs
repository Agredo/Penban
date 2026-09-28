using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>
/// Reads the ink of cards in the background and keeps the searchable text up to date.
/// <para>
/// Recognition costs the better part of a second per line and a whole board takes minutes, so it never
/// happens on the path that saves a card: the card is written, the work is queued, and the text turns
/// up later. Nothing here blocks, and nothing here reports a result the caller has to wait for - the
/// queue raises <see cref="Recognized"/> when a card's text is ready.
/// </para>
/// </summary>
public interface IRecognitionQueue
{
    /// <summary>
    /// Queues a card for reading. Queuing a card that is already waiting replaces what is waiting, so
    /// a note saved several times in a row is read once, from the ink it was last left with.
    /// <para>
    /// Cards are read most recently queued first: the note the user has just closed is read before a
    /// whole board's worth of notes that was queued when the board was opened.
    /// </para>
    /// </summary>
    void Enqueue(Guid cardId, IReadOnlyList<InkStroke> strokes);

    /// <summary>
    /// Drops a card that is still waiting, for when it is deleted before it was read. A card already
    /// being read is not affected; its result is written and the caller clears it separately.
    /// </summary>
    void Forget(Guid cardId);

    /// <summary>How many cards are waiting. The card being read is not counted.</summary>
    int PendingCount { get; }

    /// <summary>Raised after a card's text was stored, so a list of matches can be built again.</summary>
    event EventHandler<Guid>? Recognized;

    /// <summary>
    /// Raised when a card could not be read. The card stays unindexed until it is queued again, which
    /// happens the next time it is saved or its board is opened.
    /// </summary>
    event EventHandler<RecognitionFailure>? Failed;
}

/// <summary>A card whose ink could not be read, and why.</summary>
public readonly record struct RecognitionFailure(Guid CardId, Exception Error);
