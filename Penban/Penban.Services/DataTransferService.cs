// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.IO.Compression;
using System.Text.Json;
using Penban.Models;
using Penban.Recognition;
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
    private readonly IProjectRepository projectRepository;
    private readonly IFileShareService fileShareService;
    private readonly IRecognitionStore recognitionStore;

    public DataTransferService(
        IBoardRepository boardRepository,
        ICardRepository cardRepository,
        IProjectRepository projectRepository,
        IFileShareService fileShareService,
        IRecognitionStore recognitionStore)
    {
        this.boardRepository = boardRepository;
        this.cardRepository = cardRepository;
        this.projectRepository = projectRepository;
        this.fileShareService = fileShareService;
        this.recognitionStore = recognitionStore;
    }

    public async Task<ExportResult> ExportAsync(ExportScope scope, Guid? boardId = null)
    {
        var boards = await boardRepository.GetAllAsync();
        var exportedBoards = scope == ExportScope.Backup
            ? boards
            : boards.Where(board => board.Id == boardId).Select(Flatten).ToList();

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
            // Only a backup carries the projects: a board file is handed to somebody else, and their
            // database has no project of that id. Its boards leave the project behind, see Flatten.
            Projects = scope == ExportScope.Backup ? await projectRepository.GetAllAsync() : new List<Project>(),
            // A card file leaves the structure behind on purpose: the cards are meant to land in
            // whichever column the user picks on the other side.
            Boards = scope == ExportScope.Cards ? new List<Board>() : exportedBoards,
            Cards = exportedCards,
        };

        var filePath = fileShareService.CreateExportPath(BuildFileName(scope, boardTitle));
        // A Penban file is mostly ink, and ink is mostly repetition: the same six key names over and
        // over, and absolute timestamps with a long shared prefix. Gzip takes a board with a page of
        // handwriting down to roughly an eighth of its size, in a tenth of a second.
        await using (var stream = File.Create(filePath))
        await using (var compressed = new GZipStream(stream, CompressionLevel.Optimal))
        {
            await JsonSerializer.SerializeAsync(compressed, file, PenbanJsonContext.Default.PenbanFile);
        }

        return new ExportResult(filePath, scope, file.Boards.Count, file.Cards.Count);
    }

    public async Task<ImportFileInfo?> ReadAsync(PickedFile file)
    {
        var document = await ReadFileAsync(file);
        if (document is null)
        {
            return null;
        }

        return new ImportFileInfo(
            ScopeOf(document.Kind),
            document.Boards.Count,
            document.Cards.Count,
            document.SourceBoardTitle);
    }

    public async Task<ImportResult> ImportAsync(PickedFile file, ImportMode mode, Guid? targetColumnId = null)
    {
        var document = await ReadFileAsync(file)
            ?? throw new InvalidOperationException("The file is not a readable Penban file.");

        return ScopeOf(document.Kind) switch
        {
            ExportScope.Cards => await AppendCardsAsync(document.Cards, targetColumnId),
            ExportScope.Board => await AddAsNewBoardsAsync(document.Projects, document.Boards, document.Cards),
            _ => mode == ImportMode.Replace
                ? await ReplaceAllAsync(document)
                : await AddAsNewBoardsAsync(document.Projects, document.Boards, document.Cards),
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
        await projectRepository.ClearAsync();

        await projectRepository.SaveAllAsync(file.Projects);
        await boardRepository.SaveAllAsync(file.Boards);
        await cardRepository.SaveAllAsync(file.Cards);
        await IndexTypedTextAsync(file.Cards);

        return new ImportResult(file.Boards.Count, file.Cards.Count, file.Projects.Count);
    }

    /// <summary>
    /// Adds the file's boards as new boards. Every id is replaced, and the card and column
    /// references are rewritten to follow, so nothing that is already stored can be touched.
    /// </summary>
    private async Task<ImportResult> AddAsNewBoardsAsync(
        IReadOnlyList<Project> sourceProjects,
        IReadOnlyList<Board> sourceBoards,
        IReadOnlyList<Card> sourceCards)
    {
        // A project of the file becomes a project here as well, and the boards that sat in it follow
        // their own copy. A board file brings no projects at all, so its board arrives without one -
        // which is what exporting a single board is meant to produce.
        var newProjectIds = new Dictionary<Guid, Guid>();
        var projects = new List<Project>();

        foreach (var source in sourceProjects)
        {
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Title = source.Title,
                UpdatedAtUtc = source.UpdatedAtUtc,
                SortOrder = source.SortOrder,
                Tags = source.Tags ?? [],
                StartDate = source.StartDate,
                EndDate = source.EndDate,
                NoteStrokes = source.NoteStrokes,
                NoteColorIndex = source.NoteColorIndex,
            };

            newProjectIds[source.Id] = project.Id;
            projects.Add(project);
        }

        var newColumnIds = new Dictionary<Guid, Guid>();
        var boards = new List<Board>();

        foreach (var source in sourceBoards)
        {
            var board = new Board
            {
                Id = Guid.NewGuid(),
                Title = source.Title,
                UpdatedAtUtc = source.UpdatedAtUtc,

                // The order the user dragged the boards into is part of the board: without this an
                // imported board would land at the end of the overview, behind everything already
                // there, no matter where it sat on the other side.
                SortOrder = source.SortOrder,

                // A board follows the project its own file brought with it and nothing else. A board
                // whose project is not in the file - a board file, or a hand-edited one - arrives
                // without a project rather than pointing at an id this database does not have.
                ProjectId = source.ProjectId is { } projectId && newProjectIds.TryGetValue(projectId, out var newProjectId)
                    ? newProjectId
                    : null,

                // What the extended mode put on the board belongs to the board, so it travels with
                // it, like the note below.
                Tags = source.Tags ?? [],
                StartDate = source.StartDate,
                EndDate = source.EndDate,

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

        await projectRepository.SaveAllAsync(projects);
        await boardRepository.SaveAllAsync(boards);
        await cardRepository.SaveAllAsync(cards);
        await IndexTypedTextAsync(cards);

        return new ImportResult(boards.Count, cards.Count, projects.Count);
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
        await IndexTypedTextAsync(cards);

        return new ImportResult(0, cards.Count, 0);
    }

    /// <summary>
    /// The board as a file of its own: everything that belongs to the board travels with it, the
    /// project it sits in does not. A board file is meant to be handed to somebody else, and their
    /// database has no project of that id - so the board leaves without one instead of arriving
    /// somewhere it does not belong.
    /// </summary>
    private static Board Flatten(Board source) => new()
    {
        Id = source.Id,
        UpdatedAtUtc = source.UpdatedAtUtc,
        IsDeleted = source.IsDeleted,
        SyncVersionTag = source.SyncVersionTag,
        Title = source.Title,
        SortOrder = source.SortOrder,
        ProjectId = null,
        Tags = source.Tags,
        StartDate = source.StartDate,
        EndDate = source.EndDate,
        Columns = source.Columns,
        NoteStrokes = source.NoteStrokes,
        NoteColorIndex = source.NoteColorIndex,
    };

    /// <summary>
    /// Puts the typed text of freshly imported cards into the search index, the way a save does.
    /// </summary>
    /// <remarks>
    /// An import writes through the repositories, and the index is filled on the way through
    /// <see cref="CardService"/> - so without this the rows would only appear once every imported
    /// card had been opened and saved again. Ink heals itself, because opening a board queues every
    /// card that carries ink to be read; typed text has nothing to read, so a restored backup would
    /// arrive on a new device with every typed note unfindable. Written for every card, empty text
    /// included, exactly as a save writes it: an id that already had a row must not keep the text of
    /// what used to be there.
    /// </remarks>
    private async Task IndexTypedTextAsync(IReadOnlyList<Card> cards)
    {
        foreach (var card in cards)
        {
            var typedText = CardText.Flatten(card);
            await recognitionStore
                .SaveTextAsync(card.Id, typedText, TextNormalizer.Normalize(typedText))
                .ConfigureAwait(false);
        }
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
        Tags = source.Tags ?? [],
        Strokes = source.Strokes,

        // The typed half of the note is copied field by field, because there is no serializer here
        // that would take it along: a card imported without these would come back as an empty note
        // that still says it was written in the text mode.
        Mode = source.Mode,
        TextTitle = source.TextTitle,
        TextBody = source.TextBody,
        TextSize = source.TextSize,
        TextColorHex = source.TextColorHex,
        TextBold = source.TextBold,
        TextItalic = source.TextItalic,
        UpdatedAtUtc = source.UpdatedAtUtc,
    };

    /// <summary>
    /// Reads a picked file, whether it carries the plain JSON this app has always written or the
    /// gzipped JSON it writes from 0.5.5 on. Both stay readable, so an old file still imports and a
    /// new one does not need the reader to be told which it is.
    /// </summary>
    private static async Task<PenbanFile?> ReadFileAsync(PickedFile file)
    {
        try
        {
            await using var stream = await OpenPayloadAsync(file);
            var document = await JsonSerializer.DeserializeAsync(stream, PenbanJsonContext.Default.PenbanFile);
            if (document is null || !document.IsPenbanFile || !document.IsSupportedVersion)
            {
                return null;
            }

            // A null here can only come from a hand-edited file, and would turn into a
            // NullReferenceException somewhere far less obvious than this.
            document.Projects ??= new List<Project>();
            document.Boards ??= new List<Board>();
            document.Cards ??= new List<Card>();
            return document;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidDataException)
        {
            // Well-formed enough to be opened, but not a Penban file - and for a compressed one that
            // also covers the case of a stream that claims to be gzip and then is not. Anything that
            // kept the file from being read at all - a denied access, a missing file - is
            // deliberately not caught here: calling that "not a readable Penban file" would blame
            // the file for something the system refused, and hide the real reason from the user.
            return null;
        }
    }

    /// <summary>
    /// Opens the file's bytes, unwrapping gzip when they are gzipped.
    /// </summary>
    /// <remarks>
    /// The two formats are told apart by gzip's own magic number rather than by a marker of ours, so
    /// there is nothing to declare, nothing to migrate and no way for the two to disagree. Looking
    /// costs a second <see cref="PickedFile.OpenRead"/> on the same file - which is exactly what that
    /// contract is for, and far cheaper than holding a whole backup in memory to be able to peek at
    /// its first two bytes.
    /// </remarks>
    private static async Task<Stream> OpenPayloadAsync(PickedFile file)
    {
        if (!await IsCompressedAsync(file))
        {
            return await file.OpenRead();
        }

        return new GZipStream(await file.OpenRead(), CompressionMode.Decompress);
    }

    private static async Task<bool> IsCompressedAsync(PickedFile file)
    {
        var header = new byte[2];
        await using var stream = await file.OpenRead();

        var read = 0;
        while (read < header.Length)
        {
            var got = await stream.ReadAsync(header.AsMemory(read, header.Length - read));
            if (got == 0)
            {
                break;
            }

            read += got;
        }

        return read == header.Length && header[0] == 0x1F && header[1] == 0x8B;
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
