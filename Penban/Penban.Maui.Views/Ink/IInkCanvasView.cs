using Penban.Models;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// UI-level contract for a card's ink drawing surface. Deliberately lives here (not in
/// Penban.Services.Abstractions) since it exposes MAUI view-level members and is not a
/// candidate for a ViewModel abstraction. Both ink renderer implementations convert
/// internally to/from <see cref="InkStroke"/>/<see cref="InkPoint"/>, so consuming code
/// never needs to know which renderer is active.
/// </summary>
public interface IInkCanvasView
{
    ObservableStrokeCollection Strokes { get; }

    event EventHandler? StrokeCompleted;

    /// <summary>
    /// Raised once a stroke that was drawn is on the note, with that stroke. Unlike
    /// <see cref="StrokeCompleted"/> this is not about the note having changed but about something
    /// having been written: rubbing out, carrying a group and taking an edit back all raise the former
    /// and none of them raise this. What it carries is the stroke itself, so the colour it was drawn in
    /// is read off the stroke rather than guessed from the pen that happens to be in hand.
    /// </summary>
    event EventHandler<InkStroke>? StrokeDrawn;

    void Clear();

    void LoadStrokes(IEnumerable<InkStroke> strokes);

    /// <summary>Removes the most recently completed stroke, if any.</summary>
    void Undo();

    /// <summary>Puts back the stroke that <see cref="Undo"/> removed last, if any.</summary>
    void Redo();

    /// <summary>Whether there is an edit to <see cref="Undo"/>.</summary>
    bool CanUndo { get; }

    /// <summary>Whether there is an undone edit to <see cref="Redo"/>.</summary>
    bool CanRedo { get; }

    /// <summary>
    /// Removes every stroke, as one edit that <see cref="Undo"/> brings back in full. Unlike
    /// <see cref="Clear"/>, which throws the history away because the stroke set is about to be
    /// replaced from somewhere else, this is a change the user made and can take back.
    /// </summary>
    /// <returns>Whether there was anything to remove.</returns>
    bool EraseAll();

    /// <summary>Which of the tools a contact on the surface is taken as - see <see cref="InkTool"/>.</summary>
    InkTool Tool { get; set; }

    /// <summary>
    /// Raised when <see cref="Tool"/> changes, whatever changed it: a button of the toolbar, the
    /// Apple Pencil's own double-tap, or a pen button that is only down for the length of a press.
    /// </summary>
    event EventHandler? ToolChanged;

    /// <summary>
    /// When true, touch input removes whole strokes under the touch point instead of drawing
    /// new ones - a simple, forgiving "eraser" that matches how a real pen/eraser feels on paper.
    /// <para>
    /// This is <see cref="Tool"/> being the eraser, and it stays because the two have to agree
    /// whichever of them a caller writes: reading it is how code that only knows of the two tools
    /// asks which one is drawing.
    /// </para>
    /// </summary>
    bool IsEraserMode { get; set; }

    /// <summary>How many strokes the lasso has picked up - see <see cref="DeleteSelection"/>.</summary>
    int SelectionCount { get; }

    /// <summary>
    /// Raised when the set of picked-up strokes changes: a lasso was closed around a different
    /// set, one was taken away, or the set was dropped because the strokes underneath were replaced
    /// (an undo, a load, a wipe).
    /// </summary>
    event EventHandler? SelectionChanged;

    /// <summary>
    /// Removes the strokes the lasso picked up, as one edit that <see cref="Undo"/> brings back in
    /// full. The selection is then empty again, because what it named is gone.
    /// </summary>
    /// <returns>Whether there was a selection to remove.</returns>
    bool DeleteSelection();

    /// <summary>
    /// Drops the picked-up group without touching the note: the strokes stay where they are and only
    /// the mark around them and the offer to throw them away go. This is the lasso being let go of,
    /// which the button that picked the group up is also the one to ask for - a group that is held is
    /// never held without a way of putting it down.
    /// </summary>
    void ClearSelection();

    /// <summary>
    /// Reads the loop being drawn the way lifting the pen does, without the contact that drew it having
    /// to be lifted: this is the pen's own button being let go, where the button is what drew the loop -
    /// see <see cref="SkiaInkCanvasView.FinishLasso"/>. Renderers that cannot draw a loop ignore this.
    /// </summary>
    void FinishLasso();

    /// <summary>
    /// Colour of strokes drawn from now on, as <c>#RRGGBB</c> - the same format as
    /// <see cref="InkStroke.Color"/>, so it survives the round trip through the database.
    /// </summary>
    string StrokeColor { get; set; }

    /// <summary>Width of strokes drawn from now on, matching <see cref="InkStroke.Thickness"/>.</summary>
    float StrokeThickness { get; set; }

    /// <summary>
    /// When false, a finger no longer draws - only a pen or the mouse does, so a hand resting on
    /// the screen cannot leave marks. Renderers that cannot tell input devices apart ignore this.
    /// </summary>
    bool AllowFingerDrawing { get; set; }
}
