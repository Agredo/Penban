// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>Board overview: lists existing boards, allows creating and deleting them.</summary>
public partial class BoardsViewModel : ObservableObject
{
    private readonly IBoardService boardService;
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;
    private readonly INavigation navigation;
    private bool isNavigating;

    public BoardsViewModel(IBoardService boardService, ICardService cardService, IDialogService dialogService, INavigation navigation)
    {
        this.boardService = boardService;
        this.cardService = cardService;
        this.dialogService = dialogService;
        this.navigation = navigation;
    }

    public ObservableCollection<BoardViewModel> Boards { get; } = new();

    [RelayCommand]
    private async Task LoadBoardsAsync()
    {
        var boards = await boardService.GetBoardsAsync();
        Boards.Clear();
        foreach (var board in boards)
        {
            Boards.Add(new BoardViewModel(board, boardService, cardService, dialogService));
        }
    }

    [RelayCommand]
    private async Task AddBoardAsync()
    {
        var name = await dialogService.DisplayPromptAsync(Strings.AddBoard, string.Empty, Strings.AddBoard, Strings.Delete);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var board = await boardService.CreateBoardAsync(name);
        Boards.Add(new BoardViewModel(board, boardService, cardService, dialogService));
    }

    [RelayCommand]
    private async Task DeleteBoardAsync(Guid boardId)
    {
        var confirmed = await dialogService.DisplayConfirmationAsync(Strings.Delete, Strings.Delete, Strings.Delete, Strings.Rename);
        if (!confirmed)
        {
            return;
        }

        await boardService.DeleteBoardAsync(boardId);
        var board = Boards.FirstOrDefault(b => b.Id == boardId);
        if (board is not null)
        {
            Boards.Remove(board);
        }
    }

    [RelayCommand]
    private async Task OpenBoardAsync(Guid boardId)
    {
        // TapGestureRecognizer doesn't respect CanExecute like Button does, so a duplicate
        // tap event can invoke this command again before the first navigation finishes,
        // which throws "Pending Navigations still processing" and crashes the app. Guard manually.
        if (isNavigating)
        {
            return;
        }

        isNavigating = true;
        try
        {
            await navigation.GoToAsync($"board?id={boardId}");
        }
        catch (InvalidOperationException)
        {
            // Shell can still be settling a just-completed navigation internally for a brief
            // moment after GoToAsync's task completes; a navigation request landing in that
            // window throws "Pending Navigations still processing". Any unhandled exception
            // here crashes the whole app (WinRT turns it into a native fault on the dispatcher),
            // so we swallow this specific, harmless race instead of letting it propagate.
        }
        finally
        {
            isNavigating = false;
        }
    }
}
