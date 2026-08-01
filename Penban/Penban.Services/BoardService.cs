// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;
using Penban.Services.Abstractions;

namespace Penban.Services;

/// <summary>Default, platform-agnostic implementation of <see cref="IBoardService"/>.</summary>
public class BoardService : IBoardService
{
    private readonly IBoardRepository repository;

    public BoardService(IBoardRepository repository)
    {
        this.repository = repository;
    }

    public Task<List<Board>> GetBoardsAsync() => repository.GetAllAsync();

    public async Task<Board> CreateBoardAsync(string title)
    {
        var board = new Board { Title = title };
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

    public Task DeleteBoardAsync(Guid boardId) => repository.DeleteAsync(boardId);

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

    public async Task DeleteColumnAsync(Guid boardId, Guid columnId)
    {
        var board = await FindBoardAsync(boardId);
        if (board is null)
        {
            return;
        }

        board.Columns.RemoveAll(c => c.Id == columnId);
        await repository.SaveAsync(board);
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
