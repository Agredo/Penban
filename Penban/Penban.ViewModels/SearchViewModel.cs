// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Recognition;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// The search over the text that was read from the notes. It looks in the recognised text, never in
/// the ink, so a note is only findable once it has been read - which is what the switch in the
/// settings turns on and what opening a board sets going.
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    private readonly Func<IInkRecognitionService> recognition;
    private readonly IBoardService boardService;
    private readonly ICardService cardService;
    private readonly IRecognitionQueue queue;
    private readonly IDialogService dialogService;

    /// <summary>
    /// The recognition service comes as a factory rather than as a value: building it loads the
    /// model, and a page whose search is never used should not have paid for that on the way in.
    /// </summary>
    public SearchViewModel(
        Func<IInkRecognitionService> recognition,
        IBoardService boardService,
        ICardService cardService,
        IRecognitionQueue queue,
        IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(boardService);
        ArgumentNullException.ThrowIfNull(cardService);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(dialogService);

        this.recognition = recognition;
        this.boardService = boardService;
        this.cardService = cardService;
        this.queue = queue;
        this.dialogService = dialogService;
    }

    /// <summary>Notes whose text contains what was typed, the most recently read ones first.</summary>
    public ObservableCollection<SearchResultViewModel> Results { get; } = new();

    /// <summary>
    /// What was typed. Nullable because the search bar hands out null before anything is typed, and
    /// a page that throws on its own empty state is worse than one that trims an empty string.
    /// </summary>
    [ObservableProperty]
    private string? query;

    /// <summary>Whether a search is running. The list stays as it was until it is done.</summary>
    [ObservableProperty]
    private bool isSearching;

    /// <summary>
    /// Whether a search has been made at all. Nothing found before anything was typed is not a
    /// "nothing found" - it is a search nobody has made yet, and saying so would be a lie.
    /// </summary>
    [ObservableProperty]
    private bool hasSearched;

    /// <summary>Whether the empty state is shown: a search that ran and found nothing.</summary>
    public bool ShowEmptyState => HasSearched && !IsSearching && Results.Count == 0;

    /// <summary>Whether anything is still waiting to be read.</summary>
    public bool HasPending => PendingCount > 0;

    /// <summary>How many notes are still waiting to be read, as the line above the list says it.</summary>
    public string PendingLabel => string.Format(Strings.SearchStillReadingFormat, PendingCount);

    /// <summary>
    /// How many notes are still waiting to be read. Reading a board takes minutes and runs while the
    /// search happens, so a search made right after opening one can come up short through no fault
    /// of the notes - this is the difference between "nothing here" and "not yet".
    /// </summary>
    [ObservableProperty]
    private int pendingCount;

    partial void OnPendingCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(PendingLabel));
    }

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    /// <summary>
    /// Takes the queue's current count. Called when the page appears and after every search, which is
    /// often enough: the count is only ever a snapshot, and a search made later is the answer to
    /// "is it done yet" anyway.
    /// </summary>
    public void RefreshPendingCount() => PendingCount = queue.PendingCount;

    /// <summary>
    /// Searches for what was typed. Emptying the field clears the list rather than searching for
    /// nothing: an empty query is not a query, and the store would return its whole contents.
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = (Query ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            Results.Clear();
            HasSearched = false;
            RefreshPendingCount();
            Announce();
            return;
        }

        IsSearching = true;
        try
        {
            var matches = await recognition().SearchAsync(query);

            // A recognised text knows only the card it came from and a card knows only its column,
            // so the boards are read once to turn a column into a board. The boards are needed for
            // the titles and for opening a result, and reading them once beats reading one per hit.
            var boardOfColumn = new Dictionary<Guid, Board>();
            foreach (var board in await boardService.GetBoardsAsync())
            {
                foreach (var column in board.Columns)
                {
                    boardOfColumn[column.Id] = board;
                }
            }

            Results.Clear();
            foreach (var match in matches)
            {
                var card = await cardService.GetCardAsync(match.CardId);
                if (card is null || !boardOfColumn.TryGetValue(card.ColumnId, out var board))
                {
                    // The card is gone, or its column was deleted while its text was still stored.
                    // There would be nothing to open, so it is not offered.
                    continue;
                }

                Results.Add(new SearchResultViewModel(card, board.Id, match.RawText, board.Title));
            }

            HasSearched = true;
        }
        catch (Exception error)
        {
            // A search that could not read the database is not "nothing found", so the results are
            // left as they were and the failure is said out loud. Letting the exception out of the
            // command would end the app instead, which is what happened when a stored note carried a
            // null text.
            await dialogService.DisplayAlertAsync(
                Strings.SearchFailedTitle,
                string.Format(Strings.SearchFailedMessageFormat, error.Message),
                Strings.Done);
        }
        finally
        {
            IsSearching = false;
            RefreshPendingCount();
        }

        Announce();
    }

    /// <summary>
    /// The ids of every note whose recognised text contains <paramref name="text"/>, for narrowing
    /// down one board rather than listing results; no cap, because a board is filtered by it.
    /// </summary>
    public async Task<HashSet<Guid>> FindCardIdsAsync(string text)
    {
        var matches = await recognition().SearchAsync(text.Trim(), int.MaxValue);
        return matches.Select(match => match.CardId).ToHashSet();
    }

    /// <summary>
    /// The empty state follows from the result list and from the two flags, so it is announced once
    /// the list is complete - not while it is still being filled, which would flash the empty state
    /// over a search that is about to find something.
    /// </summary>
    private void Announce()
    {
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
