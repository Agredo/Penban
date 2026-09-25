// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using CommunityToolkit.Mvvm.Input;

namespace Penban.ViewModels;

/// <summary>
/// Something the ink editor can write on. A card and the board's own note carry the same kind of
/// ink and differ only in where it is stored, so one page edits both; all the editor has to reach is
/// the strokes, the paper colour and the two commands that end the session, and that is what this
/// says.
/// </summary>
public interface INoteEditorTarget
{
    /// <summary>The strokes being drawn, in the document space of <see cref="Penban.Models.InkDocument"/>.</summary>
    InkCanvasViewModel InkCanvas { get; }

    /// <summary>
    /// Paper colour of the note being edited. A note the user never coloured reports the colour
    /// derived from its id, so the picker always has something to mark as the chosen one.
    /// </summary>
    int NoteColorIndex { get; set; }

    /// <summary>True once the user has confirmed and completed deletion, so the editor stops writing.</summary>
    bool IsDeleted { get; }

    /// <summary>Writes the ink back. There is no "Save" button; the editor calls this once the pen is idle.</summary>
    IAsyncRelayCommand SaveCommand { get; }

    /// <summary>Removes what is being edited, after asking the user.</summary>
    IAsyncRelayCommand DeleteCommand { get; }
}
