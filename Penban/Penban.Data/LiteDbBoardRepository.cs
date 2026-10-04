using LiteDB;
using Penban.Models;
using Penban.Services.Abstractions;

namespace Penban.Data;

/// <summary>LiteDB-backed implementation of <see cref="IBoardRepository"/>.</summary>
public class LiteDbBoardRepository : IBoardRepository
{
    private readonly LiteDbContext context;

    public LiteDbBoardRepository(LiteDbContext context) => this.context = context;

    /// <summary>
    /// Asked for per call, not kept: the collection belongs to the database instance of the moment,
    /// and that one is closed when the app is left (see <see cref="LiteDbContext.Suspend"/>).
    /// </summary>
    private ILiteCollection<Board> Boards => context.Collection<Board>("boards");

    public Task<List<Board>> GetAllAsync()
        => DatabaseWork.RunAsync(() => Boards.Find(b => !b.IsDeleted).ToList());

    public Task SaveAsync(Board board) => DatabaseWork.RunAsync(() =>
    {
        board.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Boards.Upsert(board);
    });

    public Task DeleteAsync(Guid boardId) => DatabaseWork.RunAsync(() =>
    {
        var board = Boards.FindById(boardId);
        if (board is null)
        {
            return;
        }

        board.IsDeleted = true;
        board.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Boards.Update(board);
    });

    public Task SaveAllAsync(IReadOnlyList<Board> imported) => DatabaseWork.RunAsync(() =>
    {
        // Upsert rather than Insert: it writes what it is given without touching it, and it does not
        // abort the whole import when a file happens to name the same board twice.
        if (imported.Count > 0)
        {
            Boards.Upsert(imported);
        }
    });

    public Task ClearAsync() => DatabaseWork.RunAsync(() =>
    {
        Boards.DeleteAll();
    });
}
