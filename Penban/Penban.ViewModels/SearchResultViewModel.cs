// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;
using Penban.Recognition;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// One note the search found, as the list shows it: the note itself, the text that was matched and the
/// board it sits on. The board is named rather than the column, because that is what a tap has to open
/// and the board's title is the only name a note's place has.
/// </summary>
public sealed class SearchResultViewModel
{
    private readonly Card card;

    /// <param name="card">The card the match belongs to - read whole, because the row draws it.</param>
    /// <param name="boardId">The board that card sits on - which board a tap has to show first.</param>
    /// <param name="match">What was stored for the card: its read text and its typed text.</param>
    /// <param name="normalizedQuery">
    /// The query as it was searched for, in the form the store compared it in. Which of the two texts
    /// matched is worked out from it - the texts are only comparable in that form.
    /// </param>
    /// <param name="boardTitle">Title of the board, as the line under the text shows it.</param>
    public SearchResultViewModel(
        Card card,
        Guid boardId,
        StoredRecognition match,
        string normalizedQuery,
        string boardTitle)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(normalizedQuery);

        this.card = card;
        BoardId = boardId;
        BoardTitle = boardTitle;
        IsTyped = match.TypedTextMatches(normalizedQuery);
        MatchedText = IsTyped ? match.TypedText : match.RawText;
    }

    /// <summary>The card the text was found on - what a tap opens.</summary>
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
    /// The text that was matched - the one the user typed, or the one that was read off the ink,
    /// whichever the query was found in. Either way it is the text as it was written and not as it was
    /// searched for: a match is a substring test, and a shortened or highlighted excerpt would need the
    /// normalised form, which exists to be compared and not to be read.
    /// </summary>
    public string MatchedText { get; }

    /// <summary>Whether it was the typed text that matched, rather than a reading of the ink.</summary>
    public bool IsTyped { get; }

    /// <summary>
    /// Title of the board the note belongs to.
    /// </summary>
    public string BoardTitle { get; }

    /// <summary>Where the note is, as the line under its text.</summary>
    public string BoardLabel => string.Format(Strings.SearchBoardLabelFormat, BoardTitle);

    /// <summary>
    /// Whether the row has to draw the text instead of ink. A note that is written in the text field
    /// has no ink to draw, and a card with a typed text and no ink would otherwise be a blank square
    /// in the results - the one spot where the note has to be recognisable at a glance.
    /// </summary>
    public bool ShowTextPreview => (card.Strokes?.Count ?? 0) == 0 && CardText.HasText(card);

    /// <summary>The beginning of the note's own text, for that preview.</summary>
    public string PreviewText => CardText.Flatten(card);
}
