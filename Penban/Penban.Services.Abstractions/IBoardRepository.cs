using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>Repository for boards (including their columns) with soft-delete semantics.</summary>
public interface IBoardRepository
{
    Task<List<Board>> GetAllAsync();

    Task SaveAsync(Board board);

    /// <summary>Soft-deletes the board with the given id.</summary>
    Task DeleteAsync(Guid boardId);

    /// <summary>
    /// Writes every given board exactly as it is - id, timestamps and flags untouched, replacing any
    /// board that already carries the same id. Used when importing a file, where the ids have to be
    /// the ones from the file so that the cards still find their columns.
    /// </summary>
    Task SaveAllAsync(IReadOnlyList<Board> boards);

    /// <summary>
    /// Removes every board from the database, including soft-deleted ones. Used when an imported
    /// backup replaces the current content.
    /// </summary>
    Task ClearAsync();
}
