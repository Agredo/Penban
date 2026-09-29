using System.Globalization;
using Penban.Maui.Views.Services;
using Penban.Maui.Views.Widget;
using Penban.Services.Abstractions;
using Penban.Util;
using Penban.ViewModels;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Pages;

/// <summary>Board overview page: lists existing boards, allows creating/deleting/opening them.</summary>
public partial class BoardsPage : ContentPage
{
    private readonly IPreferences preferences;
    private readonly IDialogService dialogService;
    private readonly SettingsViewModel settingsViewModel;
    private readonly SearchViewModel searchViewModel;
    private readonly FeedbackViewModel feedbackViewModel;
    private readonly TransferCoordinator transferCoordinator;
    private readonly WidgetSnapshotTrigger widget;
    private readonly WidgetBoardLink boardLink;
    private bool isOpeningBoard;

    public BoardsPage(BoardsViewModel viewModel, SettingsViewModel settingsViewModel, SearchViewModel searchViewModel, FeedbackViewModel feedbackViewModel, IPreferences preferences, IDialogService dialogService, TransferCoordinator transferCoordinator, WidgetSnapshotTrigger widget, WidgetBoardLink boardLink)
    {
        InitializeComponent();
        this.settingsViewModel = settingsViewModel;
        this.searchViewModel = searchViewModel;
        this.feedbackViewModel = feedbackViewModel;
        this.preferences = preferences;
        this.dialogService = dialogService;
        this.transferCoordinator = transferCoordinator;
        this.widget = widget;
        this.boardLink = boardLink;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is not BoardsViewModel viewModel)
        {
            return;
        }

        // The home screen widget shows a copy of this list, and the overview is the one place that
        // knows the list is complete again: on every visit, whether the app was just started or a
        // board was closed with a new note on it. The trigger belongs to the app rather than to this
        // page - it is what the copy is written from even after the page is gone - so what it watches
        // is handed over on every visit instead of being tied to the first of them.
        widget.Attach(viewModel);

        await viewModel.LoadBoardsCommand.ExecuteAsync(null);
        await widget.RefreshAsync();

        // A tap on a widget that woke the app asked for its board before this page existed. The
        // activation normally opens it; if it could not - a cold start has no overview yet - it is
        // waiting here (see WidgetBoardLink).
        if (boardLink.Take() is { } boardId)
        {
            await OpenBoardAsync(boardId);
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
        if (sender is not Element { BindingContext: BoardViewModel board })
        {
            return;
        }

        await OpenBoardAsync(board.Id);
    }

    /// <summary>
    /// Shows one board of the overview. Used by the tap on a row and by the board a tapped home
    /// screen widget stands for, which is the same thing asked for by something that is not a row.
    /// </summary>
    /// <param name="boardId">The board to show.</param>
    /// <param name="openCardId">
    /// A note on that board to open on top of it. Handed in when the board was found by a search:
    /// the board is then only the way to the note the user was looking for.
    /// </param>
    public async Task OpenBoardAsync(Guid boardId, Guid? openCardId = null)
    {
        // Tapping a note twice before the push settles would push the board twice.
        if (isOpeningBoard || BindingContext is not BoardsViewModel viewModel)
        {
            return;
        }

        // The board may be gone from a list that was read a while ago: the widget can outlive a board
        // that was deleted, and an import replaces the database behind this page's back.
        var board = viewModel.FindBoard(boardId) ?? await LoadBoardAsync(viewModel, boardId);
        if (board is null)
        {
            return;
        }

        isOpeningBoard = true;
        try
        {
            // Only the overview of this board is wanted; whatever was open went with the tap.
            if (Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopToRootAsync();
            }

            await Navigation.PushAsync(new BoardPage(board, preferences, transferCoordinator, openCardId));
        }
        finally
        {
            isOpeningBoard = false;
        }
    }

    private static async Task<BoardViewModel?> LoadBoardAsync(BoardsViewModel viewModel, Guid boardId)
    {
        await viewModel.LoadBoardsCommand.ExecuteAsync(null);
        return viewModel.FindBoard(boardId);
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage(settingsViewModel, feedbackViewModel));
    }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        // The search hands a found board back to this page: opening one is the overview's job, and
        // the page that was pushed from here is gone by the time a result is tapped.
        await Navigation.PushAsync(new SearchPage(searchViewModel, OpenBoardAsync));
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

    /// <summary>
    /// Asks for a widget showing the given board, on behalf of a card held down on this page (see
    /// <see cref="BoardWidgetHoldBehavior"/>, which is what the cards in the list carry). iOS places a
    /// widget for the person using the phone and offers an app no way to do it, so nothing can be
    /// created here: the board is noted - the widget then starts on it - and the rest of the way is
    /// explained.
    /// </summary>
    internal async void RequestWidgetForBoard(BoardViewModel board)
    {
        WidgetPreferredBoard.Set(preferences, board.Id);

        // The wish is part of what the widget reads, so it has to be written now rather than on the
        // next visit: whoever stands on the home screen with the menu just closed cannot wait.
        await widget.RefreshAsync();

        await dialogService.DisplayAlertAsync(
            Strings.WidgetForBoard,
            string.Format(CultureInfo.CurrentCulture, Strings.WidgetForBoardHelpFormat, board.Title),
            Strings.Done);
    }
}
