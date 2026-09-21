namespace Penban.Services.Abstractions;

/// <summary>
/// Writes and reads Penban export files: the whole database as a backup, a single board, or just
/// the cards of a board. A file is written next to the app and then handed to the platform share
/// sheet by <see cref="IFileShareService"/>; reading starts from a path a file picker returned.
/// </summary>
public interface IDataTransferService
{
    /// <summary>
    /// Writes the requested scope to a new file and returns where it landed; sharing it is up to
    /// the caller. <paramref name="boardId"/> is required for <see cref="ExportScope.Board"/> and
    /// <see cref="ExportScope.Cards"/>.
    /// </summary>
    Task<ExportResult> ExportAsync(ExportScope scope, Guid? boardId = null);

    /// <summary>
    /// Reads what a file holds without importing anything. Returns <c>null</c> when the file is not
    /// a Penban file, or comes from a version this build does not understand.
    /// </summary>
    Task<ImportFileInfo?> ReadAsync(string filePath);

    /// <summary>
    /// Imports a file. What the file holds decides what happens: a backup replaces or merges,
    /// a board file always becomes a new board, and a cards file is always appended to
    /// <paramref name="targetColumnId"/> - which is therefore required for a cards file.
    /// <paramref name="mode"/> only matters for a backup.
    /// </summary>
    Task<ImportResult> ImportAsync(string filePath, ImportMode mode, Guid? targetColumnId = null);
}
