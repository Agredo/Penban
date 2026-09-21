#if IOS
using Foundation;
using Penban.Services.Abstractions;
using UIKit;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Reads how far the pencil is leaning and which way, and hands it to the ink canvas.
/// <para>
/// SkiaSharp's touch events carry pressure but not tilt, so the value has to come from UIKit. It is
/// read from a gesture recogniser that never recognises anything: a recogniser is handed the touches
/// on their way to the view below it, and while a touch is available its <c>AltitudeAngle</c> and
/// <c>AzimuthAngle</c> are too. The recogniser does not cancel the touches, so drawing is untouched.
/// </para>
/// <para>
/// The altitude is the angle above the surface - upright is π/2, flat is 0 - while the canvas stores
/// the lean away from upright, so it is the complement that is handed over. The azimuth is already
/// measured in the view's own coordinates from the positive x-axis towards the positive y-axis, which
/// is exactly the direction of the lean the canvas expects.
/// </para>
/// <para>
/// iOS only; the Windows counterpart is <c>PenTiltBehavior</c> (WinUI's pointer tilt) and the
/// Android one lives in <c>AndroidInkInput</c> (the activity's motion events). Only the
/// setting decides whether values are reported; it is re-read on every contact, so turning
/// <see cref="PreferenceKeys.TiltDetectionEnabled"/> off takes effect on the next stroke. A finger
/// reports no tilt at all, which is why the values are cleared when the contact is not the pencil.
/// </para>
/// </summary>
public class PenTiltBehavior : PlatformBehavior<View, UIView>
{
    private const float HalfPi = MathF.PI / 2f;

    private readonly IPreferences? preferences;
    private TouchObserver? observer;

    public PenTiltBehavior(IPreferences? preferences = null) => this.preferences = preferences;

    protected override void OnAttachedTo(View view, UIView platformView)
    {
        base.OnAttachedTo(view, platformView);

        if (view is not InkCanvasHostView host)
        {
            return;
        }

        observer = new TouchObserver(host, preferences) { CancelsTouchesInView = false };
        platformView.AddGestureRecognizer(observer);
    }

    protected override void OnDetachedFrom(View view, UIView platformView)
    {
        base.OnDetachedFrom(view, platformView);

        if (observer is null)
        {
            return;
        }

        platformView.RemoveGestureRecognizer(observer);
        observer.Dispose();
        observer = null;
    }

    /// <summary>
    /// Watches the touches without ever recognising a gesture: the recogniser is only there to be
    /// given the touch objects, which is the one place UIKit exposes the pencil's angles.
    /// </summary>
    private sealed class TouchObserver : UIGestureRecognizer
    {
        // Owned by UIKit, so the surface is held weakly to keep a closed card from being kept alive
        // by its own recogniser.
        private readonly WeakReference<InkCanvasHostView> host;
        private readonly IPreferences? preferences;

        public TouchObserver(InkCanvasHostView host, IPreferences? preferences)
        {
            this.host = new WeakReference<InkCanvasHostView>(host);
            this.preferences = preferences;
        }

        public override void TouchesBegan(NSSet touches, UIEvent evt) => Publish(touches);

        /// <summary>
        /// Moving is watched as well as pressing, so the second point of a stroke already carries the
        /// lean instead of only from the third one on.
        /// </summary>
        public override void TouchesMoved(NSSet touches, UIEvent evt) => Publish(touches);

        public override void TouchesEnded(NSSet touches, UIEvent evt) => Forget();

        public override void TouchesCancelled(NSSet touches, UIEvent evt) => Forget();

        private void Publish(NSSet touches)
        {
            if (!host.TryGetTarget(out var surface)
                || !IsEnabled()
                || touches.AnyObject is not UITouch touch
                || touch.Type != UITouchType.Stylus)
            {
                // A finger has no tilt to report; leaving the last value behind would make the next
                // pencil stroke start with somebody else's lean.
                Forget();
                return;
            }

            surface.SetStylusTilt(HalfPi - (float)touch.AltitudeAngle, (float)touch.GetAzimuthAngle(View));
        }

        private void Forget()
        {
            if (host.TryGetTarget(out var surface))
            {
                surface.SetStylusTilt(0f, 0f);
            }
        }

        private bool IsEnabled() =>
            bool.TryParse(preferences?.Get(PreferenceKeys.TiltDetectionEnabled, "false"), out var enabled) && enabled;
    }
}
#endif
