using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using Penban.Maui.Views.Controls;
using Penban.Maui.Views.Services;
using Penban.Services.Abstractions;
using Penban.Util;
using Penban.ViewModels;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// One project's own page: what the project is, and the boards that hang under it. The boards are
/// worked with here - opened, created, edited, deleted - while the project itself is edited on the
/// details page the header opens.
/// </summary>
public partial class ProjectPage : ContentPage, Microsoft.Maui.Controls.IQueryAttributable
{
    private readonly SettingsViewModel settingsViewModel;
    private readonly FeedbackViewModel feedbackViewModel;
    private readonly IPreferences preferences;
    private readonly BoardPageFactory boardPageFactory;
    private readonly INavigationService navigation;

    /// <summary>
    /// The project this page is about. Kept here as well as in the view model, because the floating
    /// button hands it on to the board details page and the view model is not the half that
    /// navigates.
    /// </summary>
    private Guid? projectId;

    private bool isOpeningBoard;

    private bool? wideCards;

    public ProjectPage(ProjectPageViewModel viewModel, SettingsViewModel settingsViewModel, FeedbackViewModel feedbackViewModel, IPreferences preferences, BoardPageFactory boardPageFactory, TransferCoordinator transferCoordinator, INavigationService navigation)
    {
        InitializeComponent();
        this.settingsViewModel = settingsViewModel;
        this.feedbackViewModel = feedbackViewModel;
        this.preferences = preferences;
        this.boardPageFactory = boardPageFactory;
        this.navigation = navigation;
        BindingContext = viewModel;
        BoardActions = new BoardCardActions(transferCoordinator, navigation, viewModel.DeleteBoardCommand);
    }

    /// <summary>
    /// What the buttons on a board card do, the same three the board overview hands its cards. The
    /// delete is this page's own, so a board thrown away from here leaves this project's list.
    /// </summary>
    public BoardCardActions BoardActions { get; }

    /// <summary>
    /// Gives the list the card layout that fits the page, the way the board overview does. A card
    /// cannot rearrange itself - changing the layout of a live grid in the list takes WinUI down - so
    /// the list swaps templates instead, which only happens where the page crosses the width.
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0)
        {
            return;
        }

        var wide = width >= BoardCard.WidePageWidth;
        if (wide == wideCards)
        {
            return;
        }

        wideCards = wide;
        var key = wide ? "WideBoardCard" : "NarrowBoardCard";
        ProjectBoardsList.ItemTemplate = (DataTemplate)Resources[key];
    }

    /// <summary>
    /// The route's parameters. Read here and handed on to the view model, which is the half of the
    /// page that has to stay free of the framework and therefore cannot implement the shell's own
    /// interface itself.
    /// </summary>
    void Microsoft.Maui.Controls.IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // Coming back to this page hands in nothing. That is not a different project, so it must not
        // forget the one on screen - the floating button would lose it.
        if (query.TryGetValue(QueryParameters.ProjectId, out var value) && value is not null)
        {
            projectId = value switch
            {
                Guid id => id,
                string text when Guid.TryParse(text, out var parsed) => parsed,
                _ => projectId,
            };
        }

        if (BindingContext is ProjectPageViewModel viewModel)
        {
            viewModel.ApplyQueryAttributes(query);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is not ProjectPageViewModel viewModel)
        {
            return;
        }

        await viewModel.LoadCommand.ExecuteAsync(null);

        // Whether the project carries a note is only known once it has been read.
        UpdateNoteButton();
    }

    /// <summary>
    /// Lights the note button while the project carries a note, the way the board page does it, so
    /// the header says whether there is one without the button having to be pressed.
    /// </summary>
    private void UpdateNoteButton()
    {
        var hasNote = BindingContext is ProjectPageViewModel { HasNote: true };
        ProjectNoteButton.Style = (Style)Application.Current!.Resources[hasNote ? "AccentIconButton" : "GhostIconButton"];
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await navigation.NavigateBack();
    }

    /// <summary>
    /// Writes the project's own note, in the same ink editor a board's own note is written in. What
    /// comes back is put on the card, which is why the note button is lit again afterwards.
    /// </summary>
    private async void OnProjectNoteClicked(object? sender, EventArgs e)
    {
        if (BindingContext is not ProjectPageViewModel viewModel || viewModel.CreateNoteEditor() is not { } target)
        {
            return;
        }

        await Navigation.PushAsync(new CardInkEditorPage(target, preferences, settingsViewModel, feedbackViewModel, navigation, viewModel.ProjectCard?.Title ?? string.Empty));

        await viewModel.LoadCommand.ExecuteAsync(null);
        UpdateNoteButton();
    }

    /// <summary>
    /// Opens one board of this project by pushing its page onto the plain navigation stack, the way
    /// the board overview does it. Shell route navigation (GoToAsync) throws "Pending Navigations
    /// still processing" on Windows when it is mixed with a plain push, and a plain push pops cleanly
    /// again.
    /// </summary>
    private async void OnBoardTapped(object? sender, TappedEventArgs e)
    {
        if (isOpeningBoard || sender is not Element { BindingContext: BoardViewModel board })
        {
            return;
        }

        isOpeningBoard = true;
        try
        {
            await Navigation.PushAsync(boardPageFactory.Create(board));
        }
        finally
        {
            isOpeningBoard = false;
        }
    }

    /// <summary>
    /// Opens the details page on a new board, already belonging to this project. The project stays
    /// changeable there - creating a board from a project page is a shortcut, not a rule.
    /// </summary>
    private async void OnAddBoardClicked(object? sender, EventArgs e)
    {
        if (projectId is not { } id)
        {
            await navigation.ShellNavigationTo(AppRoutes.BoardDetails);
            return;
        }

        await navigation.ShellNavigationTo(AppRoutes.BoardDetails, new Dictionary<string, object>
        {
            [QueryParameters.ProjectId] = id,
        });
    }

    private async void OnEditProjectClicked(object? sender, EventArgs e)
    {
        if (projectId is not { } id)
        {
            return;
        }

        await navigation.ShellNavigationTo(AppRoutes.ProjectDetails, new Dictionary<string, object>
        {
            [QueryParameters.ProjectId] = id,
        });
    }
}
