using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>Business logic for projects (CRUD, order, and what happens to the boards under one).</summary>
public interface IProjectService
{
    Task<List<Project>> GetProjectsAsync();

    Task<Project> CreateProjectAsync(ProjectDetails details);

    /// <summary>
    /// Stores everything the project's details page edits. The boards under the project are not
    /// touched: they are what hangs under it, not what is written on it.
    /// </summary>
    Task SaveProjectDetailsAsync(Guid projectId, ProjectDetails details);

    /// <summary>
    /// Stores the project's own note. Passing no strokes deletes the note, so a project the user
    /// cleared falls back to its monogram instead of showing an empty note.
    /// </summary>
    Task SaveProjectNoteAsync(Guid projectId, IReadOnlyList<InkStroke> strokes, int? colorIndex);

    /// <summary>
    /// Deletes the project and leaves its boards standing - they lose the project and nothing else.
    /// A board is reachable on the overview and carries work of its own, so throwing it away with the
    /// heading over it would take writing with it.
    /// </summary>
    Task DeleteProjectAsync(Guid projectId);

    /// <summary>
    /// Persists a new order of the projects themselves after the user drags a project card: the
    /// projects are placed in the order they are handed in, which is the order the list is then read
    /// back in.
    /// </summary>
    Task ReorderProjectsAsync(IReadOnlyList<Guid> orderedProjectIds);
}
