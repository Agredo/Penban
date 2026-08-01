using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>Repository for boards (including their columns) with soft-delete semantics.</summary>
public interface IBoardRepository
{
    Task<List<Board>> GetAllAsync();

    Task SaveAsync(Board board);

    /// <summary>Soft-deletes the board with the given id.</summary>
    Task DeleteAsync(Guid boardId);
}
