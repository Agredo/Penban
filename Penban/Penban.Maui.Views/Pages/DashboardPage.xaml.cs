using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using Penban.Maui.Views.Controls;
using Penban.Maui.Views.Services;
using Penban.Services.Abstractions;
using Penban.ViewModels;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// The dashboard: a look at what was worked on lately, a row of projects that is scrolled sideways and
/// a list of boards under it. Both blocks are a way in rather than a place to work - a tap opens the
/// project or the board, and the heading over each block leads to the page that holds all of them.
/// </summary>
public partial class DashboardPage : ContentPage
{
    private readonly BoardPageFactory boardPageFactory;
    private readonly INavigationService navigation;

    /// <summary>
    /// Whether a board is on its way to being opened. A board is opened by pushing its page, and a
    /// second tap before that push is through would push it twice.
    /// </summary>
    private bool isOpeningBoard;

    /// <summary>
    /// Which of the two board card layouts the list carries, or null while the page has not been
    /// measured yet.
    /// </summary>
    private bool? wideCards;

    public DashboardPage(DashboardViewModel viewModel, BoardPageFactory boardPageFactory, INavigationService navigation)
    {
        InitializeComponent();
        this.boardPageFactory = boardPageFactory;
        this.navigation = navigation;
        BindingContext = viewModel;
    }

    /// <summary>
    /// Gives the board list the card layout that fits the page, the way the board overview does it and
    /// at the same width, so the two pages do not draw different cards on the same screen. A card
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
        BindableLayout.SetItemTemplate(BoardsList, (DataTemplate)Resources[key]);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is not DashboardViewModel viewModel)
        {
            return;
        }

        // Read again on every visit rather than kept: the dashboard is what the app opens on, and a
        // board that was worked on elsewhere is exactly what it is meant to show.
        await viewModel.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Opens the tapped project's own page, which is pushed on top of the dashboard: the way back from
    /// it leads here, to the look at what was worked on.
    /// </summary>
    private async void OnProjectTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element { BindingContext: ProjectViewModel project })
        {
            return;
        }

        await navigation.ShellNavigationTo(AppRoutes.Project, new Dictionary<string, object>
        {
            [QueryParameters.ProjectId] = project.Id,
        });
    }

    /// <summary>
    /// Opens one board of the dashboard by pushing its page onto the plain navigation stack, the way
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
    /// Leads to the page that holds every project. The dashboard shows a few of them, and a block that
    /// is showing less than there is says where the rest is.
    /// </summary>
    private async void OnAllProjectsClicked(object? sender, EventArgs e)
    {
        await navigation.ShellNavigationTo($"//{AppRoutes.Projects}");
    }

    /// <summary>Leads to the page that holds every board.</summary>
    private async void OnAllBoardsClicked(object? sender, EventArgs e)
    {
        await navigation.ShellNavigationTo($"//{AppRoutes.Boards}");
    }

    /// <summary>Opens the menu, which is the only way to the other two areas.</summary>
    private void OnMenuClicked(object? sender, EventArgs e) => ShellMenu.Open();

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await navigation.ShellNavigationTo(AppRoutes.Settings);
    }
}
