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
    /// <summary>Builds the snapshot, including the fingerprint of the data it was built from.</summary>
    public static WidgetSnapshot Create(IReadOnlyList<BoardViewModel> boards)
    {
        var snapshot = new WidgetSnapshot
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Signature = Fingerprint(boards),
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
        var widget = new WidgetBoard
        {
            Id = board.Id,
            Title = board.Title,
            Summary = board.SummaryText,
            CardCount = board.CardCount,
            HasNote = board.HasNote,
            NoteColorIndex = board.NoteColorIndex,
            LastEditedUtc = board.LastEditedUtc,
            Images = WidgetImages.For(board.Id),
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
    /// </summary>
    private static string Fingerprint(IReadOnlyList<BoardViewModel> boards)
    {
        var text = new StringBuilder();

        foreach (var board in boards)
        {
            text.Append(board.Id).Append('|')
                .Append(board.Title).Append('|')
                .Append(board.SummaryText).Append('|')
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

            text.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
