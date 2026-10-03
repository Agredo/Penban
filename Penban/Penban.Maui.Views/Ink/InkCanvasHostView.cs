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
    private bool closeOnSwipeDown;
    private bool radialMenuEnabled;

    public InkCanvasHostView()
    {
        renderer = InkRenderer.Skia;
        activeRenderer = CreateRenderer(renderer);
        activeRenderer.SelectionChanged += OnRendererSelectionChanged;
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

    /// <summary>
    /// Raised while a finger drags the card down, with where that finger is on the note - see
    /// <see cref="CloseSwipeMove"/>. Raised by the Skia renderer only: the toolkit renderer has no
    /// touch hook of its own that sees individual fingers.
    /// </summary>
    public event EventHandler<CloseSwipeMove>? CloseSwipeMoved
    {
        add
        {
            if (activeRenderer is SkiaInkCanvasView skia)
            {
                skia.CloseSwipeMoved += value;
            }
        }
        remove
        {
            if (activeRenderer is SkiaInkCanvasView skia)
            {
                skia.CloseSwipeMoved -= value;
            }
        }
    }

    /// <summary>
    /// Raised when the finger of such a drag is gone again - see <see cref="CloseSwipeMoved"/> for
    /// why the toolkit renderer never raises this.
    /// </summary>
    public event EventHandler? CloseSwipeEnded
    {
        add
        {
            if (activeRenderer is SkiaInkCanvasView skia)
            {
                skia.CloseSwipeEnded += value;
            }
        }
        remove
        {
            if (activeRenderer is SkiaInkCanvasView skia)
            {
                skia.CloseSwipeEnded -= value;
            }
        }
    }

    /// <summary>
    /// Whether a finger swiped down drags the card out of the page - see
    /// <see cref="CloseSwipeMoved"/>. Asked for by the page that can be left that way, so it is not a
    /// preference and not something the renderer reads for itself; it is remembered here because a
    /// renderer swapped in later has to be told again.
    /// </summary>
    public bool CloseOnSwipeDown
    {
        get => closeOnSwipeDown;
        set
        {
            closeOnSwipeDown = value;

            if (activeRenderer is SkiaInkCanvasView skia)
            {
                skia.CloseOnSwipeDown = value;
            }
        }
    }

    /// <summary>
    /// Raised when the menu is asked for from the note itself - see
    /// <see cref="SkiaInkCanvasView.MenuRequested"/>. Raised by the Skia renderer only: the toolkit
    /// renderer never sees individual contacts and so has no tap to read a request off.
    /// </summary>
    public event EventHandler<MenuRequest>? MenuRequested
    {
        add
        {
            if (activeRenderer is SkiaInkCanvasView skia)
            {
                skia.MenuRequested += value;
            }
        }
        remove
        {
            if (activeRenderer is SkiaInkCanvasView skia)
            {
                skia.MenuRequested -= value;
            }
        }
    }

    /// <summary>
    /// Whether a finger tapped on the note asks for the menu - see <see cref="MenuRequested"/>. Asked
    /// for by the page that has such a menu, so it is not a preference; remembered here because a
    /// renderer swapped in later has to be told again.
    /// </summary>
    public bool RadialMenuEnabled
    {
        get => radialMenuEnabled;
        set
        {
            radialMenuEnabled = value;

            if (activeRenderer is SkiaInkCanvasView skia)
            {
                skia.RadialMenuEnabled = value;
            }
        }
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
            var tool = activeRenderer.Tool;

            renderer = value;
            activeRenderer = CreateRenderer(renderer);
            activeRenderer.LoadStrokes(existingStrokes);
            activeRenderer.StrokeColor = strokeColor;
            activeRenderer.StrokeThickness = strokeThickness;
            activeRenderer.Tool = tool;

            // The handlers the page put on this view stay with this view; the ones on the renderer
            // that has just been put in its place have to be set up again here.
            activeRenderer.SelectionChanged += OnRendererSelectionChanged;
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

    public bool EraseAll() => activeRenderer.EraseAll();

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
    /// Hands the pressure reported by a platform input handler down to the renderer, <c>0</c> to
    /// <c>1</c>; <c>0</c> means the platform has nothing to add and the renderer falls back on what
    /// came with the touch event. Only the Skia renderer draws a width from it - see
    /// <see cref="SkiaInkCanvasView.SetStylusPressure"/> for why the value has to be handed in at all -
    /// and the toolkit renderer has one width for the whole stroke and ignores this.
    /// </summary>
    public void SetStylusPressure(float pressure)
    {
        if (activeRenderer is SkiaInkCanvasView skia)
        {
            skia.SetStylusPressure(pressure);
        }
    }

    /// <summary>
    /// Whether the platform names the contacts that come from the pen, through
    /// <see cref="SetStylusContact"/>. Set by the behavior that reads those contacts - see
    /// <c>PenInputBehavior</c>, which exists only on Windows and iOS - and only on Apple, where
    /// SkiaSharp's touch events call every
    /// contact
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
    /// Whether the eraser is the tool in use - <see cref="Tool"/> seen as a choice between two.
    /// Kept for the callers that only ever had those two.
    /// </summary>
    public bool IsEraserMode
    {
        get => activeRenderer.IsEraserMode;
        set => Tool = value ? InkTool.Eraser : InkTool.Pen;
    }

    /// <summary>
    /// Raised when the tool changes through the setter, so the page can keep its pen, eraser and lasso
    /// buttons in step - a pen button or a pencil tap changes the tool without any button being pressed.
    /// </summary>
    public event EventHandler? ToolChanged;

    /// <summary>Which tool the note is being worked on with.</summary>
    public InkTool Tool
    {
        get => activeRenderer.Tool;
        set
        {
            if (activeRenderer.Tool == value)
            {
                return;
            }

            activeRenderer.Tool = value;
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>How many strokes are picked up on the note, zero when none are.</summary>
    public int SelectionCount => activeRenderer.SelectionCount;

    /// <summary>
    /// Raised when strokes are picked up or put down, so the page can offer to throw the picked-up
    /// group away - which only makes sense while there is one. Raised by the renderer, which is what
    /// reads the loop off the note, and passed on from whichever renderer is in use.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Throws the picked-up strokes away as one undoable edit.</summary>
    public bool DeleteSelection() => activeRenderer.DeleteSelection();

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
            // The pen's lean is what both tilt settings work with, and only the platform input handler
            // reads it: with the switch off it never arrives, so neither setting has anything to work
            // with and the stroke is drawn as if the pen were upright.
            var tiltDetected = ReadBool(PreferenceKeys.TiltDetectionEnabled, false);

            skia.PressureSensitiveWidth = ReadBool(PreferenceKeys.PressureSensitiveWidth, true);

            // One switch makes the stroke wider the flatter the pen lies, the other turns that width
            // into the nib's own shape, which makes it depend on the direction the pen is dragged in.
            skia.TiltSensitiveWidth = tiltDetected;
            skia.TiltRenderingEffect = tiltDetected && ReadBool(PreferenceKeys.TiltRenderingEffect, false);

            // A stroke that is left standing is read as a shape - a line, a rectangle, an ellipse -
            // and replaced by it. Read here rather than in the renderer because only the renderer's
            // own view holds a timed wait, and the page above it must be able to keep the setting.
            skia.ShapeRecognition = ReadBool(PreferenceKeys.ShapeRecognitionEnabled, true);

            // The renderer that is being swapped in has to be told this again as well: it is not a
            // setting but something a platform behavior asked for.
            skia.PlatformNamesStylusContacts = platformNamesStylusContacts;

            // Nor is this a setting: the page the surface is on asks for it.
            skia.CloseOnSwipeDown = closeOnSwipeDown;
            skia.RadialMenuEnabled = radialMenuEnabled;
        }
    }

    private bool ReadBool(string key, bool @default) =>
        bool.TryParse(preferences?.Get(key, @default.ToString()), out var value) ? value : @default;

    private void OnRendererSelectionChanged(object? sender, EventArgs e) =>
        SelectionChanged?.Invoke(this, EventArgs.Empty);

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
