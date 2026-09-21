namespace Penban.Services.Abstractions;

/// <summary>
/// Every preference key the app stores, in one place. Keeping them together stops the same key
/// from being written as a string literal in several layers and silently drifting apart - the
/// settings page, the ink canvas host and the view models all have to agree on them.
/// </summary>
public static class PreferenceKeys
{
    /// <summary>Which <see cref="InkRenderer"/> a card's ink surface uses.</summary>
    public const string InkRenderer = "InkCanvas.Renderer";

    /// <summary>Paper colour the user picked last; a new note inherits it instead of a random one.</summary>
    public const string NoteLastColorIndex = "Note.LastColorIndex";

    /// <summary>Whether a finger draws. Off means only a pen draws and a finger pans the board.</summary>
    public const string AllowFingerDrawing = "Draw.AllowFingerDrawing";

    /// <summary>Whether an Apple Pencil double-tap switches to the eraser.</summary>
    public const string PencilDoubleTapEnabled = "Draw.PencilDoubleTapEnabled";

    /// <summary>Whether stylus pressure varies the line width along a stroke.</summary>
    public const string PressureSensitiveWidth = "Draw.PressureSensitiveWidth";

    /// <summary>
    /// Whether the eraser end of a pen erases while it is held against the surface. Only Windows
    /// reports which end of the pen is down (a Surface Pen's tail); the setting is inert elsewhere.
    /// </summary>
    public const string PenTailEraserEnabled = "Draw.PenTailEraserEnabled";

    /// <summary>Whether cards grow to fit their ink instead of keeping a fixed note size.</summary>
    public const string AutoSizeCards = "Board.AutoSizeCards";

    /// <summary>Whether the board reads tilt data from the stylus.</summary>
    public const string TiltDetectionEnabled = "Draw.TiltDetectionEnabled";

    /// <summary>Whether stylus tilt is rendered as a calligraphy effect.</summary>
    public const string TiltRenderingEffect = "Draw.TiltRenderingEffect";
}
