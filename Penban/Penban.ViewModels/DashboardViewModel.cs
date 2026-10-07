// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Services.Abstractions;

namespace Penban.ViewModels;

/// <summary>
/// The dashboard: a short look at what was worked on lately. The projects stand in one row that is
/// scrolled sideways, the boards under them in a list that is scrolled down - each block with a way
/// to the page that holds all of them.
/// <para>
/// Both blocks show what was written to last rather than what stands first: the dashboard answers
/// "where was I" rather than "what is there".
/// </para>
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    /// <summary>How many projects the row shows.</summary>
    private const int ProjectCount = 3;

    /// <summary>How many boards the list shows.</summary>
    private const int BoardCount = 3;

    private readonly IProjectService projectService;
    private readonly IBoardService boardService;
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;

    /// <summary>
    /// Every board as it was read, whether it is among the most recent ones or not. The rows live
    /// here so that one that is already on screen is brought up to date instead of built again
    /// (see <see cref="RowList"/>).
    /// </summary>
    private readonly List<BoardViewModel> allBoards = new();

    public DashboardViewModel(IProjectService projectService, IBoardService boardService, ICardService cardService, IDialogService dialogService)
    {
        this.projectService = projectService;
        this.boardService = boardService;
        this.cardService = cardService;
        this.dialogService = dialogService;
    }

    /// <summary>The most recently worked on projects, newest first. Scrolled sideways.</summary>
    public ObservableCollection<ProjectViewModel> Projects { get; } = new();

    /// <summary>The most recently worked on boards, newest first. Scrolled down.</summary>
    public ObservableCollection<BoardViewModel> Boards { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    partial void OnIsLoadingChanged(bool value) => NotifyRowsChanged();

    /// <summary>Whether the projects block belongs on screen.</summary>
    public bool HasProjects => Projects.Count > 0;

    /// <summary>Whether the boards block belongs on screen.</summary>
    public bool HasBoards => Boards.Count > 0;

    /// <summary>
    /// Whether the "nothing here" text belongs on screen: nothing there and nothing coming. A
    /// dashboard that is still filling must not claim the app is empty.
    /// </summary>
    public bool ShowEmptyState => !IsLoading && Projects.Count == 0 && Boards.Count == 0;

    /// <summary>
    /// Reads both blocks. The wait is carried by the command around it rather than by the reading
    /// itself, so that no way into this method can leave the spinner running.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var projects = await projectService.GetProjectsAsync();
            var boards = await boardService.GetBoardsAsync();

            ReadProjects(projects, ProjectStats.CountByProject(boards));
            await ReadBoardsAsync(boards);

            NotifyRowsChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// The newest projects. Built from the same row the projects list builds, so the two cannot show
    /// a project differently, and then cut off after the first few.
    /// </summary>
    private void ReadProjects(IReadOnlyList<Project> projects, Dictionary<Guid, (int Count, DateTimeOffset LastEdited)> byProject)
    {
        var recent = projects
            .Select(project =>
            {
                var (count, boardEdited) = byProject.TryGetValue(project.Id, out var found) ? found : (0, DateTimeOffset.MinValue);
                return new ProjectViewModel(project, count, boardEdited > project.UpdatedAtUtc ? boardEdited : project.UpdatedAtUtc);
            })
            .OrderByDescending(row => row.LastEditedUtc)
            .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(ProjectCount)
            .ToList();

        // Built again rather than brought up to date: this row is short enough that the whole of it
        // can be replaced in one go, and it holds no place a reader could have scrolled to.
        Projects.Clear();
        foreach (var row in recent)
        {
            Projects.Add(row);
        }
    }

    /// <summary>
    /// The newest boards, read with their summaries and their load bars. A row that is already there
    /// is brought up to date rather than built again, so that the place the reader scrolled to
    /// survives a visit to one of the boards.
    /// <para>
    /// Only the few that are shown are read: a summary is read off every column of a board, so
    /// reading all of them to put three on screen would cost what the whole overview costs - which is
    /// what the page the board block leads to is for.
    /// </para>
    /// </summary>
    private async Task ReadBoardsAsync(IReadOnlyList<Board> boards)
    {
        var recent = boards.OrderByDescending(board => board.UpdatedAtUtc)
                           .ThenBy(board => board.Title, StringComparer.CurrentCultureIgnoreCase)
                           .Take(BoardCount)
                           .ToList();

        var rows = new List<BoardViewModel>(recent.Count);
        foreach (var board in recent)
        {
            var existing = allBoards.FirstOrDefault(row => row.Id == board.Id);
            if (existing is null)
            {
                existing = new BoardViewModel(board, boardService, cardService, dialogService);
                await existing.LoadSummaryAsync();
            }
            else
            {
                await existing.RefreshAsync(board);
            }

            rows.Add(existing);
        }

        allBoards.Clear();
        allBoards.AddRange(rows);

        RowList.Merge(Boards, rows);
    }

    /// <summary>
    /// Says that the rows have changed. The blocks and the empty state are read off the lists rather
    /// than kept beside them, and a list raises nothing by itself, so it is said here.
    /// </summary>
    private void NotifyRowsChanged()
    {
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasBoards));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
