namespace Penban.Data;

/// <summary>
/// Runs the synchronous LiteDB work of the repositories away from the calling thread. LiteDB has no
/// asynchronous API of its own, so without this every repository call blocks whichever thread made
/// it - in the app that is the UI thread, which then cannot draw or react while the database is
/// read. A single gate serialises the work: all repositories share one <see cref="LiteDbContext"/>,
/// and keeping the calls apart means two of them never touch the file at the same time.
/// </summary>
internal static class DatabaseWork
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> RunAsync<T>(Func<T> work)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
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
        await Gate.WaitAsync().ConfigureAwait(false);
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
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(work, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
