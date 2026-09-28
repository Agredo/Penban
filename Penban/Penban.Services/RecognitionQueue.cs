// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;
using Penban.Recognition;
using Penban.Services.Abstractions;

namespace Penban.Services;

/// <inheritdoc />
/// <remarks>
/// One card is read at a time, on a single background worker. Reading is CPU-bound and the recogniser
/// already spreads a line across the machine's cores, so running cards side by side would divide the
/// same cores rather than use more of them - while competing with the drawing the user is doing.
/// </remarks>
public sealed class RecognitionQueue : IRecognitionQueue, IAsyncDisposable
{
    private readonly Func<IInkRecognitionService> recognition;

    /// <summary>
    /// Guards the queue and the worker handle. Both are touched from the caller's thread (queuing) and
    /// from the worker's, and a card is enqueued from the UI thread while a card is being read.
    /// </summary>
    private readonly Lock gate = new();

    /// <summary>Waiting cards, most recently queued first.</summary>
    private readonly LinkedList<Work> waiting = [];

    /// <summary>The same cards by id, so queuing one again can find and move its entry.</summary>
    private readonly Dictionary<Guid, LinkedListNode<Work>> queued = [];

    /// <summary>Counts the waiting cards. One release per card, so the worker never spins.</summary>
    private readonly SemaphoreSlim signal = new(0);

    private readonly CancellationTokenSource shutdown = new();

    /// <summary>
    /// Read once, in the constructor: the worker is started from a queued card and could in principle
    /// look at the source after it was disposed, which a token read is not allowed to do.
    /// </summary>
    private readonly CancellationToken stopped;

    /// <summary>Set when the queue is being torn down, so a late card cannot be queued into nothing.</summary>
    private bool closed;

    private Task? worker;

    /// <summary>
    /// The recognition service is taken as a factory rather than as a value, because building it loads
    /// the model. A queue that is created when a board opens - and stays empty when nothing on that
    /// board was ever written - should not pay for that.
    /// </summary>
    public RecognitionQueue(Func<IInkRecognitionService> recognition)
    {
        ArgumentNullException.ThrowIfNull(recognition);

        this.recognition = recognition;
        stopped = shutdown.Token;
    }

    /// <inheritdoc />
    public int PendingCount
    {
        get
        {
            lock (gate)
            {
                return waiting.Count;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<Guid>? Recognized;

    /// <inheritdoc />
    public event EventHandler<RecognitionFailure>? Failed;

    /// <inheritdoc />
    public void Enqueue(Guid cardId, IReadOnlyList<InkStroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);

        lock (gate)
        {
            if (closed)
            {
                return;
            }

            var work = new Work(cardId, strokes);

            if (queued.TryGetValue(cardId, out var entry))
            {
                // Already waiting: the newest ink takes the place of the old, and moves to the front
                // because it is now the most recently touched card. No release, since the worker is
                // already counting on this card.
                entry.Value = work;
                waiting.Remove(entry);
                waiting.AddFirst(entry);
                return;
            }

            queued[cardId] = waiting.AddFirst(work);
            signal.Release();

            // Started on the first card rather than in the constructor, so an unused queue holds no
            // background machinery at all.
            worker ??= Task.Run(() => RunAsync(stopped));
        }
    }

    /// <inheritdoc />
    public void Forget(Guid cardId)
    {
        lock (gate)
        {
            if (queued.Remove(cardId, out var entry))
            {
                waiting.Remove(entry);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? running;
        lock (gate)
        {
            closed = true;
            running = worker;
            worker = null;
            waiting.Clear();
            queued.Clear();
        }

        await shutdown.CancelAsync().ConfigureAwait(false);

        // Wakes a worker that is sitting on the signal, so it can see the cancellation and stop.
        signal.Release();

        if (running is not null)
        {
            await running.ConfigureAwait(false);
        }

        signal.Dispose();
        shutdown.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!TryTake(out var work))
            {
                continue;
            }

            await RecognizeAsync(work, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecognizeAsync(Work work, CancellationToken cancellationToken)
    {
        try
        {
            await recognition()
                .RecognizeAndStoreAsync(work.CardId, work.Strokes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down, or the card was dropped while it was being read. Either way
            // there is nothing to report and nothing to keep.
            return;
        }
        catch (Exception exception)
        {
            // One card that cannot be read must not stop the queue: the remaining cards are still
            // worth reading, and this one is tried again the next time it is saved.
            Failed?.Invoke(this, new RecognitionFailure(work.CardId, exception));
            return;
        }

        Recognized?.Invoke(this, work.CardId);
    }

    private bool TryTake(out Work work)
    {
        lock (gate)
        {
            if (waiting.First is not { } first)
            {
                work = default;
                return false;
            }

            waiting.RemoveFirst();
            queued.Remove(first.Value.CardId);
            work = first.Value;
            return true;
        }
    }

    /// <summary>One card waiting to be read, with the ink it had when it was queued.</summary>
    private readonly record struct Work(Guid CardId, IReadOnlyList<InkStroke> Strokes);
}
