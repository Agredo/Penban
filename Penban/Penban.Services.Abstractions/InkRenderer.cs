namespace Penban.Services.Abstractions;

/// <summary>
/// Selects which ink drawing implementation the card editor uses. Lives next to
/// <see cref="PreferenceKeys.InkRenderer"/> because it is the value stored under that key.
/// </summary>
/// <remarks>
/// Not offered in the settings page: <see cref="CommunityToolkitDrawingView"/> supports neither
/// lasso selection nor shape recognition, so it is a fallback to reach for while working on the ink
/// surface, not a choice to hand a user. A build can still pick it by writing
/// <see cref="PreferenceKeys.InkRenderer"/>.
/// </remarks>
public enum InkRenderer
{
    /// <summary>Custom SkiaSharp <c>SKCanvasView</c> with manual pointer/pressure handling.</summary>
    Skia,

    /// <summary>Wraps <c>CommunityToolkit.Maui.Views.DrawingView</c>.</summary>
    CommunityToolkitDrawingView,
}
