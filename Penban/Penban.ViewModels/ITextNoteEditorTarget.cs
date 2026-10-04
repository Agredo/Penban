// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Models;

namespace Penban.ViewModels;

/// <summary>
/// Something that carries typed text next to its ink, which the editor can write on the same way it
/// writes ink: a card. It is a second interface rather than more members on
/// <see cref="INoteEditorTarget"/>, because not every note has text - the board's own note is written
/// by hand alone - and a target that cannot carry text should not have to pretend it can.
/// </summary>
public interface ITextNoteEditorTarget
{
    /// <summary>Which of the two the note is being written in. Kept with the note, not with the page.</summary>
    CardContentMode Mode { get; set; }

    /// <summary>Title line of the note. May be empty - the body alone is a note.</summary>
    string TextTitle { get; set; }

    /// <summary>Everything under the title, over as many lines as the user needs.</summary>
    string TextBody { get; set; }

    /// <summary>
    /// How large the text is written, in the document units of <see cref="InkDocument"/>. The size on
    /// screen is worked out from it and from the size of the note, so a note keeps its proportions
    /// however large it is shown.
    /// </summary>
    float TextSize { get; set; }

    /// <summary>Colour the text is written in, as <c>#RRGGBB</c>.</summary>
    string TextColorHex { get; set; }

    /// <summary>Whether the whole note - title and body - is written in bold.</summary>
    bool TextBold { get; set; }

    /// <summary>Whether the whole note is written in italics.</summary>
    bool TextItalic { get; set; }

    /// <summary>Whether the note carries any typed text at all, which is what the board shows as a preview.</summary>
    bool HasText { get; }
}
