// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// One board's details: the name it carries and, behind the extended mode, its labels, its time frame
/// and the project it belongs to. The same view model creates a board and edits one - which of the
/// two it is follows from the parameters it was handed, not from a second type.
/// </summary>
public partial class BoardDetailsViewModel : ObservableObject, IQueryAttributable
{
    private readonly IBoardService boardService;
    private readonly IProjectService projectService;

    /// <summary>The board being edited, or <c>null</c> while a new one is being created.</summary>
    private Guid? boardId;

    /// <summary>The project a new board was asked to be put into, from the project's own page.</summary>
    private Guid? initialProjectId;

    public BoardDetailsViewModel(IBoardService boardService, IProjectService projectService)
    {
        this.boardService = boardService;
        this.projectService = projectService;
    }

    /// <summary>
    /// What the project picker offers: every project, with the entry that means "no project" first.
    /// </summary>
    public ObservableCollection<ProjectOption> Projects { get; } = new();

    /// <summary>Whether an existing board is being edited rather than a new one created.</summary>
    public bool IsEditing => boardId is not null;

    public string PageTitle => IsEditing ? Strings.EditBoardTitle : Strings.NewBoardTitle;

    [ObservableProperty]
    private string title = string.Empty;

    /// <summary>The labels as they were typed, in one field rather than one field per label.</summary>
    [ObservableProperty]
    private string tagsText = string.Empty;

    /// <summary>
    /// Whether the board has a time frame at all. A board without one holds no dates rather than a
    /// pair of default ones, which is what keeps the field out of the way of the boards that never
    /// wanted it.
    /// </summary>
    [ObservableProperty]
    private bool hasPeriod;

    [ObservableProperty]
    private DateTime startDate = DateTime.Today;

    [ObservableProperty]
    private DateTime endDate = DateTime.Today;

    [ObservableProperty]
    private ProjectOption? selectedProject;

    /// <summary>Whether the extended mode is unfolded.</summary>
    [ObservableProperty]
    private bool isExpanded;

    /// <summary>Whether there is enough on the board to store it: a name is the one thing it needs.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Title);

    /// <summary>
    /// What the folded extended mode says about what is inside it, so that a board whose labels or
    /// time frame are hidden behind it does not look like a board that has none.
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

            if (SelectedProject is { Id: not null } project)
            {
                parts.Add(project.Title);
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
        var id = ReadGuid(query, QueryParameters.BoardId);
        var projectId = ReadGuid(query, QueryParameters.ProjectId);

        // Coming back to this page - from a picker on top of it, say - hands in nothing. That is not a
        // different board, so it must not clear the one being edited.
        if (id is null && projectId is null)
        {
            return;
        }

        boardId = id;
        initialProjectId = projectId;

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(PageTitle));
    }

    /// <summary>
    /// Fills the fields: from the board being edited, or from the parameters a new board was opened
    /// with. Read on every visit rather than in the constructor, so that the page has already been
    /// handed its parameters when it happens.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        Projects.Clear();
        Projects.Add(new ProjectOption(null, Strings.FieldNoProject));

        foreach (var project in await projectService.GetProjectsAsync())
        {
            Projects.Add(new ProjectOption(project.Id, project.Title));
        }

        if (boardId is { } id)
        {
            await ReadBoardAsync(id);
            return;
        }

        // A board that is created out of a project opens with the extended mode unfolded: the project
        // it is being put into is the one thing on it that the reader did not type.
        SelectedProject = Projects.FirstOrDefault(option => option.Id == initialProjectId) ?? Projects[0];
        IsExpanded = initialProjectId is not null;
    }

    private async Task ReadBoardAsync(Guid id)
    {
        var board = (await boardService.GetBoardsAsync()).FirstOrDefault(candidate => candidate.Id == id);
        if (board is null)
        {
            return;
        }

        Title = board.Title;
        TagsText = string.Join(", ", board.Tags);

        HasPeriod = board.StartDate is not null || board.EndDate is not null;

        // One end of a half-open time frame is what the other one falls back to, so that setting only
        // the end does not leave the start sitting on today.
        var start = board.StartDate?.LocalDateTime.Date;
        var end = board.EndDate?.LocalDateTime.Date;
        StartDate = start ?? end ?? DateTime.Today;
        EndDate = end ?? start ?? DateTime.Today;

        // A board whose project is gone reads as a board without one, which is what the database holds
        // for it after the project was deleted.
        SelectedProject = Projects.FirstOrDefault(option => option.Id == board.ProjectId) ?? Projects[0];
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

        var details = new BoardDetails
        {
            Title = title,
            ProjectId = SelectedProject?.Id,
            Tags = TagList.Parse(TagsText),
            StartDate = HasPeriod ? new DateTimeOffset(StartDate.Date) : null,
            EndDate = HasPeriod ? new DateTimeOffset(EndDate.Date) : null,
        };

        if (boardId is { } id)
        {
            await boardService.SaveBoardDetailsAsync(id, details);
            return;
        }

        await boardService.CreateBoardAsync(details);
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

    partial void OnSelectedProjectChanged(ProjectOption? value) => OnPropertyChanged(nameof(AdvancedSummary));
}
