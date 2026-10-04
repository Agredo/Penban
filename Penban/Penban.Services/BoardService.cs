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

    /// <summary>
    /// The overview's list, the board that was touched last first. The database hands the boards out
    /// in whatever order they happen to sit in, which is why a new board used to turn up somewhere in
    /// the middle of the list: nothing said where it belonged. This is the order of the boards
    /// themselves - the overview then puts the rows in the order of the date it prints on them, which
    /// counts the notes written on the board as well. Boards that carry the same timestamp - an import
    /// writes many at once - are ordered by title so the list is at least the same on every visit.
    /// <para>
    /// A board that the user dragged somewhere sits where it was put, whatever its date says: the
    /// stored place comes first and the date only decides between boards that have none - every board
    /// of a database written before that field existed, and any two that were never moved apart. A
    /// board that was just created carries no place of its own either, and the date puts it on top,
    /// where the newest board belongs.
    /// </para>
    /// </summary>
    public async Task<List<Board>> GetBoardsAsync()
    {
        var boards = await repository.GetAllAsync();
        return boards
            .OrderBy(b => b.SortOrder)
            .ThenByDescending(b => b.UpdatedAtUtc)
            .ThenBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

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
    /// own list, and a note without strokes is removed rather than kept as an empty one - the colour
    /// however is stored either way: it is a choice of the user, and erasing the last stroke must not
    /// repaint the board card. Only dropping the note gives it up, and that hands in <c>null</c>.
    /// </summary>
    public async Task SaveBoardNoteAsync(Guid boardId, IReadOnlyList<InkStroke> strokes, int? colorIndex)
    {
        var board = await FindBoardAsync(boardId);
        if (board is null)
        {
            return;
        }

        board.NoteStrokes = strokes.Count == 0 ? [] : strokes.ToList();
        board.NoteColorIndex = colorIndex;
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

    /// <summary>
    /// Stores the order the overview was dragged into: the given boards get the places 0, 1, 2 ... in
    /// the order they were handed in, and a board that was not named - one that was created between
    /// the drag and this write - keeps its place behind them.
    /// <para>
    /// Written through the import path rather than through <c>SaveAsync</c> on purpose: that one
    /// stamps the board with the current time, and that timestamp is what the overview prints on the
    /// card. Dragging a board would then claim it had just been written to, and the date the caption
    /// is read for - when the board was really last touched - would be gone.
    /// </para>
    /// </summary>
    public async Task ReorderBoardsAsync(IReadOnlyList<Guid> orderedBoardIds)
    {
        var boards = await repository.GetAllAsync();

        var place = 0;
        foreach (var boardId in orderedBoardIds)
        {
            var board = boards.FirstOrDefault(b => b.Id == boardId);
            if (board is not null)
            {
                board.SortOrder = place++;
            }
        }

        foreach (var board in boards.Where(b => !orderedBoardIds.Contains(b.Id)).OrderBy(b => b.SortOrder))
        {
            board.SortOrder = place++;
        }

        await repository.SaveAllAsync(boards);
    }

    private async Task<Board?> FindBoardAsync(Guid boardId)
    {
        var boards = await repository.GetAllAsync();
        return boards.FirstOrDefault(b => b.Id == boardId);
    }
}
