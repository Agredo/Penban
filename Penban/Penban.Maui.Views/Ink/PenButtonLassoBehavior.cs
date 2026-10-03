#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Penban.Services.Abstractions;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Picks strokes up while the pen's own button - the barrel button of a Surface Pen - is held, and
/// writes again as soon as it is let go. That is what the button does in Windows' own note apps: hold
/// it, draw a loop around what is to be moved, and drag it where it belongs.
/// <para>
/// The signal has to come from WinUI: the button is reported as
/// <c>PointerPointProperties.IsBarrelButtonPressed</c>, and nothing in the touch stream that SkiaSharp
/// hands over says anything about it. Only the tool is switched here, and only for as long as the
/// button is down - the loop, the drag and the undo step stay in the ordinary lasso path, which
/// already works in canvas pixels and so needs no coordinate conversion here.
/// </para>
/// <para>
/// A press that comes while the button is already held is read from the hover events, which arrive
/// before the pen touches down - which is how it is meant to be used anyway: press, then draw. A
/// contact the button is pressed on after the pen is already on the surface is drawn first and picked
/// up from the next move on.
/// </para>
/// <para>
/// Windows only. The pencil's own controls are <see cref="PencilTapBehavior"/>, and the eraser end of
/// a pen, which erases while it is held against the surface, is <see cref="PenTailEraserBehavior"/>.
/// Android's S Pen button would need an implementation of its own.
/// </para>
/// </summary>
public class PenButtonLassoBehavior : PlatformBehavior<View, FrameworkElement>
{
    private readonly IPreferences? preferences;
    private readonly PointerEventHandler onPointerPressed;
    private readonly PointerEventHandler onPointerMoved;
    private readonly PointerEventHandler onPointerEnded;

    private InkCanvasHostView? host;

    /// <summary>
    /// The tool that is put back when the button is let go. Null while the button is up, which is what
    /// says that the tool in use is the user's own choice and must not be touched.
    /// </summary>
    private InkTool? toolWithoutButton;

    /// <summary>
    /// <paramref name="preferences"/> is checked on every contact instead of once, so turning the
    /// setting off takes effect on the next stroke without reopening the card.
    /// </summary>
    public PenButtonLassoBehavior(IPreferences? preferences = null)
    {
        this.preferences = preferences;
        onPointerPressed = OnPointerPressed;
        onPointerMoved = OnPointerMoved;
        onPointerEnded = OnPointerEnded;
    }

    protected override void OnAttachedTo(View view, FrameworkElement platformView)
    {
        base.OnAttachedTo(view, platformView);

        host = view as InkCanvasHostView;

        // These are the pointer events the ink canvas has already handled, so they are observed rather
        // than taken over - which is what handledEventsToo does.
        platformView.AddHandler(UIElement.PointerPressedEvent, onPointerPressed, true);
        platformView.AddHandler(UIElement.PointerMovedEvent, onPointerMoved, true);
        platformView.AddHandler(UIElement.PointerReleasedEvent, onPointerEnded, true);
        platformView.AddHandler(UIElement.PointerCanceledEvent, onPointerEnded, true);
        platformView.AddHandler(UIElement.PointerCaptureLostEvent, onPointerEnded, true);
        platformView.AddHandler(UIElement.PointerExitedEvent, onPointerEnded, true);
    }

    protected override void OnDetachedFrom(View view, FrameworkElement platformView)
    {
        base.OnDetachedFrom(view, platformView);

        platformView.RemoveHandler(UIElement.PointerPressedEvent, onPointerPressed);
        platformView.RemoveHandler(UIElement.PointerMovedEvent, onPointerMoved);
        platformView.RemoveHandler(UIElement.PointerReleasedEvent, onPointerEnded);
        platformView.RemoveHandler(UIElement.PointerCanceledEvent, onPointerEnded);
        platformView.RemoveHandler(UIElement.PointerCaptureLostEvent, onPointerEnded);
        platformView.RemoveHandler(UIElement.PointerExitedEvent, onPointerEnded);

        host = null;
        toolWithoutButton = null;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e) => Track(sender, e);

    /// <summary>
    /// The button is read off the moves as well, because that is where it is seen before the pen
    /// touches down: a pen that is only held near the surface reports moves, not presses.
    /// </summary>
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e) => Track(sender, e);

    /// <summary>
    /// Whether the button is still down is read here as well - letting go of a pen that is still
    /// writing ends the loop but not the picking up, and a lift of the pen that keeps the button held
    /// must not take the tool away for the next loop. Only a pointer that is gone for good - capture
    /// lost, or out of range - ends it without asking.
    /// </summary>
    private void OnPointerEnded(object sender, PointerRoutedEventArgs e) => Track(sender, e);

    private void Track(object sender, PointerRoutedEventArgs e)
    {
        if (host is null || sender is not UIElement element)
        {
            return;
        }

        if (IsEnabled() && e.GetCurrentPoint(element).Properties.IsBarrelButtonPressed)
        {
            BeginSelection();
        }
        else
        {
            EndSelection();
        }
    }

    private void BeginSelection()
    {
        if (host is null || toolWithoutButton is not null)
        {
            return;
        }

        toolWithoutButton = host.Tool;
        host.Tool = InkTool.Lasso;
    }

    private void EndSelection()
    {
        if (host is null || toolWithoutButton is not { } previous)
        {
            return;
        }

        toolWithoutButton = null;
        host.Tool = previous;
    }

    private bool IsEnabled() =>
        !bool.TryParse(preferences?.Get(PreferenceKeys.PenButtonLassoEnabled, "true"), out var enabled) || enabled;
}
#endif
