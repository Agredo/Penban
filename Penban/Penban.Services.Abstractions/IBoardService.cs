using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>Business logic for boards and their columns (CRUD + reordering).</summary>
public interface IBoardService
{
    Task<List<Board>> GetBoardsAsync();

    /// <summary>
    /// The boards of one project, in the order the overview shows them. A board that has no project
    /// belongs to none of them - there is no "without a project" project, the overview is where those
    /// boards live.
    /// </summary>
    Task<List<Board>> GetBoardsOfProjectAsync(Guid projectId);

    /// <summary>
    /// Creates a board and gives it the three default columns. Everything the extended mode of the
    /// create dialog can say about it - labels, the time frame, the project it belongs to - arrives
    /// in <paramref name="details"/>, so the plain case and the extended one are the same call.
    /// </summary>
    Task<Board> CreateBoardAsync(BoardDetails details);

    Task RenameBoardAsync(Guid boardId, string title);

    /// <summary>
    /// Stores everything the board's details page edits. The columns are not touched: the details are
    /// what stands around them, not what is in them.
    /// </summary>
    Task SaveBoardDetailsAsync(Guid boardId, BoardDetails details);

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

    /// <summary>
    /// Persists a new order of the boards themselves after the user drags a board card on the
    /// overview: the boards are placed in the order they are handed in, which is the order the
    /// overview is then read back in.
    /// </summary>
    Task ReorderBoardsAsync(IReadOnlyList<Guid> orderedBoardIds);
}
