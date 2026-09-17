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
        Boards.Clear();
        foreach (var board in boards)
        {
            await AddBoardViewModelAsync(board);
        }

        // Raised once at the end: the empty state must not flash while the rows are still loading.
        OnPropertyChanged(nameof(HasBoards));
    }

    private async Task AddBoardViewModelAsync(Board board)
    {
        var viewModel = new BoardViewModel(board, boardService, cardService, dialogService);
        Boards.Add(viewModel);

        await viewModel.LoadSummaryAsync();
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
