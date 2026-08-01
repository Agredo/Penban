// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Penban.Models;

namespace Penban.ViewModels;

/// <summary>
/// Holds the in-progress ink strokes for the card currently being drawn on. Kept separate
/// from <see cref="CardViewModel"/> so the ink-editing session (used by whichever
/// <c>IInkCanvasView</c> renderer is active) has a small, focused surface.
/// </summary>
public partial class InkCanvasViewModel : ObservableObject
{
    public ObservableCollection<InkStroke> Strokes { get; } = new();

    public void LoadStrokes(IEnumerable<InkStroke> strokes)
    {
        Strokes.Clear();
        foreach (var stroke in strokes)
        {
            Strokes.Add(stroke);
        }
    }

    public void AddStroke(InkStroke stroke) => Strokes.Add(stroke);

    public void Clear() => Strokes.Clear();
}
