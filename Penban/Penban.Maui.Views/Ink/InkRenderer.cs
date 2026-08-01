namespace Penban.Maui.Views.Ink;

/// <summary>Selects which ink drawing implementation <see cref="InkCanvasHostView"/> uses.</summary>
public enum InkRenderer
{
    /// <summary>Custom SkiaSharp <c>SKCanvasView</c> with manual pointer/pressure handling.</summary>
    Skia,

    /// <summary>Wraps <c>CommunityToolkit.Maui.Views.DrawingView</c>.</summary>
    CommunityToolkitDrawingView,
}
