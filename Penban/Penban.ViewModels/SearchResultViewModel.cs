// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// One note the search found, as the list shows it: the text that was read from it and the board it
/// sits on. The board is named rather than the column, because that is what a tap has to open and
/// the board's title is the only name a note's place has.
/// </summary>
public sealed class SearchResultViewModel
{
    public SearchResultViewModel(Guid cardId, Guid boardId, string text, string boardTitle)
    {
        CardId = cardId;
        BoardId = boardId;
        Text = text;
        BoardTitle = boardTitle;
    }

    /// <summary>The card the text was read from - what a tap opens.</summary>
    public Guid CardId { get; }

    /// <summary>The board that card sits on - which board a tap has to show first.</summary>
    public Guid BoardId { get; }

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
