using Penban.Maui.Views.Pages;
using Penban.Services.Abstractions;
using Penban.ViewModels;

namespace Penban.Maui;

/// <summary>
/// Thin Shell route target for "board?id={boardId}". <see cref="BoardViewModel"/> needs a loaded
/// <see cref="Penban.Models.Board"/> up front, which Shell's constructor-time DI resolution can't
/// provide, so this page loads the board asynchronously from the "id" query parameter, then
/// pushes the real <see cref="BoardPage"/> and removes itself from the navigation stack.
/// </summary>
[QueryProperty(nameof(BoardId), "id")]
public class BoardHostPage : ContentPage
{
    private readonly IBoardService boardService;
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;
    private bool hasNavigatedToBoard;

    public BoardHostPage(IBoardService boardService, ICardService cardService, IDialogService dialogService)
    {
        this.boardService = boardService;
        this.cardService = cardService;
        this.dialogService = dialogService;
        Title = "Board";
    }

    public string BoardId { get; set; } = string.Empty;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (hasNavigatedToBoard)
        {
            return;
        }

        if (!Guid.TryParse(BoardId, out var boardId))
        {
            return;
        }

        var boards = await boardService.GetBoardsAsync();
        var board = boards.FirstOrDefault(b => b.Id == boardId);
        if (board is null)
        {
            return;
        }

        hasNavigatedToBoard = true;
        var viewModel = new BoardViewModel(board, boardService, cardService, dialogService);

        var navigation = Shell.Current.Navigation;
        await navigation.PushAsync(new BoardPage(viewModel));
        if (navigation.NavigationStack.Contains(this))
        {
            navigation.RemovePage(this);
        }
    }
}
