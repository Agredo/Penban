// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// Board overview: lists existing boards and removes them. Creating one and changing one both belong
/// to the board's details page, so coming back from it reads the list again rather than patching a
/// row - the board that was just written arrives with the rest, in the place the order puts it.
/// </summary>
public partial class BoardsViewModel : ObservableObject
{
    private readonly IBoardService boardService;
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;

    public BoardsViewModel(IBoardService boardService, ICardService cardService, IDialogService dialogService)
    {
        this.boardService = boardService;
        this.cardService = cardService;
        this.dialogService = dialogService;
    }

    public ObservableCollection<BoardViewModel> Boards { get; } = new();

    /// <summary>
    /// Whether the overview is still reading its rows. Every board on it is read with its summary -
    /// how much sits on it, when it was last written to, a fan of its notes - and "nothing here yet"
    /// has no business standing on screen during that.
    /// </summary>
    [ObservableProperty]
    private bool isLoading;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    /// <summary>Whether the "no boards yet" text belongs on screen: nothing there and nothing coming.</summary>
    public bool ShowEmptyState => !IsLoading && Boards.Count == 0;

    /// <summary>
    /// Reads the list from the database. The wait is carried by the command around it rather than by
    /// the reading itself, so that no way into this method - the visit to the overview, the import
    /// behind it, a page that wants a board the list no longer holds - can leave the spinner running.
    /// </summary>
    [RelayCommand]
    private async Task LoadBoardsAsync()
    {
        IsLoading = true;
        try
        {
            await ReadBoardsAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ReadBoardsAsync()
    {
        var boards = await boardService.GetBoardsAsync();

        // A row that is already there is brought up to date rather than built again, and every row is
        // complete before the first of them is shown: reading a board's summary no longer blocks, so a
        // row added up front would sit there with an empty summary until its own load came round - and
        // the list would build itself up in visible steps.
        var loaded = new List<BoardViewModel>(boards.Count);
        foreach (var board in boards)
        {
            var existing = Boards.FirstOrDefault(b => b.Id == board.Id);
            if (existing is null)
            {
                var viewModel = new BoardViewModel(board, boardService, cardService, dialogService);
                await viewModel.LoadSummaryAsync();
                loaded.Add(viewModel);
            }
            else
            {
                await existing.RefreshAsync(board);
                loaded.Add(existing);
            }
        }

        // Ordered by the date each row prints, which counts the notes written on the board as well as
        // the board itself - the same rule the caption under the title follows, so the list cannot
        // disagree with the dates written in it.
        loaded.Sort(CompareRows);

        RowList.Merge(Boards, loaded);

        // Raised once at the end, and not per row: the list is put together completely before it is
        // shown, so it cannot build itself up in visible steps.
        NotifyRowsChanged();
    }

    /// <summary>
    /// Says that the rows have changed. The empty state is read off the list rather than kept beside
    /// it, and the list raises nothing by itself, so it is said here - once per change, and never
    /// while a load is still on its way (see <see cref="IsLoading"/>).
    /// </summary>
    private void NotifyRowsChanged() => OnPropertyChanged(nameof(ShowEmptyState));

    private static int CompareRows(BoardViewModel left, BoardViewModel right)
    {
        // The place a board was dragged to comes first, so a board that was moved stays where it was
        // put however long ago it was written to. Boards that were never moved apart - every board of
        // a database written before places existed - all carry 0 and fall through to the date, which
        // is the order the overview had before.
        var byPlace = left.SortOrder.CompareTo(right.SortOrder);
        if (byPlace != 0)
        {
            return byPlace;
        }

        var byDate = right.LastEditedUtc.CompareTo(left.LastEditedUtc);
        return byDate != 0
            ? byDate
            : string.Compare(left.Title, right.Title, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Puts the dragged board where it was dropped and stores the new order, so the next visit to the
    /// overview shows the same list. The row is moved here rather than in the page: the list the
    /// reader sees and the order that is written down have to be the same one, and only this class
    /// holds both.
    /// </summary>
    public async Task MoveBoardAsync(Guid boardId, int targetIndex)
    {
        var sourceIndex = IndexOf(boardId);
        if (sourceIndex < 0 || Boards.Count < 2)
        {
            return;
        }

        var target = Math.Clamp(targetIndex, 0, Boards.Count - 1);
        if (sourceIndex == target)
        {
            return;
        }

        Boards.Move(sourceIndex, target);

        // The rows carry the new places right away, so the sort above agrees with the list until the
        // next load reads them back from the database.
        for (var index = 0; index < Boards.Count; index++)
        {
            Boards[index].SortOrder = index;
        }

        await boardService.ReorderBoardsAsync(Boards.Select(b => b.Id).ToList());
    }

    private int IndexOf(Guid boardId)
    {
        for (var index = 0; index < Boards.Count; index++)
        {
            if (Boards[index].Id == boardId)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Deletes one board and everything on it. The row goes with it, so the list does not keep a
    /// board that is no longer there.
    /// </summary>
    [RelayCommand]
    private async Task DeleteBoardAsync(Guid boardId)
    {
        var confirmed = await dialogService.DisplayConfirmationAsync(Strings.Delete, Strings.DeleteBoardConfirmation, Strings.Delete, Strings.Cancel);
        if (!confirmed)
        {
            return;
        }

        await boardService.DeleteBoardAsync(boardId);
        var board = Boards.FirstOrDefault(b => b.Id == boardId);
        if (board is not null)
        {
            Boards.Remove(board);
            NotifyRowsChanged();
        }
    }

    /// <summary>
    /// The view model of an already listed board, ready to be shown. Opening a board only needs a
    /// page on the ordinary navigation stack, which is a view-layer concern, so the tap is handled
    /// by <c>BoardsPage</c> and this lookup stays free of any navigation API.
    /// </summary>
    public BoardViewModel? FindBoard(Guid boardId) => Boards.FirstOrDefault(b => b.Id == boardId);
}
