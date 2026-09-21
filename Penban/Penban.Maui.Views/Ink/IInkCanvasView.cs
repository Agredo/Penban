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
    /// When true, touch input removes whole strokes under the touch point instead of drawing
    /// new ones - a simple, forgiving "eraser" that matches how a real pen/eraser feels on paper.
    /// </summary>
    bool IsEraserMode { get; set; }

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
