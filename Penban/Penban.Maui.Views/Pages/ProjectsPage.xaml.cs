using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using Penban.Maui.Views.Services;
using Penban.Services.Abstractions;
using Penban.ViewModels;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Projects overview page: lists the projects, narrows them down by label, and opens, creates,
/// edits and deletes them. The work of one project happens on its own page, which a tap opens.
/// </summary>
public partial class ProjectsPage : ContentPage
{
    private readonly INavigationService navigation;
    private readonly TransferCoordinator transferCoordinator;

    public ProjectsPage(ProjectsViewModel viewModel, INavigationService navigation, TransferCoordinator transferCoordinator)
    {
        InitializeComponent();
        this.navigation = navigation;
        this.transferCoordinator = transferCoordinator;
        BindingContext = viewModel;
    }

    /// <summary>Page width from which a project card keeps its thumbnail beside its text.</summary>
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
        var key = wide ? "WideProjectCard" : "NarrowProjectCard";
        ProjectsList.ItemTemplate = (DataTemplate)Resources[key];
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is not ProjectsViewModel viewModel)
        {
            return;
        }

        // Read again on every visit rather than kept: creating and editing a project both happen on
        // the details page, and coming back from it is the only moment this page learns about them.
        await viewModel.LoadProjectsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Opens the tapped project's own page, where its boards are and where its note is written. The
    /// board itself is opened from there, so nothing is pushed on the stack from here.
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
    /// The project that is in the air. Kept because the two halves of the gesture are told different
    /// things: the drag knows what was picked up and the drop only knows the row it landed on, and
    /// the row is where the dragged project has to be put.
    /// </summary>
    private ProjectViewModel? draggedProject;

    /// <summary>
    /// A project card was picked up to be put somewhere else. The package is given something to carry
    /// so the platform accepts the drag at all; what it carries is not read back, since the project
    /// itself is already known here.
    /// </summary>
    private void OnProjectDragStarting(object? sender, DragStartingEventArgs e)
    {
        draggedProject = (sender as Element)?.BindingContext as ProjectViewModel;

        if (draggedProject is not null)
        {
            e.Data.Text = draggedProject.Id.ToString();
        }
    }

    /// <summary>
    /// The drag is over, wherever it ended: a project that was let go over nothing must not be dropped
    /// onto the next row that is touched.
    /// </summary>
    private void OnProjectDropCompleted(object? sender, DropCompletedEventArgs e)
    {
        draggedProject = null;
    }

    /// <summary>
    /// A project was dropped onto another one: the dragged project takes the place of the row it was
    /// dropped on and everything else shifts along, which is the order the overview then stores.
    /// </summary>
    private async void OnProjectDrop(object? sender, DropEventArgs e)
    {
        var dragged = draggedProject;
        draggedProject = null;

        if (dragged is null || BindingContext is not ProjectsViewModel viewModel)
        {
            return;
        }

        if ((sender as Element)?.BindingContext is not ProjectViewModel target || target.Id == dragged.Id)
        {
            return;
        }

        await viewModel.MoveProjectAsync(dragged.Id, target.Id);
    }

    /// <summary>Opens the menu, which is the only way to the other two areas.</summary>
    private void OnMenuClicked(object? sender, EventArgs e) => ShellMenu.Open();

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await navigation.ShellNavigationTo(AppRoutes.Settings);
    }

    /// <summary>
    /// The whole-database export and import, the same menu the board overview offers. A backup
    /// carries the projects as well, so an import can leave the list on this page different from how
    /// it was - which is the one case it is read again for.
    /// </summary>
    private async void OnTransferClicked(object? sender, EventArgs e)
    {
        if (await transferCoordinator.ShowDatabaseMenuAsync() && BindingContext is ProjectsViewModel viewModel)
        {
            await viewModel.LoadProjectsCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Opens the details page on a new project. The project is created there rather than here, so
    /// that its name and the fields behind the extended mode are written in one place.
    /// </summary>
    private async void OnAddProjectClicked(object? sender, EventArgs e)
    {
        await navigation.ShellNavigationTo(AppRoutes.ProjectDetails);
    }

    /// <summary>
    /// Opens the details page on an existing project. The card is read again when the overview comes
    /// back, which is what puts an edited name, new labels or a new time frame on it.
    /// </summary>
    private async void OnEditProjectClicked(object? sender, EventArgs e)
    {
        if (sender is not Element { BindingContext: ProjectViewModel project })
        {
            return;
        }

        await navigation.ShellNavigationTo(AppRoutes.ProjectDetails, new Dictionary<string, object>
        {
            [QueryParameters.ProjectId] = project.Id,
        });
    }
}
