#if WINDOWS
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Penban.Services.Abstractions;
using Windows.Foundation;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Undoes with a two-finger tap and redoes with a three-finger tap, so a mistake can be taken back
/// without putting the pen down and reaching for the toolbar. Both taps can be switched off in the
/// settings page.
/// <para>
/// MAUI has no gesture for this, so the contacts are counted on the platform element instead. The
/// tap only counts when the fingers land together, stay still and are gone again quickly; a pen or
/// mouse that is down at the same time is left alone. The stray stroke the first finger would
/// otherwise leave behind is removed by the canvas itself - see
/// <see cref="SkiaInkCanvasView.TrackMultiTouch"/>.
/// </para>
/// </summary>
public class MultiFingerTapBehavior : PlatformBehavior<View, FrameworkElement>
{
    /// <summary>Longer than this and the contact is a drag or a rest, not a tap.</summary>
    private const int MaxTapMilliseconds = 600;

    /// <summary>
    /// How long the fingers of one tap may take to all arrive. Fingers that follow each other more
    /// slowly are a hand being put down, not a tap.
    /// </summary>
    private const int MaxFingerSpreadMilliseconds = 250;

    /// <summary>How far a finger may wander and still count as a tap, in device-independent units.</summary>
    private const double MaxTapMovement = 16;

    private readonly PointerEventHandler onPointerPressed;
    private readonly PointerEventHandler onPointerMoved;
    private readonly PointerEventHandler onPointerReleased;
    private readonly PointerEventHandler onPointerCanceled;

    private readonly Dictionary<uint, Windows.Foundation.Point> pressedAt = new();
    private InkCanvasHostView? host;
    private long firstPressTicks;
    private int maxFingers;
    private bool hasMoved;
    private bool isStaggered;

    public MultiFingerTapBehavior()
    {
        onPointerPressed = OnPointerPressed;
        onPointerMoved = OnPointerMoved;
        onPointerReleased = OnPointerReleased;
        onPointerCanceled = OnPointerCanceled;
    }

    protected override void OnAttachedTo(View view, FrameworkElement platformView)
    {
        base.OnAttachedTo(view, platformView);

        host = view as InkCanvasHostView;

        platformView.AddHandler(UIElement.PointerPressedEvent, onPointerPressed, true);
        platformView.AddHandler(UIElement.PointerMovedEvent, onPointerMoved, true);
        platformView.AddHandler(UIElement.PointerReleasedEvent, onPointerReleased, true);
        platformView.AddHandler(UIElement.PointerCanceledEvent, onPointerCanceled, true);
        platformView.AddHandler(UIElement.PointerCaptureLostEvent, onPointerCanceled, true);
    }

    protected override void OnDetachedFrom(View view, FrameworkElement platformView)
    {
        base.OnDetachedFrom(view, platformView);

        platformView.RemoveHandler(UIElement.PointerPressedEvent, onPointerPressed);
        platformView.RemoveHandler(UIElement.PointerMovedEvent, onPointerMoved);
        platformView.RemoveHandler(UIElement.PointerReleasedEvent, onPointerReleased);
        platformView.RemoveHandler(UIElement.PointerCanceledEvent, onPointerCanceled);
        platformView.RemoveHandler(UIElement.PointerCaptureLostEvent, onPointerCanceled);

        host = null;
        Forget();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element || !IsFinger(e))
        {
            return;
        }

        var now = Environment.TickCount64;
        if (pressedAt.Count == 0)
        {
            firstPressTicks = now;
            maxFingers = 0;
            hasMoved = false;
            isStaggered = false;
        }
        else if (now - firstPressTicks > MaxFingerSpreadMilliseconds)
        {
            isStaggered = true;
        }

        pressedAt[e.Pointer.PointerId] = e.GetCurrentPoint(element).Position;

        // The fingers leave one after the other, so the count has to be remembered here: by the
        // time the last one is lifted, only one contact is still down.
        maxFingers = Math.Max(maxFingers, pressedAt.Count);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element
            || !IsFinger(e)
            || !pressedAt.TryGetValue(e.Pointer.PointerId, out var start))
        {
            return;
        }

        var current = e.GetCurrentPoint(element).Position;
        if (Math.Abs(current.X - start.X) > MaxTapMovement || Math.Abs(current.Y - start.Y) > MaxTapMovement)
        {
            hasMoved = true;
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!IsFinger(e))
        {
            return;
        }

        pressedAt.Remove(e.Pointer.PointerId);

        if (pressedAt.Count > 0 || host is null)
        {
            // Still waiting for the other fingers of the same tap.
            return;
        }

        var fingers = maxFingers;
        var elapsed = Environment.TickCount64 - firstPressTicks;
        var isTap = !hasMoved && !isStaggered && elapsed <= MaxTapMilliseconds;
        Forget();

        if (!isTap)
        {
            return;
        }

        if (fingers == 2)
        {
            if (IsEnabled(host, PreferenceKeys.TwoFingerTapUndoEnabled))
            {
                host.Undo();
            }
        }
        else if (fingers >= 3 && IsEnabled(host, PreferenceKeys.ThreeFingerTapRedoEnabled))
        {
            host.Redo();
        }
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (IsFinger(e))
        {
            pressedAt.Remove(e.Pointer.PointerId);
        }
    }

    private static bool IsFinger(PointerRoutedEventArgs e) =>
        e.Pointer.PointerDeviceType == PointerDeviceType.Touch;

    /// <summary>
    /// Reads the gesture switch at the moment of the tap, so flipping it in the settings page takes
    /// effect without the editor having to be reopened.
    /// </summary>
    private static bool IsEnabled(InkCanvasHostView host, string key) =>
        !bool.TryParse(host.Preferences?.Get(key, bool.TrueString), out var enabled) || enabled;

    private void Forget()
    {
        pressedAt.Clear();
        maxFingers = 0;
        hasMoved = false;
        isStaggered = false;
    }
}
#endif
