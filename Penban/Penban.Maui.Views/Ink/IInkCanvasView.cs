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
}
