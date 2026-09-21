#if WINDOWS
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Penban.Services.Abstractions;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Reads how far the pen is leaning and which way, and hands it to the ink canvas.
/// <para>
/// SkiaSharp's touch events carry pressure but not tilt, so the only place the value can be had is
/// WinUI's own pointer information: <c>PointerPointProperties.XTilt</c> and <c>YTilt</c>, in degrees.
/// WinUI measures the two axes separately, so they are combined here into the two values the canvas
/// stores: the angle away from upright, and the direction of the lean.
/// </para>
/// <para>
/// Only installed while <see cref="PreferenceKeys.TiltDetectionEnabled"/> is on; the setting is
/// re-read on every contact, so turning it off takes effect on the next stroke. The values reach the
/// canvas one event late - the pointer event is seen after the canvas has already handled it - so the
/// first point of a stroke is always recorded upright. That is invisible in practice, and the
/// alternative would be to intercept and re-dispatch every touch.
/// </para>
/// </summary>
public class PenTiltBehavior : PlatformBehavior<View, FrameworkElement>
{
    /// <summary>Beyond this the tilt is treated as flat on the surface; the tangent runs away at 90°.</summary>
    private const float MaxAxisTiltDegrees = 89f;

    private readonly IPreferences? preferences;
    private readonly PointerEventHandler onPointerPressed;
    private readonly PointerEventHandler onPointerMoved;
    private readonly PointerEventHandler onPointerEnded;

    private InkCanvasHostView? host;

    public PenTiltBehavior(IPreferences? preferences = null)
    {
        this.preferences = preferences;
        onPointerPressed = OnPointerTracked;
        onPointerMoved = OnPointerTracked;
        onPointerEnded = OnPointerEnded;
    }

    protected override void OnAttachedTo(View view, FrameworkElement platformView)
    {
        base.OnAttachedTo(view, platformView);

        host = view as InkCanvasHostView;

        // Observed rather than taken over, exactly like the pen tail eraser: the canvas has already
        // handled these events, and re-dispatching them would break its own touch handling.
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

    private void OnPointerEnded(object sender, PointerRoutedEventArgs e) => host?.SetStylusTilt(0f, 0f);

    /// <summary>
    /// Publishes the current tilt. Pressed is handled as well as Moved so the second point of a
    /// stroke is already correct instead of only from the third one on.
    /// </summary>
    private void OnPointerTracked(object sender, PointerRoutedEventArgs e)
    {
        if (host is null || sender is not UIElement element)
        {
            return;
        }

        if (e.Pointer.PointerDeviceType != PointerDeviceType.Pen || !IsEnabled())
        {
            // A finger or the mouse has no tilt to report; leaving the last value behind would make
            // the next pen stroke start with somebody else's lean.
            host.SetStylusTilt(0f, 0f);
            return;
        }

        var properties = e.GetCurrentPoint(element).Properties;

        var xRadians = ToRadians(properties.XTilt);
        var yRadians = ToRadians(properties.YTilt);

        // The two axis tilts are tangents of an angle from upright, so the total lean is the tangent
        // of the combination and the direction is the angle between the two axes.
        var tangentX = MathF.Tan(xRadians);
        var tangentY = MathF.Tan(yRadians);
        var tilt = MathF.Atan(MathF.Sqrt((tangentX * tangentX) + (tangentY * tangentY)));
        var azimuth = MathF.Atan2(yRadians, xRadians);

        host.SetStylusTilt(tilt, azimuth);
    }

    private static float ToRadians(float degrees) =>
        Math.Clamp(degrees, -MaxAxisTiltDegrees, MaxAxisTiltDegrees) * (MathF.PI / 180f);

    private bool IsEnabled() =>
        bool.TryParse(preferences?.Get(PreferenceKeys.TiltDetectionEnabled, "false"), out var enabled) && enabled;
}
#endif
