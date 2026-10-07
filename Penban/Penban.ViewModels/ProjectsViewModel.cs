// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// Projects overview: lists the projects with their labels, narrows them down by label, removes them
/// and keeps the order a project was dragged to. Creating one and changing one both belong to the
/// project's details page, so coming back from it reads the list again rather than patching a row.
/// </summary>
public partial class ProjectsViewModel : ObservableObject
{
    private readonly IProjectService projectService;
    private readonly IBoardService boardService;
    private readonly IDialogService dialogService;

    /// <summary>
    /// Every project as it was read, whether the filter row shows it or not. The rows live here
    /// rather than in <see cref="Projects"/> because the filter is applied to this list and the list
    /// on screen is only ever a view of it - so turning a chip off brings its projects back in the
    /// order they were in.
    /// </summary>
    private readonly List<ProjectViewModel> allProjects = new();

    public ProjectsViewModel(IProjectService projectService, IBoardService boardService, IDialogService dialogService)
    {
        this.projectService = projectService;
        this.boardService = boardService;
        this.dialogService = dialogService;
    }

    /// <summary>The rows on screen: everything that was read, or only what the chosen labels select.</summary>
    public ObservableCollection<ProjectViewModel> Projects { get; } = new();

    /// <summary>The labels offered above the list, one chip each.</summary>
    public ObservableCollection<TagFilterViewModel> TagFilters { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    partial void OnIsLoadingChanged(bool value) => NotifyRowsChanged();

    /// <summary>Whether the filter row belongs on screen: without labels there is nothing to narrow down.</summary>
    public bool ShowFilterRow => TagFilters.Count > 0;

    /// <summary>Whether a label is currently narrowing the list down.</summary>
    public bool HasActiveFilters => TagFilters.Any(filter => filter.IsSelected);

    /// <summary>Whether the "nothing here" text belongs on screen: nothing there and nothing coming.</summary>
    public bool ShowEmptyState => !IsLoading && Projects.Count == 0;

    /// <summary>
    /// What the empty state says. A list that is empty because of the filter row says so, so that a
    /// project that is merely filtered away is not read as a project that is gone.
    /// </summary>
    public string EmptyStateText => HasActiveFilters ? Strings.NoProjectsMatchFilter : Strings.NoProjectsYet;

    /// <summary>
    /// Reads the list from the database. The wait is carried by the command around it rather than by
    /// the reading itself, so that no way into this method can leave the spinner running.
    /// </summary>
    [RelayCommand]
    private async Task LoadProjectsAsync()
    {
        IsLoading = true;
        try
        {
            await ReadProjectsAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ReadProjectsAsync()
    {
        var projects = await projectService.GetProjectsAsync();
        var boards = await boardService.GetBoardsAsync();

        // Counted here rather than asked per project: the boards are one read, and the card needs
        // the latest write of a project's boards anyway. A board carries the work, so a project
        // whose board was just written to is not a project that has stood still.
        var byProject = new Dictionary<Guid, (int Count, DateTimeOffset LastEdited)>();
        foreach (var board in boards)
        {
            if (board.ProjectId is not { } projectId)
            {
                continue;
            }

            var current = byProject.TryGetValue(projectId, out var found) ? found : (Count: 0, LastEdited: DateTimeOffset.MinValue);
            byProject[projectId] = (
                current.Count + 1,
                board.UpdatedAtUtc > current.LastEdited ? board.UpdatedAtUtc : current.LastEdited);
        }

        // A row that is already there is brought up to date rather than built again, see RowList.
        var loaded = new List<ProjectViewModel>(projects.Count);
        foreach (var project in projects)
        {
            var (count, boardEdited) = byProject.TryGetValue(project.Id, out var found) ? found : (0, DateTimeOffset.MinValue);
            var lastEdited = boardEdited > project.UpdatedAtUtc ? boardEdited : project.UpdatedAtUtc;

            var existing = allProjects.FirstOrDefault(row => row.Id == project.Id);
            if (existing is null)
            {
                loaded.Add(new ProjectViewModel(project, count, lastEdited));
            }
            else
            {
                existing.Refresh(project, count, lastEdited);
                loaded.Add(existing);
            }
        }

        loaded.Sort(CompareRows);

        allProjects.Clear();
        allProjects.AddRange(loaded);

        RebuildTagFilters();
        ApplyFilter();
    }

    private static int CompareRows(ProjectViewModel left, ProjectViewModel right)
    {
        // The place a project was dragged to comes first, same rule as on the board overview, so a
        // project that was moved stays where it was put however long ago it was written to.
        var byPlace = left.SortOrder.CompareTo(right.SortOrder);
        if (byPlace != 0)
        {
            return byPlace;
        }

        var byDate = right.LastEditedUtc.CompareTo(left.LastEditedUtc);
        return byDate != 0
            ? byDate
            : string.Compare(left.Title, right.Title, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Rebuilds the chips from the labels the projects actually carry, keeping the ones that are
    /// chosen chosen: a label that is still in use keeps its chip, and a chip whose last project
    /// dropped the label goes away rather than filtering the list down to nothing.
    /// </summary>
    private void RebuildTagFilters()
    {
        var chosen = TagFilters.Where(filter => filter.IsSelected)
                               .Select(filter => filter.Tag)
                               .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var tags = allProjects.SelectMany(project => project.Tags)
                              .Distinct(StringComparer.CurrentCultureIgnoreCase)
                              .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
                              .ToList();

        TagFilters.Clear();
        foreach (var tag in tags)
        {
            TagFilters.Add(new TagFilterViewModel(tag) { IsSelected = chosen.Contains(tag) });
        }

        OnPropertyChanged(nameof(ShowFilterRow));
    }

    /// <summary>
    /// Puts the rows the filter lets through on screen, keeping the ones that are already there (see
    /// <see cref="RowList"/>). A label that is chosen narrows the list down, so a project has to
    /// carry every chosen label to stay on it.
    /// </summary>
    private void ApplyFilter()
    {
        var chosen = TagFilters.Where(filter => filter.IsSelected)
                               .Select(filter => filter.Tag)
                               .ToList();

        var visible = chosen.Count == 0
            ? allProjects
            : allProjects.Where(project => project.HasAllTags(chosen)).ToList();

        RowList.Merge(Projects, visible);
        NotifyRowsChanged();
    }

    /// <summary>
    /// Says that the rows have changed. The empty state and the filter row are read off the list
    /// rather than kept beside it, and the list raises nothing by itself, so it is said here.
    /// </summary>
    private void NotifyRowsChanged()
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    /// <summary>Narrows the list down to a label, or widens it again when the chip was already on.</summary>
    [RelayCommand]
    private void ToggleTag(TagFilterViewModel? filter)
    {
        if (filter is null)
        {
            return;
        }

        filter.IsSelected = !filter.IsSelected;
        ApplyFilter();
    }

    /// <summary>Drops every chosen label, which is the whole list again.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        if (!HasActiveFilters)
        {
            return;
        }

        foreach (var filter in TagFilters)
        {
            filter.IsSelected = false;
        }

        ApplyFilter();
    }

    /// <summary>
    /// Deletes the project and leaves its boards standing - they lose the project and nothing else,
    /// which is what <c>IProjectService.DeleteProjectAsync</c> does to them.
    /// </summary>
    [RelayCommand]
    private async Task DeleteProjectAsync(Guid projectId)
    {
        if (allProjects.All(project => project.Id != projectId))
        {
            return;
        }

        var confirmed = await dialogService.DisplayConfirmationAsync(Strings.Delete, Strings.DeleteProjectConfirmation, Strings.Delete, Strings.Cancel);
        if (!confirmed)
        {
            return;
        }

        await projectService.DeleteProjectAsync(projectId);

        // Taken out here rather than read again: deleting a project touches the boards under it and
        // no other project, and a reload would send the reader back to the top of the list.
        allProjects.RemoveAll(project => project.Id == projectId);
        RebuildTagFilters();
        ApplyFilter();
    }

    /// <summary>
    /// Puts the dragged project where it was dropped and stores the new order, so the next visit to
    /// the overview shows the same list. Moved in the full list rather than in the one on screen:
    /// with a filter on, the row it was dropped on is only one of the projects between the two.
    /// </summary>
    public async Task MoveProjectAsync(Guid projectId, Guid targetProjectId)
    {
        var source = allProjects.FindIndex(project => project.Id == projectId);
        var target = allProjects.FindIndex(project => project.Id == targetProjectId);
        if (source < 0 || target < 0 || source == target)
        {
            return;
        }

        var moved = allProjects[source];
        allProjects.RemoveAt(source);
        allProjects.Insert(target, moved);

        // The rows carry the new places right away, so the sort above agrees with the list until the
        // next load reads them back from the database.
        for (var index = 0; index < allProjects.Count; index++)
        {
            allProjects[index].SortOrder = index;
        }

        await projectService.ReorderProjectsAsync(allProjects.Select(project => project.Id).ToList());

        ApplyFilter();
    }
}
