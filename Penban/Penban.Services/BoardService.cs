// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.Services;

/// <summary>Default, platform-agnostic implementation of <see cref="IBoardService"/>.</summary>
public class BoardService : IBoardService
{
    private readonly IBoardRepository repository;
    private readonly ICardRepository cardRepository;

    public BoardService(IBoardRepository repository, ICardRepository cardRepository)
    {
        this.repository = repository;
        this.cardRepository = cardRepository;
    }

    public Task<List<Board>> GetBoardsAsync() => repository.GetAllAsync();

    /// <summary>
    /// Column titles a new board starts with. A board is only usable once it has lanes to
    /// write on, and demanding a first column before anything can be drawn pushes the
    /// "add folder before adding a note" chore onto the user. Evaluated per call so the
    /// titles follow the active culture.
    /// </summary>
    private static string[] DefaultColumnTitles =>
    [
        Strings.DefaultColumnTodo,
        Strings.DefaultColumnInProgress,
        Strings.DefaultColumnDone,
    ];

    public async Task<Board> CreateBoardAsync(string title)
    {
        var board = new Board { Title = title };

        var defaultTitles = DefaultColumnTitles;
        for (var i = 0; i < defaultTitles.Length; i++)
        {
            board.Columns.Add(new BoardColumn
            {
                BoardId = board.Id,
                Title = defaultTitles[i],
                SortOrder = i,
            });
        }

        await repository.SaveAsync(board);
        return board;
    }

    public async Task RenameBoardAsync(Guid boardId, string title)
    {
        var board = await FindBoardAsync(boardId);
        if (board is null)
        {
            return;
        }

        board.Title = title;
        await repository.SaveAsync(board);
    }

    /// <summary>
    /// Stores the board's own note. The strokes are copied so the caller can keep drawing into its
    /// own list, and a note without strokes is removed rather than kept as an empty one.
    /// </summary>
    public async Task SaveBoardNoteAsync(Guid boardId, IReadOnlyList<InkStroke> strokes, int? colorIndex)
    {
        var board = await FindBoardAsync(boardId);
        if (board is null)
        {
            return;
        }

        board.NoteStrokes = strokes.Count == 0 ? [] : strokes.ToList();
        board.NoteColorIndex = strokes.Count == 0 ? null : colorIndex;
        await repository.SaveAsync(board);
    }

    /// <summary>
    /// Deletes the board together with every card in it. The cards are removed outright rather than
    /// flagged: they are only reachable through the columns of this board, so once the board is gone
    /// nothing could ever open them again.
    /// </summary>
    public async Task DeleteBoardAsync(Guid boardId)
    {
        var board = await FindBoardAsync(boardId);

        // Read the columns before deleting: afterwards the board is flagged as deleted and no longer
        // returned by GetAllAsync, so its column ids could not be looked up any more.
        var columnIds = board?.Columns.Select(c => c.Id).ToList() ?? [];

        await repository.DeleteAsync(boardId);

        foreach (var columnId in columnIds)
        {
            await cardRepository.DeleteByColumnAsync(columnId);
        }
    }

    public async Task<BoardColumn> AddColumnAsync(Guid boardId, string title)
    {
        var board = await FindBoardAsync(boardId)
            ?? throw new InvalidOperationException($"Board '{boardId}' was not found.");

        var column = new BoardColumn
        {
            BoardId = boardId,
            Title = title,
            SortOrder = board.Columns.Count,
        };

        board.Columns.Add(column);
        await repository.SaveAsync(board);
        return column;
    }

    public async Task RenameColumnAsync(Guid boardId, Guid columnId, string title)
    {
        var board = await FindBoardAsync(boardId);
        var column = board?.Columns.FirstOrDefault(c => c.Id == columnId);
        if (board is null || column is null)
        {
            return;
        }

        column.Title = title;
        await repository.SaveAsync(board);
    }

    /// <summary>
    /// Removes the column from its board and deletes every card it held, for the same reason a
    /// deleted board takes its cards with it: the cards would be unreachable from then on.
    /// </summary>
    public async Task DeleteColumnAsync(Guid boardId, Guid columnId)
    {
        var board = await FindBoardAsync(boardId);
        if (board is null)
        {
            return;
        }

        board.Columns.RemoveAll(c => c.Id == columnId);
        await repository.SaveAsync(board);
        await cardRepository.DeleteByColumnAsync(columnId);
    }

    public async Task ReorderColumnsAsync(Guid boardId, IReadOnlyList<Guid> orderedColumnIds)
    {
        var board = await FindBoardAsync(boardId);
        if (board is null)
        {
            return;
        }

        for (var i = 0; i < orderedColumnIds.Count; i++)
        {
            var column = board.Columns.FirstOrDefault(c => c.Id == orderedColumnIds[i]);
            if (column is not null)
            {
                column.SortOrder = i;
            }
        }

        board.Columns.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        await repository.SaveAsync(board);
    }

    private async Task<Board?> FindBoardAsync(Guid boardId)
    {
        var boards = await repository.GetAllAsync();
        return boards.FirstOrDefault(b => b.Id == boardId);
    }
}
