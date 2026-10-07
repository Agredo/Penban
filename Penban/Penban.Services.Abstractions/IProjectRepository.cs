using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>Repository for projects with soft-delete semantics.</summary>
public interface IProjectRepository
{
    Task<List<Project>> GetAllAsync();

    Task SaveAsync(Project project);

    /// <summary>Soft-deletes the project with the given id.</summary>
    Task DeleteAsync(Guid projectId);

    /// <summary>
    /// Writes every given project exactly as it is - id, timestamps and flags untouched, replacing
    /// any project that already carries the same id. Used when importing a backup, and when the
    /// projects list stores a new order, which must not look like a write to each of them.
    /// </summary>
    Task SaveAllAsync(IReadOnlyList<Project> projects);

    /// <summary>
    /// Removes every project from the database, including soft-deleted ones. Used when an imported
    /// backup replaces the current content.
    /// </summary>
    Task ClearAsync();
}
