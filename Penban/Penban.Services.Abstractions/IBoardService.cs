using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>Business logic for boards and their columns (CRUD + reordering).</summary>
public interface IBoardService
{
    Task<List<Board>> GetBoardsAsync();

    Task<Board> CreateBoardAsync(string title);

    Task RenameBoardAsync(Guid boardId, string title);

    Task DeleteBoardAsync(Guid boardId);

    Task<BoardColumn> AddColumnAsync(Guid boardId, string title);

    Task RenameColumnAsync(Guid boardId, Guid columnId, string title);

    Task DeleteColumnAsync(Guid boardId, Guid columnId);

    /// <summary>Persists a new column order after the user reorders columns via touch drag.</summary>
    Task ReorderColumnsAsync(Guid boardId, IReadOnlyList<Guid> orderedColumnIds);
}
