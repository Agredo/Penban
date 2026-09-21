using Penban.Models;

namespace Penban.Services.Abstractions;

/// <summary>
/// Envelope of an exported Penban file. One shape carries all three kinds of export - the whole
/// database, a single board, or just the cards of a board - so the reader only ever has to
/// recognise a single format, and <see cref="Kind"/> says what was packed.
/// </summary>
/// <remarks>
/// Deliberately plain JSON rather than a copy of the LiteDB files: it is readable, it survives a
/// LiteDB upgrade, and it can be handed to the share sheet as one self-contained file.
/// </remarks>
public sealed class PenbanFile
{
    /// <summary>
    /// Marker that identifies the file as ours; anything else is refused. Deliberately has no
    /// default value: if it did, any JSON object at all would deserialize into a valid empty
    /// backup and a "replace" import of an unrelated file would clear the whole database.
    /// </summary>
    public const string FormatName = "penban";

    /// <summary>Version this build writes. A file from a newer version is refused, not guessed at.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Extension of an exported file, including the dot, for the file picker and the file name.</summary>
    public const string FileExtension = ".penban";

    /// <summary>Every board of the database, with its columns and its cards.</summary>
    public const string BackupKind = "backup";

    /// <summary>One board with its columns and its cards.</summary>
    public const string BoardKind = "board";

    /// <summary>Only the cards of one board, without the board and its columns.</summary>
    public const string CardsKind = "cards";

    public string? Format { get; set; }

    public int Version { get; set; }

    /// <summary>One of <see cref="BackupKind"/>, <see cref="BoardKind"/> or <see cref="CardsKind"/>.</summary>
    public string? Kind { get; set; }

    public DateTimeOffset ExportedAtUtc { get; set; }

    /// <summary>Title of the board the export was taken from; <c>null</c> for a full backup.</summary>
    public string? SourceBoardTitle { get; set; }

    /// <summary>
    /// Boards including their columns. Empty for a <see cref="CardsKind"/> file, which deliberately
    /// leaves the structure behind so that the cards can be dropped into any other column.
    /// </summary>
    public List<Board> Boards { get; set; } = new();

    /// <summary>Cards of the exported boards, in column order and, within a column, in card order.</summary>
    public List<Card> Cards { get; set; } = new();

    /// <summary>Whether this really is a Penban file and not something the picker happened to return.</summary>
    public bool IsPenbanFile => string.Equals(Format, FormatName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this build understands the file's layout.</summary>
    public bool IsSupportedVersion => Version >= 1 && Version <= CurrentVersion;
}
