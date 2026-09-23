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
    private bool platformNamesStylusContacts;

    public InkCanvasHostView()
    {
        renderer = InkRenderer.Skia;
        activeRenderer = CreateRenderer(renderer);
        Content = (View)activeRenderer;
    }

    /// <summary>
    /// Points the Android touch reader at this surface while it is alive - see
    /// <see cref="AndroidInkInput"/>. Only Android needs it: there the pen's tilt and the
    /// two- and three-finger taps are only visible to the activity, and the drawing surface itself
    /// keeps its touches to itself.
    /// </summary>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

#if ANDROID
        if (Handler is null)
        {
            AndroidInkInput.Detach();
        }
        else
        {
            AndroidInkInput.Attach(this);
        }
#endif
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

    public void Redo() => activeRenderer.Redo();

    public bool CanUndo => activeRenderer.CanUndo;

    public bool CanRedo => activeRenderer.CanRedo;

    /// <summary>
    /// Hands the stylus tilt reported by a platform input handler down to the renderer. Only the Skia
    /// renderer stores tilt; the toolkit renderer has nowhere to put it and ignores this.
    /// </summary>
    public void SetStylusTilt(float tilt, float azimuth)
    {
        if (activeRenderer is SkiaInkCanvasView skia)
        {
            skia.SetStylusTilt(tilt, azimuth);
        }
    }

    /// <summary>
    /// Whether the platform names the contacts that come from the pen, through
    /// <see cref="SetStylusContact"/>. Set by the behavior that reads those contacts - see
    /// <see cref="PenTiltBehavior"/> - and only on Apple, where SkiaSharp's touch events call every
    /// contact a finger and the pencil therefore cannot be told from a hand without being named. While
    /// it is on, the canvas waits for a press to be named before it decides what to do with it.
    /// </summary>
    public bool PlatformNamesStylusContacts
    {
        get => platformNamesStylusContacts;
        set
        {
            platformNamesStylusContacts = value;

            if (activeRenderer is SkiaInkCanvasView skia)
            {
                skia.PlatformNamesStylusContacts = value;
            }
        }
    }

    /// <summary>
    /// Hands the contact the platform named as the pen down to the renderer - see
    /// <see cref="SkiaInkCanvasView.SetStylusContact"/>. Only the Skia renderer can tell the pen from
    /// a finger; the toolkit renderer has no such distinction and ignores this.
    /// </summary>
    public void SetStylusContact(long contactId)
    {
        if (activeRenderer is SkiaInkCanvasView skia)
        {
            skia.SetStylusContact(contactId);
        }
    }

    /// <summary>Ends a named pen contact again - see <see cref="SetStylusContact"/>.</summary>
    public void EndStylusContact(long contactId)
    {
        if (activeRenderer is SkiaInkCanvasView skia)
        {
            skia.EndStylusContact(contactId);
        }
    }

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
    /// Erases while the eraser end of the pen stays down - see
    /// <see cref="SkiaInkCanvasView.BeginPenTailErase"/>. Only the Skia renderer sees pen input
    /// finely enough to tell the two ends of a pen apart; the toolkit renderer ignores this.
    /// </summary>
    public void BeginPenTailErase()
    {
        if (activeRenderer is SkiaInkCanvasView skia)
        {
            skia.BeginPenTailErase();
        }
    }

    /// <summary>Ends the eraser-end contact - see <see cref="BeginPenTailErase"/>.</summary>
    public void EndPenTailErase()
    {
        if (activeRenderer is SkiaInkCanvasView skia)
        {
            skia.EndPenTailErase();
        }
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
            skia.TiltRenderingEffect = ReadBool(PreferenceKeys.TiltDetectionEnabled, false)
                && ReadBool(PreferenceKeys.TiltRenderingEffect, false);

            // The renderer that is being swapped in has to be told this again as well: it is not a
            // setting but something a platform behavior asked for.
            skia.PlatformNamesStylusContacts = platformNamesStylusContacts;
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
