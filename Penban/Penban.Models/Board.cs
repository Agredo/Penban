namespace Penban.Models;

/// <summary>A Kanban board containing an ordered set of columns.</summary>
public class Board : SyncableEntity
{
    public string Title { get; set; } = string.Empty;

    public List<BoardColumn> Columns { get; set; } = new();
}
