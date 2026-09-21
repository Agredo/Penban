using LiteDB;
using Penban.Models;
using Penban.Services.Abstractions;

namespace Penban.Data;

/// <summary>LiteDB-backed implementation of <see cref="IBoardRepository"/>.</summary>
public class LiteDbBoardRepository : IBoardRepository
{
    private readonly ILiteCollection<Board> boards;

    public LiteDbBoardRepository(LiteDbContext context)
    {
        boards = context.Database.GetCollection<Board>("boards");
    }

    public Task<List<Board>> GetAllAsync()
        => Task.FromResult(boards.Find(b => !b.IsDeleted).ToList());

    public Task SaveAsync(Board board)
    {
        board.UpdatedAtUtc = DateTimeOffset.UtcNow;
        boards.Upsert(board);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid boardId)
    {
        var board = boards.FindById(boardId);
        if (board is null)
        {
            return Task.CompletedTask;
        }

        board.IsDeleted = true;
        board.UpdatedAtUtc = DateTimeOffset.UtcNow;
        boards.Update(board);
        return Task.CompletedTask;
    }

    public Task SaveAllAsync(IReadOnlyList<Board> imported)
    {
        // Upsert rather than Insert: it writes what it is given without touching it, and it does not
        // abort the whole import when a file happens to name the same board twice.
        if (imported.Count > 0)
        {
            boards.Upsert(imported);
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        boards.DeleteAll();
        return Task.CompletedTask;
    }
}
