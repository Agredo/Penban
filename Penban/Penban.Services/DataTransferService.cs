// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Text.Json;
using Penban.Models;
using Penban.Services.Abstractions;

namespace Penban.Services;

/// <summary>
/// Moves data in and out of the app as Penban files: the whole database as a backup, a single
/// board, or just the cards of a board.
/// </summary>
/// <remarks>
/// Two deliberate rules run through the import side. A backup that replaces everything keeps the
/// ids from the file, so that a restore is an exact copy of what was backed up. Anything that is
/// added alongside existing data gets fresh ids, so an import can never overwrite a board or a card
/// that is already there.
/// </remarks>
public class DataTransferService : IDataTransferService
{
    private readonly IBoardRepository boardRepository;
    private readonly ICardRepository cardRepository;
    private readonly IFileShareService fileShareService;

    public DataTransferService(
        IBoardRepository boardRepository,
        ICardRepository cardRepository,
        IFileShareService fileShareService)
    {
        this.boardRepository = boardRepository;
        this.cardRepository = cardRepository;
        this.fileShareService = fileShareService;
    }

    public async Task<ExportResult> ExportAsync(ExportScope scope, Guid? boardId = null)
    {
        var boards = await boardRepository.GetAllAsync();
        var exportedBoards = scope == ExportScope.Backup
            ? boards
            : boards.Where(board => board.Id == boardId).ToList();

        if (scope != ExportScope.Backup && exportedBoards.Count == 0)
        {
            throw new InvalidOperationException($"Board '{boardId}' was not found.");
        }

        // Walked in column order and, inside a column, in card order, so the file carries the same
        // order the board shows. That is what lets a card file be appended back in a sensible order.
        var exportedCards = new List<Card>();
        foreach (var board in exportedBoards)
        {
            foreach (var column in board.Columns.OrderBy(column => column.SortOrder))
            {
                exportedCards.AddRange(await cardRepository.GetByColumnAsync(column.Id));
            }
        }

        var boardTitle = exportedBoards.Count == 1 ? exportedBoards[0].Title : null;
        var file = new PenbanFile
        {
            Format = PenbanFile.FormatName,
            Version = PenbanFile.CurrentVersion,
            Kind = scope switch
            {
                ExportScope.Board => PenbanFile.BoardKind,
                ExportScope.Cards => PenbanFile.CardsKind,
                _ => PenbanFile.BackupKind,
            },
            ExportedAtUtc = DateTimeOffset.UtcNow,
            SourceBoardTitle = boardTitle,
            // A card file leaves the structure behind on purpose: the cards are meant to land in
            // whichever column the user picks on the other side.
            Boards = scope == ExportScope.Cards ? new List<Board>() : exportedBoards,
            Cards = exportedCards,
        };

        var filePath = fileShareService.CreateExportPath(BuildFileName(scope, boardTitle));
        await using (var stream = File.Create(filePath))
        {
            await JsonSerializer.SerializeAsync(stream, file, PenbanJsonContext.Default.PenbanFile);
        }

        return new ExportResult(filePath, scope, file.Boards.Count, file.Cards.Count);
    }

    public async Task<ImportFileInfo?> ReadAsync(string filePath)
    {
        var file = await ReadFileAsync(filePath);
        if (file is null)
        {
            return null;
        }

        return new ImportFileInfo(
            ScopeOf(file.Kind),
            file.Boards.Count,
            file.Cards.Count,
            file.SourceBoardTitle);
    }

    public async Task<ImportResult> ImportAsync(string filePath, ImportMode mode, Guid? targetColumnId = null)
    {
        var file = await ReadFileAsync(filePath)
            ?? throw new InvalidOperationException("The file is not a readable Penban file.");

        return ScopeOf(file.Kind) switch
        {
            ExportScope.Cards => await AppendCardsAsync(file.Cards, targetColumnId),
            ExportScope.Board => await AddAsNewBoardsAsync(file.Boards, file.Cards),
            _ => mode == ImportMode.Replace
                ? await ReplaceAllAsync(file)
                : await AddAsNewBoardsAsync(file.Boards, file.Cards),
        };
    }

    /// <summary>
    /// Restores the file over the current content. Everything goes first, so the file's own ids and
    /// timestamps survive untouched and the result is exactly what was backed up.
    /// </summary>
    private async Task<ImportResult> ReplaceAllAsync(PenbanFile file)
    {
        await boardRepository.ClearAsync();
        await cardRepository.ClearAsync();

        await boardRepository.SaveAllAsync(file.Boards);
        await cardRepository.SaveAllAsync(file.Cards);

        return new ImportResult(file.Boards.Count, file.Cards.Count);
    }

