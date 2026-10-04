namespace Penban.Models;

/// <summary>
/// A Kanban card. A card carries handwriting as ink strokes, typed text, or both: the two are kept
/// side by side and neither is derived from the other, so switching between the pen and the keyboard
/// never loses anything.
/// </summary>
public class Card : SyncableEntity
{
    public Guid ColumnId { get; set; }

    /// <summary>Determines the display order of this card relative to its siblings.</summary>
    public int SortOrder { get; set; }

    /// <summary>The handwritten content of the card.</summary>
    public List<InkStroke> Strokes { get; set; } = new();

    /// <summary>
    /// Which of the two the card is being written in. It is a mode rather than a kind of card: both
    /// halves are kept, and the mode only says which editor the card opens in - see
    /// <see cref="CardContentMode.Ink"/> for why the default costs no migration.
    /// </summary>
    public CardContentMode Mode { get; set; }

    /// <summary>Title of the typed note, or <c>null</c>/empty when it has none.</summary>
    public string? TextTitle { get; set; }

    /// <summary>The typed text, as many lines as the user wrote. <c>null</c> while nothing was typed.</summary>
    public string? TextBody { get; set; }

    /// <summary>
    /// How large the typed text is written, in document units - the same unit
    /// <see cref="InkStroke.Thickness"/> and <see cref="InkDocument"/> use, so a size means the same
    /// thing on a phone, on a tablet and on the board's preview. See <see cref="CardText"/>.
    /// </summary>
    public float TextSize { get; set; }

    /// <summary>Colour of the typed text as <c>#RRGGBB</c>, written and read the way a pen colour is.</summary>
    public string? TextColorHex { get; set; }

    /// <summary>Whether the whole typed note - title and body together - is written in bold.</summary>
    public bool TextBold { get; set; }

    /// <summary>Whether the whole typed note is written in italics.</summary>
    public bool TextItalic { get; set; }

    /// <summary>
    /// Paper colour the user picked for this note, or <c>null</c> while the colour is still derived
    /// from <see cref="SyncableEntity.Id"/> - which is how cards created before the colour picker
    /// keep looking exactly as they did.
    /// </summary>
    public int? NoteColorIndex { get; set; }

    /// <summary>Emoji tags pinned to the note. Empty for cards written before tags existed.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// The <see cref="InkDocument"/> space version <see cref="Strokes"/> were captured in. Cards written
    /// before the coordinate space was fixed read <c>0</c> and are converted once on load; new cards are
    /// marked with the current version as soon as they are created.
    /// </summary>
    public int InkSpaceVersion { get; set; }
}

/// <summary>
/// Which of the card's two halves the editor opens in. <see cref="Ink"/> is <c>0</c> on purpose: it
/// is the value of a card that says nothing about a mode, which is every card written before the
/// typed text existed - so those keep being the ink notes they were, with no migration and no field
/// that has to be tested for null.
/// </summary>
public enum CardContentMode
{
    Ink = 0,
    Text = 1,
}
