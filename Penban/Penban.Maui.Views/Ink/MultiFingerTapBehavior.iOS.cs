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
    }

    private void AddRecognizer(UIView platformView, InkCanvasHostView host, int fingers, string preferenceKey, bool redo)
    {
        var handler = new TapHandler(host, preferenceKey, redo);
        var recognizer = new UITapGestureRecognizer(handler, new Selector("HandleTap:"))
        {
            NumberOfTouchesRequired = (nuint)fingers,

            // The canvas keeps handling the touches; the tap is only observed.
            CancelsTouchesInView = false,
        };

        platformView.AddGestureRecognizer(recognizer);
        recognizers.Add(recognizer);
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
