using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Penban.ViewModels;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// Keeps the copy of the overview the home screen widget shows in step with the overview itself.
/// <para>
/// The trigger watches the board list rather than being called from every action that can change it:
/// adding, renaming and deleting a board all touch it, and so does the reload on every visit to the
/// overview - which is also how a note written on a board reaches the widget.
/// </para>
/// <para>
/// Changes are left to settle before anything is written. Rebuilding the list adds its rows one by
/// one, and each of those would otherwise be a snapshot of its own; the settle turns the whole burst
/// into a single write, of which the writer then still skips the ones that change nothing.
/// </para>
/// <para>
/// Must be driven from the thread the list belongs to - the page's own thread - because both the
/// waiting and the write return there before the list is read.
/// </para>
/// </summary>
public sealed class WidgetSnapshotTrigger : IDisposable
{
    /// <summary>How long a burst of changes is left to settle before the snapshot is written.</summary>
    public static readonly TimeSpan DefaultSettle = TimeSpan.FromSeconds(2);

    private readonly BoardsViewModel boards;
    private readonly string? folder;
    private readonly TimeSpan settle;
    private readonly HashSet<BoardViewModel> watched = [];
    private readonly object gate = new();

    private Task pending = Task.CompletedTask;
    private long version;
    private bool disposed;

    /// <summary>Watches the overview and writes into the shared folder.</summary>
    public WidgetSnapshotTrigger(BoardsViewModel boards) : this(boards, null)
    {
    }

    /// <summary>
    /// The same against an explicit folder: the shared one in the app, any folder in the checks - and
    /// none at all, which keeps the bookkeeping but writes nothing.
    /// </summary>
    public WidgetSnapshotTrigger(BoardsViewModel boards, string? folder, TimeSpan? settle = null)
    {
        this.boards = boards;
        this.folder = folder;
        this.settle = settle ?? DefaultSettle;

        boards.Boards.CollectionChanged += OnBoardsChanged;
        Watch(boards.Boards);
    }

    /// <summary>Completes when the next write is through; for the checks, which have no widget to wait on.</summary>
    public Task Idle
    {
        get
        {
            lock (gate)
            {
                return pending;
            }
        }
    }

    /// <summary>Records that the overview changed; the write follows once the changes have settled.</summary>
    public void Trigger()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            var mine = ++version;
            pending = SettleThenWriteAsync(mine);
        }
    }

    /// <summary>
    /// Writes at once instead of waiting for a burst to settle, for the moment the overview is known
    /// to be complete again.
    /// </summary>
    public Task RefreshAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return Task.CompletedTask;
            }

            // Anything still waiting to settle is no longer the current state.
            version++;
            return pending = WriteAsync();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            // A write that is still waiting to settle is no longer wanted either.
            version++;
            boards.Boards.CollectionChanged -= OnBoardsChanged;

            foreach (var board in watched)
            {
                board.PropertyChanged -= OnBoardChanged;
            }

            watched.Clear();
        }
    }

    private async Task SettleThenWriteAsync(long mine)
    {
        try
        {
            // Deliberately without ConfigureAwait: the write reads the board list, which belongs to
            // the thread this was called from and must not be enumerated anywhere else.
            await Task.Delay(settle);

            if (IsCurrent(mine))
            {
                await WriteAsync();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Widget snapshot not scheduled: {exception}");
        }
    }

    private Task WriteAsync() => WidgetSnapshotWriter.UpdateAsync(boards.Boards, folder);

    private bool IsCurrent(long mine)
    {
        lock (gate)
        {
            return !disposed && version == mine;
        }
    }

    private void OnBoardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            // A rebuilt list is not only added to: it is cleared first, which reports no old rows, so
            // what is watched is matched against the list instead of against the event.
            foreach (var board in watched.Where(board => !boards.Boards.Contains(board)).ToList())
            {
                Unwatch(board);
            }

            Watch(boards.Boards);
        }

        Trigger();
    }

    private void OnBoardChanged(object? sender, PropertyChangedEventArgs e) => Trigger();

    private void Watch(IEnumerable<BoardViewModel> items)
    {
        foreach (var board in items)
        {
            if (watched.Add(board))
            {
                board.PropertyChanged += OnBoardChanged;
            }
        }
    }

    private void Unwatch(BoardViewModel board)
    {
        if (watched.Remove(board))
        {
            board.PropertyChanged -= OnBoardChanged;
        }
    }
}
