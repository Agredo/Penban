#if ANDROID
using Android.Views;
using Microsoft.Maui.Devices;
using Penban.Services.Abstractions;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Reads the pen's tilt and the two- and three-finger taps out of Android's touch stream and hands
/// them to the ink surface that is on screen.
/// <para>
/// Both are part of the touch events themselves, but the surface that has to be told about them is a
/// SkiaSharp view with a touch listener of its own, and replacing that listener would take the ink
/// input away from it. The activity sees every touch before it is dispatched instead (see
/// <c>MainActivity.DispatchTouchEvent</c>), so the values are read there and passed on here.
/// </para>
/// <para>
/// Only one ink surface exists at a time; it is held weakly and dropped when its handler goes away,
/// so a closed editor cannot be woken up by a tap on another page. The tilt setting is re-read on
/// every contact, so turning it off takes effect on the next stroke. Android only - the Windows and
/// iOS counterparts are <c>PenTiltBehavior</c> and <c>MultiFingerTapBehavior</c>.
/// </para>
/// </summary>
public static class AndroidInkInput
{
    /// <summary>Longer than this and the contact was a drag or a resting hand, not a tap.</summary>
    private const long MaxTapMilliseconds = 600;

    /// <summary>How far a finger may wander and still count as a tap, in dp.</summary>
    private const float MaxTapMovementDp = 12f;

    /// <summary>Most fingers a tap may have. A whole hand on the screen is not a tap.</summary>
    private const int MaxTapPointers = 4;

    private static readonly Dictionary<int, (float X, float Y)> fingerStarts = [];

    private static WeakReference<InkCanvasHostView>? surface;
    private static long startTime;
    private static int maxPointers;
    private static bool moved;
    private static bool tracking;

    /// <summary>Points at the surface that is currently on screen - see <see cref="InkCanvasHostView"/>.</summary>
    internal static void Attach(InkCanvasHostView host) => surface = new WeakReference<InkCanvasHostView>(host);

    /// <summary>Drops the surface again once it is gone, so nothing is left pointing at it.</summary>
    internal static void Detach() => surface = null;

    /// <summary>
    /// Reads one touch. Called for every touch in the app before the view it belongs to handles it.
    /// </summary>
    public static void Publish(MotionEvent? e)
    {
        if (e is null || surface is null || !surface.TryGetTarget(out var host))
        {
            return;
        }

        PublishTilt(e, host);
        TrackTap(e, host);
    }

    /// <summary>
    /// Hands over how far the pen leans and which way. A finger reports no tilt, so the values are
    /// cleared then - otherwise the next pen stroke would start with somebody else's lean.
    /// </summary>
    private static void PublishTilt(MotionEvent e, InkCanvasHostView host)
    {
        if (e.GetToolType(0) != MotionEventToolType.Stylus || !IsTiltEnabled(host))
        {
            host.SetStylusTilt(0f, 0f);
            return;
        }

        // AXIS_TILT is the angle between the pen and the screen - 0 upright, π/2 flat - which is
        // exactly what the canvas stores. AXIS_ORIENTATION is measured clockwise from the vertical
        // (0 means the pen leans towards the top of the screen, π/2 towards the right), while the
        // canvas measures the direction of the lean from the positive x-axis, so the quarter turn
        // between the two is taken off.
        var tilt = e.GetAxisValue(Axis.Tilt);
        var orientation = e.GetAxisValue(Axis.Orientation);
        host.SetStylusTilt(tilt, orientation - (MathF.PI / 2f));
    }

    /// <summary>
    /// Follows a contact from the first finger down to the last one up and, if it turns out to have
    /// been a short, still tap, takes back or puts back the last edit.
    /// </summary>
    private static void TrackTap(MotionEvent e, InkCanvasHostView host)
    {
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                Begin(e);
                break;

            case MotionEventActions.PointerDown:
                AddFinger(e);
                break;

            case MotionEventActions.Move:
                WatchMovement(e);
                break;

            case MotionEventActions.PointerUp:
                if (tracking)
                {
                    fingerStarts.Remove(e.GetPointerId(e.ActionIndex));
                }

                break;

            case MotionEventActions.Up:
                FinishTap(e, host);
                break;

            case MotionEventActions.Cancel:
                Reset();
                break;
        }
    }

    private static void Begin(MotionEvent e)
    {
        Reset();

        // A pen, the mouse or more than a handful of fingers: not something that should undo
        // anything, and the fingers' own starting points have to be known to measure the movement.
        if (e.PointerCount > MaxTapPointers || e.GetToolType(0) != MotionEventToolType.Finger)
        {
            return;
        }

        tracking = true;
        startTime = e.EventTime;
        AddFinger(e);
    }

    private static void AddFinger(MotionEvent e)
    {
        if (!tracking)
        {
            return;
        }

        maxPointers = Math.Max(maxPointers, e.PointerCount);
        if (maxPointers > MaxTapPointers)
        {
            Reset();
            return;
        }

        for (var index = 0; index < e.PointerCount; index++)
        {
            // The pen joining the fingers turns the contact into a drawing gesture, not a tap.
            if (e.GetToolType(index) != MotionEventToolType.Finger)
            {
                Reset();
                return;
            }

            fingerStarts[e.GetPointerId(index)] = (e.GetX(index), e.GetY(index));
        }
    }

    private static void WatchMovement(MotionEvent e)
    {
        if (!tracking || moved)
        {
            return;
        }

        var limit = MaxTapMovementDp * (float)DeviceDisplay.MainDisplayInfo.Density;
        for (var index = 0; index < e.PointerCount; index++)
        {
            if (!fingerStarts.TryGetValue(e.GetPointerId(index), out var start))
            {
                continue;
            }

            var dx = e.GetX(index) - start.X;
            var dy = e.GetY(index) - start.Y;
            if ((dx * dx) + (dy * dy) > limit * limit)
            {
                moved = true;
                return;
            }
        }
    }

    private static void FinishTap(MotionEvent e, InkCanvasHostView host)
    {
        var fingers = maxPointers;
        var isTap = tracking && !moved && e.EventTime - startTime <= MaxTapMilliseconds;
        Reset();

        if (!isTap)
        {
            return;
        }

        // Two fingers take back the last edit, three put it back - the same pair of gestures as on
        // Windows and iOS. Both switches are read at the moment of the tap, so the settings page
        // takes effect without the editor having to be reopened.
        if (fingers == 2)
        {
            if (IsEnabled(host, PreferenceKeys.TwoFingerTapUndoEnabled, true))
            {
                host.Undo();
            }
        }
        else if (fingers >= 3 && IsEnabled(host, PreferenceKeys.ThreeFingerTapRedoEnabled, true))
        {
            host.Redo();
        }
    }

    private static void Reset()
    {
        tracking = false;
        moved = false;
        maxPointers = 0;
        fingerStarts.Clear();
    }

    private static bool IsTiltEnabled(InkCanvasHostView host) =>
        IsEnabled(host, PreferenceKeys.TiltDetectionEnabled, false);

    private static bool IsEnabled(InkCanvasHostView host, string key, bool @default) =>
        bool.TryParse(host.Preferences?.Get(key, @default.ToString()), out var enabled) ? enabled : @default;
}
#endif
