using Penban.ViewModels;

namespace Penban.Maui.Views.Pages;

/// <summary>Board overview page: lists existing boards, allows creating/deleting/opening them.</summary>
public partial class BoardsPage : ContentPage
{
    private bool isOpeningBoard;

    public BoardsPage(BoardsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is BoardsViewModel viewModel)
        {
            viewModel.LoadBoardsCommand.Execute(null);
        }
    }

    /// <summary>
    /// Opens the tapped board by pushing its page onto the plain navigation stack. Shell route
    /// navigation (GoToAsync) throws "Pending Navigations still processing" on Windows, which
    /// left the section's stack inconsistent and made the way back impossible; a plain push pops
    /// cleanly again.
    /// </summary>
    private async void OnBoardTapped(object? sender, TappedEventArgs e)
    {
        // Tapping a note twice before the push settles would push the board twice.
        if (isOpeningBoard || sender is not Element { BindingContext: BoardViewModel board } || BindingContext is not BoardsViewModel viewModel)
        {
            return;
        }

        isOpeningBoard = true;
        try
        {
            await Navigation.PushAsync(new BoardPage(viewModel.FindBoard(board.Id) ?? board));
        }
        finally
        {
            isOpeningBoard = false;
        }
    }
}