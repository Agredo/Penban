namespace Penban.Services.Abstractions;

/// <summary>What an export contains.</summary>
public enum ExportScope
{
    /// <summary>Every board of the database - a full backup.</summary>
    Backup,

    /// <summary>A single board with its columns and its cards.</summary>
    Board,

    /// <summary>Only the cards of a single board.</summary>
    Cards,
}

/// <summary>How an imported file is combined with what is already stored.</summary>
public enum ImportMode
{
    /// <summary>Drop the existing data and keep the file's. Only offered for a full backup.</summary>
    Replace,

    /// <summary>Add the file's content alongside the existing data.</summary>
    Merge,
}

/// <summary>The file <see cref="IDataTransferService.ExportAsync"/> produced, and what went into it.</summary>
public sealed record ExportResult(string FilePath, ExportScope Scope, int BoardCount, int CardCount);

/// <summary>
/// What a Penban file contains, read back before anything is written so that the import can be
/// confirmed against the right expectations.
/// </summary>
public sealed record ImportFileInfo(ExportScope Scope, int BoardCount, int CardCount, string? SourceBoardTitle);

/// <summary>What <see cref="IDataTransferService.ImportAsync"/> actually added.</summary>
public sealed record ImportResult(int BoardCount, int CardCount);
