#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Penban.Services.Abstractions;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Erases while the eraser end of the pen - the tail of a Surface Pen - is held against the
/// surface, and writes again as soon as it is lifted.
/// <para>
/// SkiaSharp reports the tail as an ordinary pen contact and its touch events carry no eraser flag,
/// so the signal has to come from WinUI: <c>PointerPointProperties.IsEraser</c> is the only place
/// that says which end of the pen is down. This behaviour therefore only tells the canvas that the
/// tail is down; the erasing itself stays in the normal touch path, which already works in canvas
/// pixels and so needs no coordinate conversion here.
/// </para>
/// <para>
/// Windows only. The iOS counterpart is <see cref="PencilTapBehavior"/>; Android's S Pen button
/// would need its own implementation behind the same idea.
/// </para>
/// </summary>
public class PenTailEraserBehavior : PlatformBehavior<View, FrameworkElement>
{
    private readonly IPreferences? preferences;
    private readonly PointerEventHandler onPointerPressed;
    private readonly PointerEventHandler onPointerMoved;
    private readonly PointerEventHandler onPointerEnded;

    private InkCanvasHostView? host;

    /// <summary>
    /// <paramref name="preferences"/> is checked on every contact instead of once, so turning the
    /// setting off takes effect on the next stroke without reopening the card.
    /// </summary>
    public PenTailEraserBehavior(IPreferences? preferences = null)
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

        // These are the pointer events the ink canvas has already handled, so they are observed
        // rather than taken over - which is what handledEventsToo does.
        platformView.AddHandler(UIElement.PointerPressedEvent, onPointerPressed, true);
        platformView.AddHandler(UIElement.PointerMovedEvent, onPointerMoved, true);
        platformView.AddHandler(UIElement.PointerReleasedEvent, onPointerEnded, true);
        platformView.AddHandler(UIElement.PointerCanceledEvent, onPointerEnded, true);
        platformView.AddHandler(UIElement.PointerCaptureLostEvent, onPointerEnded, true);
    }

    protected override void OnDetachedFrom(View view, FrameworkElement platformView)
    {
        base.OnDetachedFrom(view, platformView);

        platformView.RemoveHandler(UIElement.PointerPressedEvent, onPointerPressed);
        platformView.RemoveHandler(UIElement.PointerMovedEvent, onPointerMoved);
        platformView.RemoveHandler(UIElement.PointerReleasedEvent, onPointerEnded);
        platformView.RemoveHandler(UIElement.PointerCanceledEvent, onPointerEnded);
        platformView.RemoveHandler(UIElement.PointerCaptureLostEvent, onPointerEnded);

        host = null;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e) => Track(sender, e);

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e) => Track(sender, e);

    private void OnPointerEnded(object sender, PointerRoutedEventArgs e) => host?.EndPenTailErase();

    /// <summary>
    /// Flags the contact as erasing for as long as it stays down. Moved is handled as well as
    /// Pressed so a device that only reveals the eraser once the pen has moved is covered too.
    /// </summary>
    private void Track(object sender, PointerRoutedEventArgs e)
    {
        if (host is null || sender is not UIElement element || !IsEnabled())
        {
            return;
        }

        var properties = e.GetCurrentPoint(element).Properties;
        if (properties.IsEraser || properties.IsInverted)
        {
            host.BeginPenTailErase();
        }
    }

    private bool IsEnabled() =>
        !bool.TryParse(preferences?.Get(PreferenceKeys.PenTailEraserEnabled, "true"), out var enabled) || enabled;
}
#endif
