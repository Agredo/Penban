using System.Text.Json.Serialization;
using Penban.Maui.Views.Controls;
using Penban.Models;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// Everything the home screen widget shows, in the shape both sides agree on.
/// <para>
/// The app writes this document next to the rendered previews into the folder it shares with the
/// widget; the Swift extension reads it back with <c>Codable</c>. The property names are therefore
/// part of the contract and are mirrored one to one in <c>WidgetSnapshot.swift</c> - renaming one
/// here without renaming it there quietly empties the widget, so both files move together.
/// </para>
/// <para>
/// The ink itself is not in the document. The widget shows the preview image, which is drawn from the
/// very same notes while this is written, so the strokes only have to survive the capture: they are
/// kept on the instance for the renderer and skipped by the serializer.
/// </para>
/// </summary>
public sealed class WidgetSnapshot
{
    /// <summary>Layout the reader expects; a document from another version is ignored rather than half-read.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Name of the document in the shared folder.</summary>
    public const string FileName = "widget.json";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Fingerprint of the board data this was captured from. Rendering six images per board is far too
    /// much work to repeat on every visit to the overview, so a capture carrying the fingerprint of
    /// the document already on disk is not rendered again.
    /// </summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>All boards, in overview order. The widget picks one of them.</summary>
    public List<WidgetBoard> Boards { get; set; } = [];
}

/// <summary>One board as the widget shows it: the card of the overview, minus the buttons.</summary>
public sealed class WidgetBoard
{
    /// <summary>Identity of the board; the widget remembers the chosen one by this.</summary>
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Caption under the title, exactly as the overview words it.</summary>
    public string Summary { get; set; } = string.Empty;

    public int CardCount { get; set; }

    /// <summary>Whether the board carries a note of its own; that note leads the stack.</summary>
    public bool HasNote { get; set; }

    public int NoteColorIndex { get; set; }

    public DateTimeOffset LastEditedUtc { get; set; }

    /// <summary>One entry per lane, drawn as the load bar of the overview.</summary>
    public List<WidgetColumn> Columns { get; set; } = [];

    /// <summary>Notes of the thumbnail, back to front, each with its place in the fan.</summary>
    public List<WidgetNote> Notes { get; set; } = [];

    /// <summary>Rendered previews of this board, one per widget size and appearance.</summary>
    public WidgetImages Images { get; set; } = new();
}

/// <summary>One lane of the load bar: how full it is, relative to the other lanes.</summary>
public sealed class WidgetColumn
{
    public string Title { get; set; } = string.Empty;

    public int Count { get; set; }

    public int ColorIndex { get; set; }

    /// <summary>Relative width of the lane's chip; see <c>BoardColumnSummary.Share</c>.</summary>
    public int Share { get; set; } = 1;

    /// <summary>How strongly the chip is painted; an empty lane is faded.</summary>
    public double Opacity { get; set; } = 1;
}

/// <summary>One note of the thumbnail, with everything the renderer needs to place it.</summary>
public sealed class WidgetNote
{
    public int ColorIndex { get; set; }

    /// <summary>Rotation in degrees.</summary>
    public double Tilt { get; set; }

    public double OffsetX { get; set; }

    public double OffsetY { get; set; }

    /// <summary>The ink of this note; drawn into the preview, never serialised.</summary>
    [JsonIgnore]
    public IReadOnlyList<InkStroke> Strokes { get; set; } = [];

    /// <summary>The same note in the form the stack renderer takes; derived, so it is not serialised.</summary>
    [JsonIgnore]
    public NoteStackItem Item => new(Strokes, ColorIndex, (float)Tilt, (float)OffsetX, (float)OffsetY);
}

/// <summary>
/// The rendered previews of one board, one file name per widget size and appearance. The widget picks
/// the file matching its family and the current appearance instead of drawing the card itself, which
/// keeps a second renderer of the app's own card out of the extension.
/// <para>
/// A preview is named <c>w-{boardId}-{square|wide|tall}-{light|dark}.png</c>; the board id is what
/// keeps the boards of one shared folder apart.
/// </para>
/// </summary>
public sealed class WidgetImages
{
    public string SquareLight { get; set; } = string.Empty;

    public string SquareDark { get; set; } = string.Empty;

    public string WideLight { get; set; } = string.Empty;

    public string WideDark { get; set; } = string.Empty;

    public string TallLight { get; set; } = string.Empty;

    public string TallDark { get; set; } = string.Empty;

    /// <summary>Every file name of this board, for the check that a snapshot is complete on disk.</summary>
    [JsonIgnore]
    public IEnumerable<string> All =>
    [
        SquareLight,
        SquareDark,
        WideLight,
        WideDark,
        TallLight,
        TallDark,
    ];

    /// <summary>The names all six previews of a board get, in the order the renderer writes them.</summary>
    public static WidgetImages For(Guid boardId) => new()
    {
        SquareLight = FileName(boardId, WidgetCardKind.Square, dark: false),
        SquareDark = FileName(boardId, WidgetCardKind.Square, dark: true),
        WideLight = FileName(boardId, WidgetCardKind.Wide, dark: false),
        WideDark = FileName(boardId, WidgetCardKind.Wide, dark: true),
        TallLight = FileName(boardId, WidgetCardKind.Tall, dark: false),
        TallDark = FileName(boardId, WidgetCardKind.Tall, dark: true),
    };

    /// <summary>File name of one preview; the widget asks for exactly this name.</summary>
    public static string FileName(Guid boardId, WidgetCardKind kind, bool dark)
    {
        var size = kind switch
        {
            WidgetCardKind.Square => "square",
            WidgetCardKind.Wide => "wide",
            _ => "tall",
        };

        return $"w-{boardId:N}-{size}-{(dark ? "dark" : "light")}.png";
    }
}

/// <summary>
/// Source-generated serialization for <see cref="WidgetSnapshot"/>. The reflection-based serializer
/// would be trimmed away in a release build of the iOS head - the only build that writes a snapshot -
/// so the contract is generated instead of discovered at runtime.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WidgetSnapshot))]
internal sealed partial class WidgetJsonContext : JsonSerializerContext;
