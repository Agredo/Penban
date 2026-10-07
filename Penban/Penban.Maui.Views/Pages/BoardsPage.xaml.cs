using AgredoApplication.MVVM.Services.Abstractions.Navigation;
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
    private readonly SettingsViewModel settingsViewModel;
    private readonly SearchViewModel searchViewModel;
    private readonly FeedbackViewModel feedbackViewModel;
    private readonly TransferCoordinator transferCoordinator;
    private readonly IDialogService dialogService;
    private readonly WidgetSnapshotTrigger widget;
    private readonly WidgetBoardLink boardLink;
    private readonly INavigationService navigation;
    private bool isOpeningBoard;

    /// <summary>
    /// The board that was opened last, so that the row it stands for can be brought into view when
    /// the overview comes back. A board opened from the home screen widget is opened without the list
    /// ever having been scrolled to it, and the reader should see where they were.
    /// </summary>
    private Guid? lastOpenedBoardId;

    public BoardsPage(BoardsViewModel viewModel, SettingsViewModel settingsViewModel, SearchViewModel searchViewModel, FeedbackViewModel feedbackViewModel, IPreferences preferences, TransferCoordinator transferCoordinator, IDialogService dialogService, WidgetSnapshotTrigger widget, WidgetBoardLink boardLink, INavigationService navigation)
    {
        InitializeComponent();
        this.settingsViewModel = settingsViewModel;
        this.searchViewModel = searchViewModel;
        this.feedbackViewModel = feedbackViewModel;
        this.preferences = preferences;
        this.transferCoordinator = transferCoordinator;
        this.dialogService = dialogService;
        this.widget = widget;
        this.boardLink = boardLink;
        this.navigation = navigation;
        BindingContext = viewModel;
    }

    /// <summary>Page width from which a board card keeps its thumbnail beside its text.</summary>
    private const double WideCardPageWidth = 700;

    private bool? wideCards;

    /// <summary>
    /// Gives the list the card layout that fits the page. A card cannot rearrange itself - changing
    /// the layout of a live grid in the list takes WinUI down - so the list swaps templates instead,
    /// which only happens where the page crosses the width, not on every resize.
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0)
        {
            return;
        }

        var wide = width >= WideCardPageWidth;
        if (wide == wideCards)
        {
            return;
        }

        wideCards = wide;
        var key = wide ? "WideBoardCard" : "NarrowBoardCard";
        BoardsList.ItemTemplate = (DataTemplate)Resources[key];
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

        // The rows were just read, so the copy is written from them rather than from a second pass
        // over every board - which would take as long as the load itself did.
        await widget.RefreshAsync(rowsAreFresh: true);

        // Back from a board: the list keeps the place it was left at (see BoardsViewModel), and a row
        // that is out of sight - the widget opens a board without the list ever having been scrolled
        // to it - is brought into view. A row that is already on screen is left alone.
        ScrollToLastOpenedBoard(viewModel);

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
    /// The board that is in the air. Kept because the two halves of the gesture are told different
    /// things: the drag knows what was picked up and the drop only knows the row it landed on, and
    /// the row is where the dragged board has to be put.
    /// </summary>
    private BoardViewModel? draggedBoard;

    /// <summary>
    /// A board card was picked up to be put somewhere else. The package is given something to carry
    /// so the platform accepts the drag at all; what it carries is not read back, since the board
    /// itself is already known here.
    /// </summary>
    private void OnBoardDragStarting(object? sender, DragStartingEventArgs e)
    {
        draggedBoard = (sender as Element)?.BindingContext as BoardViewModel;

        if (draggedBoard is not null)
        {
            e.Data.Text = draggedBoard.Id.ToString();
        }
    }

    /// <summary>
    /// The drag is over, wherever it ended: a board that was let go over nothing must not be dropped
    /// onto the next row that is touched.
    /// </summary>
    private void OnBoardDropCompleted(object? sender, DropCompletedEventArgs e)
    {
        draggedBoard = null;
    }

    /// <summary>
    /// A board was dropped onto another one: the dragged board takes the place of the row it was
    /// dropped on and everything else shifts along, which is the order the overview then stores.
    /// </summary>
    private async void OnBoardDrop(object? sender, DropEventArgs e)
    {
        var dragged = draggedBoard;
        draggedBoard = null;

        if (dragged is null || BindingContext is not BoardsViewModel viewModel)
        {
            return;
        }

        if ((sender as Element)?.BindingContext is not BoardViewModel target || target.Id == dragged.Id)
        {
            return;
        }

        await viewModel.MoveBoardAsync(dragged.Id, viewModel.Boards.IndexOf(target));
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

            lastOpenedBoardId = boardId;
            await Navigation.PushAsync(new BoardPage(board, settingsViewModel, feedbackViewModel, preferences, transferCoordinator, dialogService, navigation, searchViewModel, openCardId));
        }
        finally
        {
            isOpeningBoard = false;
        }
    }

    /// <summary>
    /// Brings the row of the board that was closed last into view, unless it is already there. Read
    /// after the rows have been loaded, when the list holds the items it is going to show.
    /// </summary>
    private void ScrollToLastOpenedBoard(BoardsViewModel viewModel)
    {
        if (lastOpenedBoardId is not { } boardId)
        {
            return;
        }

        lastOpenedBoardId = null;

        // A board that was deleted in the meantime has no row to go to.
        if (viewModel.FindBoard(boardId) is not { } board)
        {
            return;
        }

        // Handed to the dispatcher: the list has just been given its rows and does not know their
        // places yet, and a list that is still being built would drop the jump.
        Dispatcher.Dispatch(() => BoardsList.ScrollTo(board, position: ScrollToPosition.MakeVisible, animate: false));
    }

    private static async Task<BoardViewModel?> LoadBoardAsync(BoardsViewModel viewModel, Guid boardId)
    {
        await viewModel.LoadBoardsCommand.ExecuteAsync(null);
        return viewModel.FindBoard(boardId);
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await navigation.ShellNavigationTo(AppRoutes.Settings);
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
}
