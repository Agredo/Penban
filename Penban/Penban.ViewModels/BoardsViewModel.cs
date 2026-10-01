// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>Board overview: lists existing boards, allows creating and deleting them.</summary>
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

    /// <summary>Whether any boards exist; drives the empty state on the overview page.</summary>
    public bool HasBoards => Boards.Count > 0;

    [RelayCommand]
    private async Task LoadBoardsAsync()
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

        MergeRows(loaded);

        // Raised once at the end: the empty state must not flash while the rows are still loading.
        OnPropertyChanged(nameof(HasBoards));
    }

    private static int CompareRows(BoardViewModel left, BoardViewModel right)
    {
        var byDate = right.LastEditedUtc.CompareTo(left.LastEditedUtc);
        return byDate != 0
            ? byDate
            : string.Compare(left.Title, right.Title, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Puts the freshly read rows into <see cref="Boards"/> without taking out the ones that are
    /// already there. Emptying the list and filling it again raises a reset, and a reset sends the
    /// overview back to the top: the reader would lose the place they had scrolled to every time a
    /// board was opened and closed. Only the rows that really moved, appeared or went away raise an
    /// event here, and a row that stays where it is raises none at all.
    /// </summary>
    private void MergeRows(IReadOnlyList<BoardViewModel> ordered)
    {
        for (var index = 0; index < ordered.Count; index++)
        {
            var viewModel = ordered[index];
            var current = Boards.IndexOf(viewModel);

            if (current < 0)
            {
                Boards.Insert(index, viewModel);
            }
            else if (current != index)
            {
                Boards.Move(current, index);
            }
        }

        while (Boards.Count > ordered.Count)
        {
            Boards.RemoveAt(Boards.Count - 1);
        }
    }

    private async Task AddBoardViewModelAsync(Board board)
    {
        var viewModel = new BoardViewModel(board, boardService, cardService, dialogService);
        await viewModel.LoadSummaryAsync();

        // At the top, where the overview keeps the board that was touched last: a board that was just
        // created is the most recently changed one, so appending it would show it in the wrong place
        // until the next load sorted it.
        Boards.Insert(0, viewModel);
    }

    [RelayCommand]
    private async Task AddBoardAsync()
    {
        var name = await dialogService.DisplayPromptAsync(Strings.AddBoard, string.Empty, Strings.Add, Strings.Cancel);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var board = await boardService.CreateBoardAsync(name);
        await AddBoardViewModelAsync(board);
        OnPropertyChanged(nameof(HasBoards));
    }

    [RelayCommand]
    private async Task RenameBoardAsync(Guid boardId)
    {
        var board = Boards.FirstOrDefault(b => b.Id == boardId);
        if (board is null)
        {
            return;
        }

        var name = await dialogService.DisplayPromptAsync(Strings.RenameBoard, string.Empty, Strings.Rename, Strings.Cancel, initialValue: board.Title);
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == board.Title)
        {
            return;
        }

        await boardService.RenameBoardAsync(boardId, name);
        board.Title = name;
    }

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
            OnPropertyChanged(nameof(HasBoards));
        }
    }

    /// <summary>
    /// The view model of an already listed board, ready to be shown. Opening a board only needs a
    /// page on the ordinary navigation stack, which is a view-layer concern, so the tap is handled
    /// by <c>BoardsPage</c> and this lookup stays free of any navigation API.
    /// </summary>
    public BoardViewModel? FindBoard(Guid boardId) => Boards.FirstOrDefault(b => b.Id == boardId);
}
