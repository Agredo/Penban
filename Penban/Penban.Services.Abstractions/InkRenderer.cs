namespace Penban.Services.Abstractions;

/// <summary>
/// Selects which ink drawing implementation the card editor uses. Lives next to
/// <see cref="PreferenceKeys.InkRenderer"/> because it is the value stored under that key, and the
/// settings page has to be able to offer it without referencing the MAUI view layer.
/// </summary>
public enum InkRenderer
{
    /// <summary>Custom SkiaSharp <c>SKCanvasView</c> with manual pointer/pressure handling.</summary>
    Skia,

    /// <summary>Wraps <c>CommunityToolkit.Maui.Views.DrawingView</c>.</summary>
    CommunityToolkitDrawingView,
}
