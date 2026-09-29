// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// One note the search found, as the list shows it: the note itself, the text that was read from it
/// and the board it sits on. The board is named rather than the column, because that is what a tap
/// has to open and the board's title is the only name a note's place has.
/// </summary>
public sealed class SearchResultViewModel
{
    private readonly Card card;

    public SearchResultViewModel(Card card, Guid boardId, string text, string boardTitle)
    {
        ArgumentNullException.ThrowIfNull(card);

        this.card = card;
        BoardId = boardId;
        Text = text;
        BoardTitle = boardTitle;
    }

    /// <summary>The card the text was read from - what a tap opens.</summary>
    public Guid CardId => card.Id;

    /// <summary>The board that card sits on - which board a tap has to show first.</summary>
    public Guid BoardId { get; }

    /// <summary>
    /// The note's own ink, for the row to draw. A note is often recognised by what is on it rather
    /// than by what was read off it - the reading is what makes it findable, not what makes it
    /// identifiable - and a row that showed only the text would make the user open one note after
    /// another to find the one they meant.
    /// </summary>
    public IReadOnlyList<InkStroke> Strokes => card.Strokes;

    /// <summary>
    /// Paper colour of the note, so the row shows the note in the paper it has on the board. Without
    /// a choice of the user the colour is derived from the id, which is what the board does too.
    /// </summary>
    public int NoteColorIndex => card.NoteColorIndex ?? NoteStyle.PaperIndexFor(card.Id);

    /// <summary>
    /// The text as it was read, not as it was searched for: a match is a substring test, and a
    /// shortened or highlighted excerpt would need the normalised form that only exists to be
    /// compared, not to be read.
    /// </summary>
    public string Text { get; }

    /// <summary>Title of the board the note belongs to.</summary>
    public string BoardTitle { get; }

    /// <summary>Where the note is, as the line under its text.</summary>
    public string BoardLabel => string.Format(Strings.SearchBoardLabelFormat, BoardTitle);
}
