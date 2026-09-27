#if WINDOWS
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Penban.Services.Abstractions;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Reads how far the pen is leaning, which way, and how hard it is pressed, and hands all three to
/// the ink canvas.
/// <para>
/// SkiaSharp's touch events carry neither: its Windows backend calls its own touch event with a
/// constructor that fills in a pressure of <c>1</c> and no tilt at all, so the only place the values
/// can be had is WinUI's own pointer information: <c>PointerPointProperties.Pressure</c> as a share of
/// full press, and <c>XTilt</c> and <c>YTilt</c> in degrees. WinUI measures the two tilt axes
/// separately, so they are combined here into the two values the canvas stores: the angle away from
/// upright, and the direction of the lean.
/// </para>
/// <para>
/// Installed for the whole time the editor is open, and the tilt setting is re-read on every contact,
/// so turning <see cref="PreferenceKeys.TiltDetectionEnabled"/> off takes effect on the next stroke.
/// The pressure is not the setting's business: whether a stroke varies its width with it is decided by
/// the renderer, so it is published either way. The values reach the canvas one event late - the
/// pointer event is seen after the canvas has already handled it - so the first point of a stroke is
/// always recorded upright and at full width. That is invisible in practice, and the alternative would
/// be to intercept and re-dispatch every touch.
/// </para>
/// </summary>
public class PenInputBehavior : PlatformBehavior<View, FrameworkElement>
{
    /// <summary>Beyond this the tilt is treated as flat on the surface; the tangent runs away at 90°.</summary>
    private const float MaxAxisTiltDegrees = 89f;

    /// <summary>The press WinUI reports for a pointer that cannot measure one, and how near counts as it.</summary>
    private const float NoPressureReported = 0.5f;
    private const float PressureEpsilon = 0.001f;

    private readonly IPreferences? preferences;
    private readonly PointerEventHandler onPointerPressed;
    private readonly PointerEventHandler onPointerMoved;
    private readonly PointerEventHandler onPointerEnded;

    private InkCanvasHostView? host;

    public PenInputBehavior(IPreferences? preferences = null)
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

    /// <summary>
    /// A pen that is gone has neither a lean nor a press, so both are taken back. The next stroke then
    /// starts upright and at full width instead of with whatever the pen left behind.
    /// </summary>
    private void OnPointerEnded(object sender, PointerRoutedEventArgs e)
    {
        if (host is null)
        {
            return;
        }

        host.SetStylusTilt(0f, 0f);
        host.SetStylusPressure(0f);
    }

    /// <summary>
    /// Publishes the current tilt and pressure. Pressed is handled as well as Moved so the second point
    /// of a stroke is already correct instead of only from the third one on.
    /// </summary>
    private void OnPointerTracked(object sender, PointerRoutedEventArgs e)
    {
        if (host is null || sender is not UIElement element)
        {
            return;
        }

        if (e.Pointer.PointerDeviceType != PointerDeviceType.Pen)
        {
            // A finger or the mouse has neither to report; leaving the last values behind would make
            // the next pen stroke start with somebody else's lean and somebody else's press. The
            // device decides and not the values themselves, because WinUI reports a pressure of 0.5
            // for a mouse.
            host.SetStylusTilt(0f, 0f);
            host.SetStylusPressure(0f);
            return;
        }

        var properties = e.GetCurrentPoint(element).Properties;

        // Handed over before the tilt is asked about, because the two are separate: whether a stroke
        // varies its width with the press is the renderer's business, not the tilt setting's.
        host.SetStylusPressure(Pressure(properties));

        if (!IsEnabled())
        {
            host.SetStylusTilt(0f, 0f);
            return;
        }

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

    /// <summary>
    /// How hard the pen is pressed, as a share of full press, or <c>0</c> when the pen cannot say. A
    /// digitizer that cannot measure force reports the neutral <c>0.5</c> for every contact of the
    /// whole stroke - the same value a mouse reports - and a pen that is merely resting on the glass
    /// passes through it. Read as a press it would draw that pen at half width from end to end, which
    /// is a change nobody asked for; read as "cannot say" it leaves the renderer on the width the
    /// ladder says, which is what the pen did before pressure was read at all.
    /// </summary>
    private static float Pressure(PointerPointProperties properties) =>
        MathF.Abs(properties.Pressure - NoPressureReported) < PressureEpsilon ? 0f : properties.Pressure;

    private bool IsEnabled() =>
        bool.TryParse(preferences?.Get(PreferenceKeys.TiltDetectionEnabled, "false"), out var enabled) && enabled;
}
#endif
