// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// One project's details: the name it carries and, behind the extended mode, its labels and its time
/// frame. The same view model creates a project and edits one - which of the two it is follows from
/// the parameters it was handed, not from a second type.
/// </summary>
public partial class ProjectDetailsViewModel : ObservableObject, IQueryAttributable
{
    private readonly IProjectService projectService;

    /// <summary>The project being edited, or <c>null</c> while a new one is being created.</summary>
    private Guid? projectId;

    public ProjectDetailsViewModel(IProjectService projectService)
    {
        this.projectService = projectService;
    }

    /// <summary>Whether an existing project is being edited rather than a new one created.</summary>
    public bool IsEditing => projectId is not null;

    public string PageTitle => IsEditing ? Strings.EditProjectTitle : Strings.NewProjectTitle;

    [ObservableProperty]
    private string title = string.Empty;

    /// <summary>The labels as they were typed, in one field rather than one field per label.</summary>
    [ObservableProperty]
    private string tagsText = string.Empty;

    /// <summary>
    /// Whether the project has a time frame at all. A project without one holds no dates rather than
    /// a pair of default ones, which is what keeps the field out of the way of the projects that
    /// never wanted it.
    /// </summary>
    [ObservableProperty]
    private bool hasPeriod;

    [ObservableProperty]
    private DateTime startDate = DateTime.Today;

    [ObservableProperty]
    private DateTime endDate = DateTime.Today;

    /// <summary>Whether the extended mode is unfolded.</summary>
    [ObservableProperty]
    private bool isExpanded;

    /// <summary>Whether there is enough on the project to store it: a name is the one thing it needs.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Title);

    /// <summary>
    /// What the folded extended mode says about what is inside it, so that a project whose labels or
    /// time frame are hidden behind it does not look like a project that has none.
    /// </summary>
    public string AdvancedSummary
    {
        get
        {
            var parts = new List<string>();

            if (TagList.Parse(TagsText) is { Count: > 0 } tags)
            {
                parts.Add(string.Join(", ", tags));
            }

            if (HasPeriod)
            {
                parts.Add($"{StartDate:d} – {EndDate:d}");
            }

            return parts.Count == 0 ? Strings.AdvancedSummaryNone : string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Reads the route's parameters. Handed on by the page: the shell only knows pages, and this view
    /// model is the part of that page which is not allowed to know the shell.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var id = ReadGuid(query, QueryParameters.ProjectId);

        // Coming back to this page hands in nothing. That is not a different project, so it must not
        // clear the one being edited.
        if (id is null)
        {
            return;
        }

        projectId = id;

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(PageTitle));
    }

    /// <summary>
    /// Fills the fields from the project being edited. Read on every visit rather than in the
    /// constructor, so that the page has already been handed its parameters when it happens.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (projectId is not { } id)
        {
            return;
        }

        var project = (await projectService.GetProjectsAsync()).FirstOrDefault(candidate => candidate.Id == id);
        if (project is null)
        {
            return;
        }

        Title = project.Title;
        TagsText = string.Join(", ", project.Tags);

        HasPeriod = project.StartDate is not null || project.EndDate is not null;

        // One end of a half-open time frame is what the other one falls back to, so that setting only
        // the end does not leave the start sitting on today.
        var start = project.StartDate?.LocalDateTime.Date;
        var end = project.EndDate?.LocalDateTime.Date;
        StartDate = start ?? end ?? DateTime.Today;
        EndDate = end ?? start ?? DateTime.Today;
    }

    /// <summary>
    /// Stores the fields. Called by the page, which is also what closes itself once this went through,
    /// because the way back is the page's business rather than the view model's.
    /// </summary>
    public async Task SaveAsync()
    {
        var title = Title.Trim();
        if (title.Length == 0)
        {
            return;
        }

        var details = new ProjectDetails
        {
            Title = title,
            Tags = TagList.Parse(TagsText),
            StartDate = HasPeriod ? new DateTimeOffset(StartDate.Date) : null,
            EndDate = HasPeriod ? new DateTimeOffset(EndDate.Date) : null,
        };

        if (projectId is { } id)
        {
            await projectService.SaveProjectDetailsAsync(id, details);
            return;
        }

        await projectService.CreateProjectAsync(details);
    }

    /// <summary>
    /// A parameter that names something. Read from both shapes it can arrive in: the page that
    /// navigates hands in the id itself, while a route written by hand - or by a future deep link -
    /// carries it as text.
    /// </summary>
    private static Guid? ReadGuid(IDictionary<string, object> query, string name)
        => query.TryGetValue(name, out var value) && value is not null
            ? value switch
            {
                Guid id => id,
                string text when Guid.TryParse(text, out var parsed) => parsed,
                _ => null,
            }
            : null;

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(CanSave));

    partial void OnTagsTextChanged(string value) => OnPropertyChanged(nameof(AdvancedSummary));

    partial void OnHasPeriodChanged(bool value) => OnPropertyChanged(nameof(AdvancedSummary));

    partial void OnStartDateChanged(DateTime value) => OnPropertyChanged(nameof(AdvancedSummary));

    partial void OnEndDateChanged(DateTime value) => OnPropertyChanged(nameof(AdvancedSummary));
}
