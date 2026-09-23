using Penban.Maui.Views.Services;
using Penban.Services.Abstractions;
using Penban.ViewModels;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Pages;

/// <summary>Board overview page: lists existing boards, allows creating/deleting/opening them.</summary>
public partial class BoardsPage : ContentPage
{
    private readonly IPreferences preferences;
    private readonly SettingsViewModel settingsViewModel;
    private readonly FeedbackViewModel feedbackViewModel;
    private readonly TransferCoordinator transferCoordinator;
    private bool isOpeningBoard;

    public BoardsPage(BoardsViewModel viewModel, SettingsViewModel settingsViewModel, FeedbackViewModel feedbackViewModel, IPreferences preferences, TransferCoordinator transferCoordinator)
    {
        InitializeComponent();
        this.settingsViewModel = settingsViewModel;
        this.feedbackViewModel = feedbackViewModel;
        this.preferences = preferences;
        this.transferCoordinator = transferCoordinator;
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
            await Navigation.PushAsync(new BoardPage(viewModel.FindBoard(board.Id) ?? board, preferences, transferCoordinator));
        }
        finally
        {
            isOpeningBoard = false;
        }
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage(settingsViewModel, feedbackViewModel));
    }

    private async void OnTransferClicked(object? sender, EventArgs e)
    {
        // An import can replace or extend the database behind this page's back, so the list is
        // rebuilt from the database whenever the coordinator reports a change.
        if (await transferCoordinator.ShowDatabaseMenuAsync() && BindingContext is BoardsViewModel viewModel)
        {
            viewModel.LoadBoardsCommand.Execute(null);
        }
    }

    private async void OnShareBoardClicked(object? sender, EventArgs e)
    {
        if (sender is Element { BindingContext: BoardViewModel board })
        {
            await transferCoordinator.ShowBoardExportMenuAsync(board.Id);
        }
    }
}