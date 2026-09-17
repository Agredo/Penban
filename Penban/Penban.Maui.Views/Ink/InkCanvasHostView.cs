using Penban.Models;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Hosts either <see cref="SkiaInkCanvasView"/> or <see cref="ToolkitInkCanvasView"/>
/// depending on <see cref="Renderer"/>, and persists the user's choice via
/// <see cref="IPreferences"/> so the same renderer is used next time the app starts.
/// </summary>
public class InkCanvasHostView : ContentView
{
    private const string RendererPreferenceKey = "InkCanvas.Renderer";

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
    /// Injects the preferences store used to persist the renderer choice, and immediately
    /// restores whichever renderer was last selected. Set this once, e.g. right after the
    /// view is created via dependency injection - XAML-instantiated controls cannot take
    /// constructor parameters, so this is done as a follow-up property instead.
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
            renderer = value;
            activeRenderer = CreateRenderer(renderer);
            activeRenderer.LoadStrokes(existingStrokes);
            Content = (View)activeRenderer;

            preferences?.Set(RendererPreferenceKey, renderer.ToString());
        }
    }

    public void LoadStrokes(IEnumerable<InkStroke> strokes) => activeRenderer.LoadStrokes(strokes);

    public IReadOnlyList<InkStroke> GetStrokes() => activeRenderer.Strokes.ToList();

    public void Clear() => activeRenderer.Clear();

    public void Undo() => activeRenderer.Undo();

    public bool IsEraserMode
    {
        get => activeRenderer.IsEraserMode;
        set => activeRenderer.IsEraserMode = value;
    }

    private static InkRenderer ReadStoredRenderer(IPreferences? preferences)
    {
        var stored = preferences?.Get(RendererPreferenceKey, InkRenderer.Skia.ToString());
        return Enum.TryParse<InkRenderer>(stored, out var parsed) ? parsed : InkRenderer.Skia;
    }

    private static IInkCanvasView CreateRenderer(InkRenderer renderer) => renderer switch
    {
        InkRenderer.CommunityToolkitDrawingView => new ToolkitInkCanvasView(),
        _ => new SkiaInkCanvasView(),
    };
}