    /// <summary>
    /// Adds the file's boards as new boards. Every id is replaced, and the card and column
    /// references are rewritten to follow, so nothing that is already stored can be touched.
    /// </summary>
    private async Task<ImportResult> AddAsNewBoardsAsync(IReadOnlyList<Board> sourceBoards, IReadOnlyList<Card> sourceCards)
    {
        var newColumnIds = new Dictionary<Guid, Guid>();
        var boards = new List<Board>();

        foreach (var source in sourceBoards)
        {
            var board = new Board
            {
                Id = Guid.NewGuid(),
                Title = source.Title,
                UpdatedAtUtc = source.UpdatedAtUtc,

                // The note belongs to the board, so it travels with it: importing a board that had
                // one must not quietly lose the only note that says what the board is about. The
                // strokes are handed on by reference, like a card's.
                NoteStrokes = source.NoteStrokes,
                NoteColorIndex = source.NoteColorIndex,
            };

            foreach (var sourceColumn in source.Columns)
            {
                var columnId = Guid.NewGuid();
                newColumnIds[sourceColumn.Id] = columnId;
                board.Columns.Add(new BoardColumn
                {
                    Id = columnId,
                    BoardId = board.Id,
                    Title = sourceColumn.Title,
                    SortOrder = sourceColumn.SortOrder,
                    UpdatedAtUtc = sourceColumn.UpdatedAtUtc,
                });
            }

            boards.Add(board);
        }

        var cards = new List<Card>();
        foreach (var source in sourceCards)
        {
            // A card whose column was not part of the file has nowhere to go; that can only come
            // from a hand-edited file, and dropping it is better than failing the whole import.
            if (newColumnIds.TryGetValue(source.ColumnId, out var columnId))
            {
                cards.Add(CloneCard(source, columnId, Guid.NewGuid(), source.SortOrder));
            }
        }

        await boardRepository.SaveAllAsync(boards);
        await cardRepository.SaveAllAsync(cards);

        return new ImportResult(boards.Count, cards.Count);
    }

    /// <summary>
    /// Appends the file's cards to one column, keeping the order they were exported in and numbering
    /// them behind the cards that are already in the column.
    /// </summary>
    private async Task<ImportResult> AppendCardsAsync(IReadOnlyList<Card> sourceCards, Guid? targetColumnId)
    {
        if (targetColumnId is not { } columnId)
        {
            throw new InvalidOperationException("Cards can only be imported into a column.");
        }

        var existing = await cardRepository.GetByColumnAsync(columnId);
        var nextSortOrder = existing.Count == 0 ? 0 : existing.Max(card => card.SortOrder) + 1;

        var cards = new List<Card>();
        foreach (var source in sourceCards)
        {
            cards.Add(CloneCard(source, columnId, Guid.NewGuid(), nextSortOrder++));
        }

        await cardRepository.SaveAllAsync(cards);

        return new ImportResult(0, cards.Count);
    }

    /// <summary>
    /// The ink strokes are handed on by reference: the deserialized file is thrown away straight
    /// after this, and copying the points of every stroke would be the most expensive part of an
    /// import by far.
    /// </summary>
    private static Card CloneCard(Card source, Guid columnId, Guid id, int sortOrder) => new()
    {
        Id = id,
        ColumnId = columnId,
        SortOrder = sortOrder,
        NoteColorIndex = source.NoteColorIndex,
        Strokes = source.Strokes,
        UpdatedAtUtc = source.UpdatedAtUtc,
    };

    private static async Task<PenbanFile?> ReadFileAsync(string filePath)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var file = await JsonSerializer.DeserializeAsync(stream, PenbanJsonContext.Default.PenbanFile);
            if (file is null || !file.IsPenbanFile || !file.IsSupportedVersion)
            {
                return null;
            }

            // A null here can only come from a hand-edited file, and would turn into a
            // NullReferenceException somewhere far less obvious than this.
            file.Boards ??= new List<Board>();
            file.Cards ??= new List<Card>();
            return file;
        }
        catch (Exception exception) when (exception is JsonException
                                             or IOException
                                             or UnauthorizedAccessException
                                             or NotSupportedException)
        {
            return null;
        }
    }

    private static ExportScope ScopeOf(string? kind) => kind switch
    {
        PenbanFile.BoardKind => ExportScope.Board,
        PenbanFile.CardsKind => ExportScope.Cards,
        _ => ExportScope.Backup,
    };

    private static string BuildFileName(ExportScope scope, string? boardTitle)
    {
        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        var extension = PenbanFile.FileExtension;
        return scope switch
        {
            ExportScope.Board => $"penban-board-{Sanitize(boardTitle)}-{stamp}{extension}",
            ExportScope.Cards => $"penban-cards-{Sanitize(boardTitle)}-{stamp}{extension}",
            _ => $"penban-backup-{stamp}{extension}",
        };
    }

    /// <summary>A board title is user input and ends up in a file name, so it cannot be trusted as is.</summary>
    private static string Sanitize(string? boardTitle)
    {
        if (string.IsNullOrWhiteSpace(boardTitle))
        {
            return "board";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(boardTitle.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        cleaned = cleaned.Trim('.', ' ');
        if (cleaned.Length == 0)
        {
            return "board";
        }

        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }
}
