using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>Business logic for boards and their columns (CRUD + reordering).</summary>
public interface IBoardService
{
    Task<List<Board>> GetBoardsAsync();

    Task<Board> CreateBoardAsync(string title);

    Task RenameBoardAsync(Guid boardId, string title);

    /// <summary>
    /// Stores the board's own note. Passing no strokes deletes the note, so a board the user cleared
    /// falls back to the plain board card instead of showing an empty note.
    /// </summary>
    Task SaveBoardNoteAsync(Guid boardId, IReadOnlyList<InkStroke> strokes, int? colorIndex);

    /// <summary>Deletes the board and removes every card it contained from the database.</summary>
    Task DeleteBoardAsync(Guid boardId);

    Task<BoardColumn> AddColumnAsync(Guid boardId, string title);

    Task RenameColumnAsync(Guid boardId, Guid columnId, string title);

    /// <summary>Removes the column from its board and deletes every card it contained.</summary>
    Task DeleteColumnAsync(Guid boardId, Guid columnId);

    /// <summary>Persists a new column order after the user reorders columns via touch drag.</summary>
    Task ReorderColumnsAsync(Guid boardId, IReadOnlyList<Guid> orderedColumnIds);
}
