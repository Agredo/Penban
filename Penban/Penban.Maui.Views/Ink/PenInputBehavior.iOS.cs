#if IOS
using Foundation;
using Penban.Services.Abstractions;
using UIKit;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Reads which contact the pencil is, how it is leaning and how hard it is pressed, and hands all of
/// it to the ink canvas.
/// <para>
/// SkiaSharp's Apple backend reads none of it out of a touch: it calls every contact a finger and fills
/// in a pressure of <c>1</c>, so all of it has to come from UIKit. It is read from a gesture
/// recogniser that never recognises anything: a recogniser is handed the touches on their way to the
/// view below it, and while a touch is available its <c>Type</c> says whether it is the pencil, its
/// <c>AltitudeAngle</c> and <c>AzimuthAngle</c> say how it is held, and its <c>Force</c> says how hard
/// it is pressed. The recogniser does not cancel the touches, so drawing is untouched.
/// </para>
/// <para>
/// The altitude is the angle above the surface - upright is π/2, flat is 0 - while the canvas stores
/// the lean away from upright, so it is the complement that is handed over. The azimuth is already
/// measured in the view's own coordinates from the positive x-axis towards the positive y-axis, which
/// is exactly the direction of the lean the canvas expects. The force is handed over as a share of
/// <c>MaximumPossibleForce</c>, because that is the number the canvas draws with.
/// </para>
/// <para>
/// iOS only; the Windows counterpart is <c>PenInputBehavior</c> (WinUI's pointer tilt and pressure)
/// and the Android one lives in <c>AndroidInkInput</c> (the activity's motion events). Whether a tilt
/// is reported is the setting's business alone - it is re-read on every contact, so turning
/// <see cref="PreferenceKeys.TiltDetectionEnabled"/> off takes effect on the next stroke. Naming the
/// pen is not, and neither is the pressure: both happen whatever the setting says, because drawing has
/// to know which contact the pen is either way, and because whether a stroke varies its width with the
/// pressure is decided by the renderer rather than here.
/// </para>
/// </summary>
public class PenInputBehavior : PlatformBehavior<View, UIView>
{
    private const float HalfPi = MathF.PI / 2f;

    private readonly IPreferences? preferences;
    private TouchObserver? observer;

    public PenInputBehavior(IPreferences? preferences = null) => this.preferences = preferences;

    protected override void OnAttachedTo(View view, UIView platformView)
    {
        base.OnAttachedTo(view, platformView);

        if (view is not InkCanvasHostView host)
        {
            return;
        }

        // This recogniser is the only thing that can tell the canvas which contact the pencil is, so
        // it says so for as long as it is attached - see InkCanvasHostView.PlatformNamesStylusContacts.
        host.PlatformNamesStylusContacts = true;

        observer = new TouchObserver(host, preferences) { CancelsTouchesInView = false };
        platformView.AddGestureRecognizer(observer);
    }

    protected override void OnDetachedFrom(View view, UIView platformView)
    {
        base.OnDetachedFrom(view, platformView);

        if (view is InkCanvasHostView host)
        {
            host.PlatformNamesStylusContacts = false;
        }

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
    /// given the touch objects, which is the one place UIKit says which contact the pencil is and how
    /// it is held.
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
        /// lean instead of only from the third one on - and so a contact is still named the pen when
        /// the platform only gets round to reporting it with a later touch.
        /// </summary>
        public override void TouchesMoved(NSSet touches, UIEvent evt) => Publish(touches);

        public override void TouchesEnded(NSSet touches, UIEvent evt) => Release(touches);

        public override void TouchesCancelled(NSSet touches, UIEvent evt) => Release(touches);

        private void Publish(NSSet touches)
        {
            if (!host.TryGetTarget(out var surface))
            {
                return;
            }

            var isTiltEnabled = IsEnabled();
            var sawPencil = false;
            var pressure = 0f;

            // Every touch is looked at, not just the first one: the pencil and a resting hand can be
            // reported together, and which of them comes first is not something to rely on.
            foreach (var touch in touches.Cast<UITouch>())
            {
                if (touch.Type != UITouchType.Stylus)
                {
                    continue;
                }

                sawPencil = true;

                // The canvas has to be told which contact the pencil is, because SkiaSharp's own touch
                // events call every contact a finger - see SkiaInkCanvasView.SetStylusContact. It is
                // told on every touch the pencil makes, so a contact that was named too late for its
                // press is still named in time for the stroke that press belongs to.
                surface.SetStylusContact(ContactId(touch));

                // The hardest press of the touches reported together is the one the pencil is making:
                // the pencil is the only contact here that can press at all, and a value of 0 is left
                // for a pencil whose force is not reported - see Force.
                pressure = MathF.Max(pressure, Pressure(touch));

                if (isTiltEnabled)
                {
                    surface.SetStylusTilt(HalfPi - (float)touch.AltitudeAngle, (float)touch.GetAzimuthAngle(View));
                }
            }

            // Not the setting's business: whether a stroke varies its width with the press is decided
            // by the renderer, so this is handed over even while the tilt is off.
            surface.SetStylusPressure(sawPencil ? pressure : 0f);

            if (!isTiltEnabled || !sawPencil)
            {
                // A finger has no lean to report and a setting that is off has none to give; leaving
                // the last value behind would make the next pencil stroke start with somebody else's.
                surface.SetStylusTilt(0f, 0f);
            }
        }

        private void Release(NSSet touches)
        {
            if (!host.TryGetTarget(out var surface))
            {
                return;
            }

            foreach (var touch in touches.Cast<UITouch>())
            {
                if (touch.Type != UITouchType.Stylus)
                {
                    continue;
                }

                surface.EndStylusContact(ContactId(touch));
                surface.SetStylusTilt(0f, 0f);
                surface.SetStylusPressure(0f);
            }
        }

        /// <summary>
        /// How hard the pencil is pressed, as a share of the hardest press it can report, which is what
        /// the canvas draws its line width from.
        /// <para>
        /// Zero means the press is unknown rather than absent, and the canvas then leaves the stroke at
        /// the width the ladder says. That is what a pencil without a force sensor produces - Apple
        /// Pencil (USB-C) has none, while the first and second generation and the Pro do - and it is
        /// also what a device that reports no <c>MaximumPossibleForce</c> at all would divide by.
        /// </para>
        /// </summary>
        private static float Pressure(UITouch touch)
        {
            var maximum = (float)touch.MaximumPossibleForce;
            return maximum > 0f ? Math.Clamp((float)touch.Force / maximum, 0f, 1f) : 0f;
        }

        /// <summary>
        /// The id SkiaSharp gives this contact, which is the touch's own native handle - so the two
        /// recognisers are talking about the same contact without having to share anything.
        /// </summary>
        private static long ContactId(UITouch touch) => ((nint)touch.Handle).ToInt64();

        private bool IsEnabled() =>
            bool.TryParse(preferences?.Get(PreferenceKeys.TiltDetectionEnabled, "false"), out var enabled) && enabled;
    }
}
#endif
