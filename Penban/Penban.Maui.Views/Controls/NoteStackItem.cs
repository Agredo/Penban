using Penban.Models;
using Penban.ViewModels;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// One note of a <see cref="NoteStackDrawable"/>: which ink to write, in which paper, and where in
/// the fan it sits.
/// <para>
/// It exists so the drawing does not have to know where the note came from. The overview builds these
/// from its <see cref="BoardNotePreview"/>s, the home screen widget from the notes it captured, and
/// both end up as the same thumbnail.
/// </para>
/// </summary>
/// <param name="Strokes">Ink of the note.</param>
/// <param name="ColorIndex">Paper colour, indexed like <see cref="StickyNoteBorder.NoteColors"/>.</param>
/// <param name="Tilt">Rotation in degrees.</param>
/// <param name="OffsetX">Push away from the middle of the fan, horizontally.</param>
/// <param name="OffsetY">Push away from the middle of the fan, vertically.</param>
public readonly record struct NoteStackItem(
    IReadOnlyList<InkStroke> Strokes,
    int ColorIndex,
    float Tilt,
    float OffsetX,
    float OffsetY)
{
    /// <summary>Takes the place, tilt and paper of a note the overview fanned out.</summary>
    public static NoteStackItem From(BoardNotePreview preview) => new(
        preview.Strokes,
        preview.NoteColorIndex,
        (float)preview.Tilt,
        (float)preview.OffsetX,
        (float)preview.OffsetY);
}
