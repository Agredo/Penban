// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.Services;

/// <summary>Default, platform-agnostic implementation of <see cref="IProjectService"/>.</summary>
public class ProjectService : IProjectService
{
    private readonly IProjectRepository repository;
    private readonly IBoardRepository boardRepository;

    public ProjectService(IProjectRepository repository, IBoardRepository boardRepository)
    {
        this.repository = repository;
        this.boardRepository = boardRepository;
    }

    /// <summary>
    /// The projects list, the one that was touched last first. Same rule as the boards: the stored
    /// place comes first and the date only decides between projects that have none - every project
    /// that was never moved, and any two that were never moved apart. A project that was just created
    /// carries no place of its own either, and the date puts it on top, where the newest belongs.
    /// </summary>
    public async Task<List<Project>> GetProjectsAsync()
    {
        var projects = await repository.GetAllAsync();
        return projects
            .OrderBy(p => p.SortOrder)
            .ThenByDescending(p => p.UpdatedAtUtc)
            .ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<Project> CreateProjectAsync(ProjectDetails details)
    {
        var project = new Project { Title = details.Title };
        Apply(project, details);

        await repository.SaveAsync(project);
        return project;
    }

    public async Task SaveProjectDetailsAsync(Guid projectId, ProjectDetails details)
    {
        var project = await FindProjectAsync(projectId);
        if (project is null)
        {
            return;
        }

        Apply(project, details);
        await repository.SaveAsync(project);
    }

    /// <summary>
    /// Stores the project's own note. The strokes are copied so the caller can keep drawing into its
    /// own list, and a note without strokes is removed rather than kept as an empty one - the colour
    /// however is stored either way: it is a choice of the user, and erasing the last stroke must not
    /// repaint the card. Only dropping the note gives it up, and that hands in <c>null</c>.
    /// </summary>
    public async Task SaveProjectNoteAsync(Guid projectId, IReadOnlyList<InkStroke> strokes, int? colorIndex)
    {
        var project = await FindProjectAsync(projectId);
        if (project is null)
        {
            return;
        }

        project.NoteStrokes = strokes.Count == 0 ? [] : strokes.ToList();
        project.NoteColorIndex = colorIndex;
        await repository.SaveAsync(project);
    }

    /// <summary>
    /// Deletes the project and takes the project off its boards. The boards themselves are written
    /// through the import path rather than through <c>SaveAsync</c>: that one stamps a board with the
    /// current time, and that timestamp is what the overview prints on the card. Losing the heading
    /// over a board is not the user writing on it, so the date must not move.
    /// </summary>
    public async Task DeleteProjectAsync(Guid projectId)
    {
        await repository.DeleteAsync(projectId);

        var boards = await boardRepository.GetAllAsync();
        var attached = boards.Where(b => b.ProjectId == projectId).ToList();
        if (attached.Count == 0)
        {
            return;
        }

        foreach (var board in attached)
        {
            board.ProjectId = null;
        }

        await boardRepository.SaveAllAsync(attached);
    }

    /// <summary>
    /// Stores the order the projects list was dragged into: the given projects get the places 0, 1, 2
    /// ... in the order they were handed in, and a project that was not named - one that was created
    /// between the drag and this write - keeps its place behind them. Written through the import path
    /// for the same reason as above: dragging a card must not claim it had just been written to.
    /// </summary>
    public async Task ReorderProjectsAsync(IReadOnlyList<Guid> orderedProjectIds)
    {
        var projects = await repository.GetAllAsync();

        var place = 0;
        foreach (var projectId in orderedProjectIds)
        {
            var project = projects.FirstOrDefault(p => p.Id == projectId);
            if (project is not null)
            {
                project.SortOrder = place++;
            }
        }

        foreach (var project in projects.Where(p => !orderedProjectIds.Contains(p.Id)).OrderBy(p => p.SortOrder))
        {
            project.SortOrder = place++;
        }

        await repository.SaveAllAsync(projects);
    }

    /// <summary>
    /// Writes what a details record says onto a project. The labels go through
    /// <see cref="TagList.Normalize"/> here rather than at every call site, so no path into the
    /// database can leave an untrimmed or duplicated label behind.
    /// </summary>
    private static void Apply(Project project, ProjectDetails details)
    {
        project.Title = details.Title;
        project.Tags = TagList.Normalize(details.Tags);
        project.StartDate = details.StartDate;
        project.EndDate = details.EndDate;
    }

    private async Task<Project?> FindProjectAsync(Guid projectId)
    {
        var projects = await repository.GetAllAsync();
        return projects.FirstOrDefault(p => p.Id == projectId);
    }
}
