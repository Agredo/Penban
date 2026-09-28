using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Penban.ViewModels;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// Keeps the copy of the overview the home screen widget shows in step with the overview itself.
/// <para>
/// One trigger serves the whole app - it is registered as a singleton and holds whatever overview is
/// on screen - because the copy the widget reads must not depend on a page that happens to be alive.
/// </para>
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

    private readonly string? folder;
    private readonly IPreferences? preferences;
    private readonly TimeSpan settle;
    private readonly HashSet<BoardViewModel> watched = [];
    private readonly object gate = new();

    private BoardsViewModel? boards;
    private Task pending = Task.CompletedTask;
    private long version;
    private bool suppress;
    private bool disposed;

    /// <summary>Watches the overview and writes into the shared folder.</summary>
    public WidgetSnapshotTrigger(IPreferences? preferences = null) : this(WidgetSharedStorage.Directory, preferences)
    {
    }

    /// <summary>
    /// The same against an explicit folder: the shared one in the app, any folder in the checks - and
    /// none at all, which keeps the bookkeeping but writes nothing.
    /// </summary>
    public WidgetSnapshotTrigger(string? folder, IPreferences? preferences = null, TimeSpan? settle = null)
    {
        this.folder = folder;
        this.preferences = preferences;
        this.settle = settle ?? DefaultSettle;
    }

    /// <summary>
    /// Puts the overview that is on screen in charge: what it holds is what the widget shows next.
    /// Attaching again replaces the previous one - a page is made per navigation, and the list of the
    /// one that was left behind is not worth watching.
    /// </summary>
    public void Attach(BoardsViewModel overview)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            Detach();
            boards = overview;
            overview.Boards.CollectionChanged += OnBoardsChanged;
            Watch(overview.Boards);
        }

        // Showing an overview is a change of what the widget should show, even when it is the same
        // boards: the copy is written once the first of them has settled.
        Trigger();
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
            // What the trigger itself announces while it re-reads the rows is not a change of them.
            if (disposed || suppress)
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
            return pending = WriteSafelyAsync();
        }
    }

    /// <summary>
    /// A write with its failure kept where it happened. Whoever asks for one of these is a page
    /// appearing or the app going away, and neither can do anything with an error from a copy of the
    /// overview on the home screen.
    /// </summary>
    private async Task WriteSafelyAsync()
    {
        try
        {
            await WriteAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Widget snapshot not written: {exception}");
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
            Detach();
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

    private async Task WriteAsync()
    {
        // Nothing to draw from as long as no overview has been shown.
        if (boards is not { } overview)
        {
            return;
        }

        await RefreshSummariesAsync(overview);

        await WidgetSnapshotWriter.UpdateAsync(overview.Boards, folder, WidgetPreferredBoard.Get(preferences));
    }

    /// <summary>
    /// Reads every row again before it is written.
    /// <para>
    /// A row only knows what it knew when it was built: a board is not rebuilt when something is done
    /// on it - a card moved, a note written, a column renamed - so the card count and the time under
    /// the title would still be the old ones behind a newly drawn picture. And because the writer
    /// compares the rows against the copy on disk, it would not even write: as far as it can see,
    /// nothing has changed.
    /// </para>
    /// </summary>
    private async Task RefreshSummariesAsync(BoardsViewModel overview)
    {
        SetSuppress(true);
        try
        {
            // Over a copy: reading a row announces it, and the list underneath can be rebuilt while
            // this runs.
            foreach (var board in overview.Boards.ToList())
            {
                await board.LoadSummaryAsync();
            }
        }
        finally
        {
            SetSuppress(false);
        }
    }

    /// <summary>
    /// Discards the announcements the summary refresh makes. A row announces its summary whether it
    /// changed or not - it cannot tell - and each of those would be taken for a change and write the
    /// snapshot again.
    /// </summary>
    private void SetSuppress(bool value)
    {
        lock (gate)
        {
            suppress = value;
        }
    }

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
            if (boards is not { } overview)
            {
                return;
            }

            // A rebuilt list is not only added to: it is cleared first, which reports no old rows, so
            // what is watched is matched against the list instead of against the event.
            foreach (var board in watched.Where(board => !overview.Boards.Contains(board)).ToList())
            {
                Unwatch(board);
            }

            Watch(overview.Boards);
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

    /// <summary>Undoes what <see cref="Attach"/> wired up.</summary>
    private void Detach()
    {
        if (boards is { } overview)
        {
            overview.Boards.CollectionChanged -= OnBoardsChanged;
            boards = null;
        }

        foreach (var board in watched)
        {
            board.PropertyChanged -= OnBoardChanged;
        }

        watched.Clear();
    }
}
