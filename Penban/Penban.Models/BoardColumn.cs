namespace Penban.Models;

/// <summary>A single column within a <see cref="Board"/>.</summary>
public class BoardColumn : SyncableEntity
{
    public Guid BoardId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Determines the display order of this column relative to its siblings.</summary>
    public int SortOrder { get; set; }
}
