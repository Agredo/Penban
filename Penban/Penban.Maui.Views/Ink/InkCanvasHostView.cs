using Penban.Models;
using Penban.Services.Abstractions;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Hosts either <see cref="SkiaInkCanvasView"/> or <see cref="ToolkitInkCanvasView"/>
/// depending on <see cref="Renderer"/>, and persists the user's choice via
/// <see cref="IPreferences"/> so the same renderer is used next time the app starts.
/// </summary>
public class InkCanvasHostView : ContentView
{
    private IPreferences? preferences;
    private IInkCanvasView activeRenderer;
    private InkRenderer renderer;

    public InkCanvasHostView()
    {
        renderer = InkRenderer.Skia;
        activeRenderer = CreateRenderer(renderer);
        Content = (View)activeRenderer;
    }

    /// <summary>
    /// Injects the preferences store used to persist the renderer choice and to read the drawing
    /// settings, and immediately restores both. Set this once, e.g. right after the view is
    /// created via dependency injection - XAML-instantiated controls cannot take constructor
    /// parameters, so this is done as a follow-up property instead.
    /// </summary>
    public IPreferences? Preferences
    {
        get => preferences;
        set
        {
            preferences = value;
            var stored = ReadStoredRenderer(preferences);
            if (stored != renderer)
            {
                Renderer = stored;
            }
            else
            {
                ApplyPreferences();
            }
        }
    }

    public event EventHandler? StrokeCompleted
    {
        add => activeRenderer.StrokeCompleted += value;
        remove => activeRenderer.StrokeCompleted -= value;
    }

    public InkRenderer Renderer
    {
        get => renderer;
        set
        {
            if (renderer == value)
            {
                return;
            }

            var existingStrokes = activeRenderer.Strokes.ToList();
            var strokeColor = activeRenderer.StrokeColor;
            var strokeThickness = activeRenderer.StrokeThickness;
            var isEraserMode = activeRenderer.IsEraserMode;

            renderer = value;
            activeRenderer = CreateRenderer(renderer);
            activeRenderer.LoadStrokes(existingStrokes);
            activeRenderer.StrokeColor = strokeColor;
            activeRenderer.StrokeThickness = strokeThickness;
            activeRenderer.IsEraserMode = isEraserMode;
            Content = (View)activeRenderer;

            ApplyPreferences();
            preferences?.Set(PreferenceKeys.InkRenderer, renderer.ToString());
        }
    }

    public void LoadStrokes(IEnumerable<InkStroke> strokes) => activeRenderer.LoadStrokes(strokes);

    public IReadOnlyList<InkStroke> GetStrokes() => activeRenderer.Strokes.ToList();

    public void Clear() => activeRenderer.Clear();

    public void Undo() => activeRenderer.Undo();

    /// <summary>
    /// Raised when <see cref="IsEraserMode"/> changes through the setter, so the page can keep its
    /// pen/eraser buttons in step - the Apple Pencil double-tap flips the mode without any button
    /// being pressed.
    /// </summary>
    public event EventHandler? IsEraserModeChanged;

    public bool IsEraserMode
    {
        get => activeRenderer.IsEraserMode;
        set
        {
            if (activeRenderer.IsEraserMode == value)
            {
                return;
            }

            activeRenderer.IsEraserMode = value;
            IsEraserModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Colour of new strokes as <c>#RRGGBB</c>.</summary>
    public string StrokeColor
    {
        get => activeRenderer.StrokeColor;
        set => activeRenderer.StrokeColor = value;
    }

    /// <summary>Width of new strokes.</summary>
    public float StrokeThickness
    {
        get => activeRenderer.StrokeThickness;
        set => activeRenderer.StrokeThickness = value;
    }

    /// <summary>When false, only a pen or the mouse draws - see <see cref="IInkCanvasView"/>.</summary>
    public bool AllowFingerDrawing
    {
        get => activeRenderer.AllowFingerDrawing;
        set => activeRenderer.AllowFingerDrawing = value;
    }

    /// <summary>
    /// Re-reads the drawing settings. Called whenever the store is attached or the renderer is
    /// swapped, so a card opened after a setting was changed behaves the same way.
    /// </summary>
    public void ApplyPreferences()
    {
        activeRenderer.AllowFingerDrawing = ReadBool(PreferenceKeys.AllowFingerDrawing, true);

        if (activeRenderer is SkiaInkCanvasView skia)
        {
            skia.PressureSensitiveWidth = ReadBool(PreferenceKeys.PressureSensitiveWidth, true);
        }
    }

    private bool ReadBool(string key, bool @default) =>
        bool.TryParse(preferences?.Get(key, @default.ToString()), out var value) ? value : @default;

    private static InkRenderer ReadStoredRenderer(IPreferences? preferences)
    {
        var stored = preferences?.Get(PreferenceKeys.InkRenderer, InkRenderer.Skia.ToString());
        return Enum.TryParse<InkRenderer>(stored, out var parsed) ? parsed : InkRenderer.Skia;
    }

    private static IInkCanvasView CreateRenderer(InkRenderer renderer) => renderer switch
    {
        InkRenderer.CommunityToolkitDrawingView => new ToolkitInkCanvasView(),
        _ => new SkiaInkCanvasView(),
    };
}
