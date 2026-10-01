using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Penban.ViewModels;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// Captures the boards the overview is showing in the shape the widget reads, and keeps a fingerprint
/// of them so the same data is not drawn twice.
/// </summary>
public static class WidgetSnapshotFactory
{
    /// <summary>
    /// Raise whenever the drawing of a widget changes without its data changing: the widget shows
    /// pre-rendered images, and a fingerprint of the data alone would keep the old pictures.
    /// </summary>
    private const int DrawVersion = 2;

    /// <summary>Builds the snapshot, including the fingerprint of the data it was built from.</summary>
    public static WidgetSnapshot Create(IReadOnlyList<BoardViewModel> boards, Guid? preferredBoardId = null)
    {
        var snapshot = new WidgetSnapshot
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            PreferredBoardId = preferredBoardId,
            Signature = Fingerprint(boards, preferredBoardId),
        };

        foreach (var board in boards)
        {
            snapshot.Boards.Add(ToWidgetBoard(board));
        }

        return snapshot;
    }

    /// <summary>Writes the snapshot as the document the widget reads.</summary>
    public static void Write(WidgetSnapshot snapshot, string file) =>
        File.WriteAllText(file, JsonSerializer.Serialize(snapshot, WidgetJsonContext.Default.WidgetSnapshot), Encoding.UTF8);

    /// <summary>
    /// The fingerprint of the document already on disk, or null when there is nothing readable there.
    /// An unreadable document counts as missing: it is overwritten rather than trusted.
    /// </summary>
    public static string? ReadSignature(string file)
    {
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize(File.ReadAllText(file, Encoding.UTF8), WidgetJsonContext.Default.WidgetSnapshot);
            return snapshot?.Signature;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static WidgetBoard ToWidgetBoard(BoardViewModel board)
    {
        // The version of the board moves the names of its previews on, so they have to be known before
        // the previews are named - and the drawer then writes into the very names the snapshot carries.
        var version = BoardFingerprint(board);

        var widget = new WidgetBoard
        {
            Id = board.Id,
            Version = version,
            Title = board.Title,
            Summary = board.WidgetSummaryText,
            CardCount = board.CardCount,
            HasNote = board.HasNote,
            NoteColorIndex = board.NoteColorIndex,
            LastEditedUtc = board.LastEditedUtc,
            Images = WidgetImages.For(board.Id, version),
        };

        foreach (var column in board.ColumnSummary)
        {
            widget.Columns.Add(new WidgetColumn
            {
                Title = column.Title,
                Count = column.CardCount,
                ColorIndex = column.NoteColorIndex,
                Share = (int)column.Share,
                Opacity = column.ChipOpacity,
            });
        }

        foreach (var note in board.PreviewNotes)
        {
            widget.Notes.Add(new WidgetNote
            {
                ColorIndex = note.NoteColorIndex,
                Tilt = note.Tilt,
                OffsetX = note.OffsetX,
                OffsetY = note.OffsetY,
                Strokes = note.Strokes,
            });
        }

        return widget;
    }

    /// <summary>
    /// Everything that ends up in the picture: the texts, the lanes with their fill, and the ink of
    /// every note of the stack. Compared only against documents written by this same app on the same
    /// device, so a fingerprint - not a canonical hash - is enough.
    /// <para>
    /// The board a widget should start on is part of it although it changes nothing about the
    /// picture: a document carrying the same boards is only rewritten - and the widget only asked to
    /// draw again - when the fingerprint moves.
    /// </para>
    /// </summary>
    private static string Fingerprint(IReadOnlyList<BoardViewModel> boards, Guid? preferredBoardId)
    {
        var text = new StringBuilder();

        text.Append(preferredBoardId?.ToString("N") ?? "none").Append('\n');

        foreach (var board in boards)
        {
            text.Append(board.Id).Append('|').Append(BoardFingerprint(board)).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>
    /// Fingerprint of one board: everything of it that ends up in the picture. Carried by the board and
    /// by the names of its previews, so a board that looks different is read afresh by the widget.
    /// <para>
    /// The caption goes in as the widget shows it, not as the overview does: "10 min ago" would move
    /// the fingerprint of every board on its own, once a minute, and have the whole widget drawn again
    /// on the next visit for a picture that is in fact still correct.
    /// </para>
    /// </summary>
    private static string BoardFingerprint(BoardViewModel board)
    {
        var text = new StringBuilder();

        text.Append(DrawVersion).Append('|')
            .Append(board.Title).Append('|')
            .Append(board.WidgetSummaryText).Append('|')
            .Append(board.CardCount).Append('|')
            .Append(board.HasNote).Append('|')
            .Append(board.NoteColorIndex).Append('|')
            .Append(board.LastEditedUtc.ToUniversalTime().Ticks).Append('|');

        foreach (var column in board.ColumnSummary)
        {
            text.Append(column.Title).Append(':').Append(column.CardCount).Append(':').Append(column.NoteColorIndex).Append(';');
        }

        foreach (var note in board.PreviewNotes)
        {
            text.Append('{').Append(note.NoteColorIndex).Append(',')
                .Append(note.Tilt).Append(',')
                .Append(note.OffsetX).Append(',')
                .Append(note.OffsetY);

            foreach (var stroke in note.Strokes)
            {
                text.Append('|').Append(stroke.Color).Append('/').Append(stroke.Thickness);

                foreach (var point in stroke.Points)
                {
                    text.Append(',').Append(point.X).Append(',').Append(point.Y);
                }
            }

            text.Append('}');
        }

        // Sixteen bytes are plenty for a name that only has to differ when the board does, and keep the
        // file name short enough to stay readable in a listing of the shared folder.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16].ToLowerInvariant();
    }
}
