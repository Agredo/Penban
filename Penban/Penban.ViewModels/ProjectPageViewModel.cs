// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// One project's own page: what the project is, and the boards that hang under it. A project collects
/// boards, so its page is where they are worked with - the way into a single board is the same here
/// as on the overview.
/// </summary>
public partial class ProjectPageViewModel : ObservableObject, IQueryAttributable
{
    private readonly IProjectService projectService;
    private readonly IBoardService boardService;
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;

    /// <summary>The project as it was read. What the note is written on and what the card is built from.</summary>
    private Project? project;

    private Guid? projectId;

    public ProjectPageViewModel(IProjectService projectService, IBoardService boardService, ICardService cardService, IDialogService dialogService)
    {
        this.projectService = projectService;
        this.boardService = boardService;
        this.cardService = cardService;
        this.dialogService = dialogService;
    }

    /// <summary>
    /// The project itself, shown as the page's summary. Built from the same row the projects list
    /// uses, so its labels, its time frame and the count of its boards read the same on both.
    /// </summary>
    [ObservableProperty]
    private ProjectViewModel? projectCard;

    /// <summary>The boards under this project, in the order the service hands them out.</summary>
    public ObservableCollection<BoardViewModel> Boards { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    partial void OnIsLoadingChanged(bool value) => NotifyRowsChanged();

    /// <summary>Whether the project carries a note of its own, which is what lights the header button.</summary>
    public bool HasNote => project?.NoteStrokes.Count > 0;

    /// <summary>Whether the "nothing here" text belongs on screen: no boards and none coming.</summary>
    public bool ShowEmptyState => !IsLoading && Boards.Count == 0;

    /// <summary>
    /// Reads the route's parameter. Handed on by the page: the shell only knows pages, and this view
    /// model is the part of that page which is not allowed to know the shell.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var id = ReadGuid(query, QueryParameters.ProjectId);

        // Coming back to this page hands in nothing. That is not a different project, so it must not
        // clear the one on screen.
        if (id is null)
        {
            return;
        }

        projectId = id;
    }

    /// <summary>
    /// Reads the project and its boards. The wait is carried by the command around it rather than by
    /// the reading itself, so that no way into this method can leave the spinner running.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (projectId is not { } id)
        {
            return;
        }

        IsLoading = true;
        try
        {
            // A project that was deleted behind this page's back - from the list, or by an import -
            // leaves the page as it is rather than emptying it under the reader.
            if ((await projectService.GetProjectsAsync()).FirstOrDefault(candidate => candidate.Id == id) is not { } loaded)
            {
                return;
            }

            project = loaded;

            var boards = await boardService.GetBoardsOfProjectAsync(id);

            // A row that is already there is brought up to date rather than built again, so the place
            // the reader scrolled to survives a visit to one of the boards (see RowList).
            var rows = new List<BoardViewModel>(boards.Count);
            foreach (var board in boards)
            {
                var existing = Boards.FirstOrDefault(row => row.Id == board.Id);
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

            RowList.Merge(Boards, rows);

            // Built after the boards are known: the card's caption counts them and names the date the
            // project was last worked on, which is the latest write to the project or to a board of it.
            var lastEdited = boards.Count == 0
                ? loaded.UpdatedAtUtc
                : boards.Max(board => board.UpdatedAtUtc);

            ProjectCard = new ProjectViewModel(loaded, Boards.Count, lastEdited > loaded.UpdatedAtUtc ? lastEdited : loaded.UpdatedAtUtc);

            OnPropertyChanged(nameof(HasNote));
            NotifyRowsChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>The note editor for this project, opened by the page's header button.</summary>
    public INoteEditorTarget? CreateNoteEditor() => project is null
        ? null
        : new ProjectNoteViewModel(project, projectService, dialogService);

    /// <summary>
    /// Deletes one of the boards under this project. The board and its cards go; the project stays,
    /// since it is only what the board hung under.
    /// </summary>
    [RelayCommand]
    private async Task DeleteBoardAsync(Guid boardId)
    {
        if (Boards.All(board => board.Id != boardId))
        {
            return;
        }

        var confirmed = await dialogService.DisplayConfirmationAsync(Strings.Delete, Strings.DeleteBoardConfirmation, Strings.Delete, Strings.Cancel);
        if (!confirmed)
        {
            return;
        }

        await boardService.DeleteBoardAsync(boardId);

        if (Boards.FirstOrDefault(board => board.Id == boardId) is { } row)
        {
            Boards.Remove(row);
        }

        NotifyRowsChanged();
    }

    /// <summary>
    /// Says that the rows have changed. The empty state and the board count are read off the list
    /// rather than kept beside it, and the list raises nothing by itself, so it is said here.
    /// </summary>
    private void NotifyRowsChanged() => OnPropertyChanged(nameof(ShowEmptyState));

    /// <summary>
    /// A parameter that names something. Read from both shapes it can arrive in: the page that
    /// navigates hands in the id itself, while a route written by hand carries it as text.
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
}
