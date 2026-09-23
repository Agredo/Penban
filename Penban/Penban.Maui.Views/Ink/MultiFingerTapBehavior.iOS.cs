#if IOS
using Foundation;
using ObjCRuntime;
using Penban.Services.Abstractions;
using UIKit;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Undoes on a two-finger tap and redoes on a three-finger tap, so a mistake can be taken back
/// without putting the pencil down and reaching for the toolbar. Both taps can be switched off in
/// the settings page.
/// <para>
/// Two tap recognisers, one per finger count, both set not to cancel the touches they watch. The
/// stroke the first finger of such a tap leaves behind is thrown away by the canvas itself: it drops
/// the stroke it is currently drawing as soon as a second finger joins in (see
/// <c>SkiaInkCanvasView</c>).
/// </para>
/// <para>
/// The Windows counterpart is <c>MultiFingerTapBehavior</c> (WinUI pointer events) and the
/// Android one lives in <c>AndroidInkInput</c> (the activity's motion events).
/// </para>
/// </summary>
public class MultiFingerTapBehavior : PlatformBehavior<View, UIView>
{
    private readonly List<UITapGestureRecognizer> recognizers = [];
    private readonly List<TapHandler> handlers = [];
    private readonly SimultaneousRecognizerDelegate recognizerDelegate = new();

    protected override void OnAttachedTo(View view, UIView platformView)
    {
        base.OnAttachedTo(view, platformView);

        if (view is not InkCanvasHostView host)
        {
            return;
        }

        AddRecognizer(platformView, host, fingers: 2, PreferenceKeys.TwoFingerTapUndoEnabled, redo: false);
        AddRecognizer(platformView, host, fingers: 3, PreferenceKeys.ThreeFingerTapRedoEnabled, redo: true);
    }

    protected override void OnDetachedFrom(View view, UIView platformView)
    {
        base.OnDetachedFrom(view, platformView);

        foreach (var recognizer in recognizers)
        {
            platformView.RemoveGestureRecognizer(recognizer);
            recognizer.Dispose();
        }

        recognizers.Clear();

        // UIKit holds the target of a recogniser weakly, so it has to be kept alive here.
        handlers.Clear();
    }

    private void AddRecognizer(UIView platformView, InkCanvasHostView host, int fingers, string preferenceKey, bool redo)
    {
        var handler = new TapHandler(host, preferenceKey, redo);
        handlers.Add(handler);
        var recognizer = new ObservedTapRecognizer(handler, new Selector("HandleTap:"))
        {
            NumberOfTouchesRequired = (nuint)fingers,

            // The canvas keeps handling the touches; the tap is only observed, and neither the
            // canvas nor the tap may cancel the other.
            CancelsTouchesInView = false,
            Delegate = recognizerDelegate,
        };

        platformView.AddGestureRecognizer(recognizer);
        recognizers.Add(recognizer);
    }

    /// <summary>
    /// Keeps the tap out of the canvas' way and the canvas out of the tap's way. Skia's touch
    /// handler sits on the drawing surface below the host and recognises as soon as a finger lands;
    /// without this the tap would be cancelled by it and would never fire.
    /// </summary>
    private sealed class SimultaneousRecognizerDelegate : UIGestureRecognizerDelegate
    {
        public override bool ShouldRecognizeSimultaneously(UIGestureRecognizer gestureRecognizer, UIGestureRecognizer otherGestureRecognizer) => true;

        public override bool ShouldRequireFailureOf(UIGestureRecognizer gestureRecognizer, UIGestureRecognizer otherGestureRecognizer) => false;

        public override bool ShouldBeRequiredToFailBy(UIGestureRecognizer gestureRecognizer, UIGestureRecognizer otherGestureRecognizer) => false;
    }

    /// <summary>
    /// A tap that never takes a touch away from anybody and never lets anybody take its own touches
    /// away, so the canvas and the tap both see the same fingers.
    /// </summary>
    private sealed class ObservedTapRecognizer : UITapGestureRecognizer
    {
        public ObservedTapRecognizer(NSObject target, Selector action)
            : base(target, action)
        {
        }

        public override bool CanPreventGestureRecognizer(UIGestureRecognizer preventedGestureRecognizer) => false;

        public override bool CanBePreventedByGestureRecognizer(UIGestureRecognizer preventingGestureRecognizer) => false;
    }

    /// <summary>
    /// The tap itself. Owned by UIKit through the recogniser, so the surface is held weakly to keep a
    /// closed card from being kept alive by its own recogniser.
    /// </summary>
    private sealed class TapHandler : NSObject
    {
        private readonly WeakReference<InkCanvasHostView> host;
        private readonly string preferenceKey;
        private readonly bool redo;

        public TapHandler(InkCanvasHostView host, string preferenceKey, bool redo)
        {
            this.host = new WeakReference<InkCanvasHostView>(host);
            this.preferenceKey = preferenceKey;
            this.redo = redo;
        }

        [Export("HandleTap:")]
        public void HandleTap(UITapGestureRecognizer recognizer)
        {
            if (!host.TryGetTarget(out var surface))
            {
                return;
            }

            // Read at the moment of the tap, so the settings page takes effect right away.
            if (bool.TryParse(surface.Preferences?.Get(preferenceKey, bool.TrueString), out var enabled) && !enabled)
            {
                return;
            }

            if (redo)
            {
                surface.Redo();
            }
            else
            {
                surface.Undo();
            }
        }
    }
}
#endif
