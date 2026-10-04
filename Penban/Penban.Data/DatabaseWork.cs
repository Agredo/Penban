namespace Penban.Data;

/// <summary>
/// Runs the synchronous LiteDB work of the repositories away from the calling thread. LiteDB has no
/// asynchronous API of its own, so without this every repository call blocks whichever thread made
/// it - in the app that is the UI thread, which then cannot draw or react while the database is
/// read. A single gate serialises the work: all repositories share one <see cref="LiteDbContext"/>,
/// and keeping the calls apart means two of them never touch the file at the same time.
/// <para>
/// The gate is also what makes it safe to give the database file back when the app is left: work that
/// arrives while it is gone waits at the gate instead of opening it again (see <see cref="Suspend"/>).
/// </para>
/// </summary>
internal static class DatabaseWork
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Guards <see cref="inForeground"/> and <see cref="held"/>, which are read and written by callers
    /// on their own threads while the app is being left.
    /// </summary>
    private static readonly Lock Signal = new();

    /// <summary>False between <see cref="Suspend"/> and <see cref="Resume"/>, while the file is closed.</summary>
    private static bool inForeground = true;

    /// <summary>What the calls that arrived in the meantime are waiting on; null while in front.</summary>
    private static TaskCompletionSource? held;

    public static async Task<T> RunAsync<T>(Func<T> work)
    {
        await EnterAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            return await Task.Run(work).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task RunAsync(Action work)
    {
        await EnterAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await Task.Run(work).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// The same, but abandoning the call while it waits for the gate. Cancellation is only observed
    /// up to the point the work starts: LiteDB has no way to stop a statement halfway, so a call that
    /// is already inside finishes and its result is discarded by the caller.
    /// </summary>
    public static async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(work, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <inheritdoc cref="RunAsync{T}(Func{T}, CancellationToken)" />
    public static async Task RunAsync(Action work, CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(work, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Gives the database file back because the app is being left. Deliberately synchronous: the lock
    /// has to be gone before iOS suspends the process, and the continuation of an <c>await</c> is not
    /// promised to run before that happens.
    /// <para>
    /// Waiting for the gate lets a statement that is already running finish, which is what keeps a
    /// half-read note from being cut off. It cannot deadlock: the work runs on the thread pool and its
    /// continuation - the one that releases the gate - does not need the thread that is waiting here.
    /// </para>
    /// </summary>
    public static void Suspend(LiteDbContext context)
    {
        // Before the gate, so that a call arriving while this waits is already holding back instead of
        // closing in behind us.
        lock (Signal)
        {
            inForeground = false;
        }

        Gate.Wait();
        try
        {
            context.Close();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Lets the calls that arrived while the app was gone through again.</summary>
    public static void Resume()
    {
        lock (Signal)
        {
            inForeground = true;
            held?.TrySetResult();
            held = null;
        }
    }

    /// <summary>
    /// Takes the gate, waiting for the app to come back first if it has been left. The state is checked
    /// inside the gate: a call that slipped past it before <see cref="Suspend"/> took the gate would
    /// otherwise open the file right after it was closed.
    /// </summary>
    private static async Task EnterAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            Task wait;
            lock (Signal)
            {
                if (inForeground)
                {
                    return;
                }

                held ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = held.Task;
            }

            // Out of the app: the call waits rather than opening the file while it is suspended. It
            // runs when the app is back - the note is written late, not lost.
            Gate.Release();
            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
