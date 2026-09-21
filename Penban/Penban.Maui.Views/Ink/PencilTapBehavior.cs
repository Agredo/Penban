#if IOS
using Penban.Services.Abstractions;
using UIKit;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Switches between pen and eraser on an Apple Pencil double-tap.
/// <para>
/// MAUI has no API for this, so it is done with iOS' own <c>UIPencilInteraction</c> (iOS 12.1+,
/// Apple Pencil 2 / Pencil Pro). The action the user picked for the double-tap in the iOS
/// settings is respected rather than always switching to the eraser - Apple expects apps to honour
/// it, and a user who set it to "show palette" should not get an eraser.
/// </para>
/// <para>
/// iOS only: Android (S Pen button) exposes no equivalent event, and the simulator never raises a
/// pencil tap, so this needs a real device to test. The Windows counterpart, which erases while the
/// eraser end of a Surface Pen is held down, is <see cref="PenTailEraserBehavior"/>.
/// </para>
/// </summary>
public class PencilTapBehavior : PlatformBehavior<View, UIView>
{
    private readonly IPreferences? preferences;
    private UIPencilInteraction? interaction;

    /// <summary>
    /// <paramref name="preferences"/> is checked on every tap instead of once, so turning the
    /// setting off takes effect on the next tap without reopening the card.
    /// </summary>
    public PencilTapBehavior(IPreferences? preferences = null)
    {
        this.preferences = preferences;
    }

    protected override void OnAttachedTo(View view, UIView platformView)
    {
        base.OnAttachedTo(view, platformView);

        if (!OperatingSystem.IsIOSVersionAtLeast(12, 1))
        {
            return;
        }

        interaction = new UIPencilInteraction { Delegate = new PencilTapHandler(view, preferences) };
        platformView.AddInteraction(interaction);
    }

    protected override void OnDetachedFrom(View view, UIView platformView)
    {
        base.OnDetachedFrom(view, platformView);

        if (interaction is null)
        {
            return;
        }

        platformView.RemoveInteraction(interaction);
        interaction.Dispose();
        interaction = null;
    }

    private sealed class PencilTapHandler : UIPencilInteractionDelegate
    {
        // The behaviour outlives nothing here, but the handler is owned by UIKit, so holding the
        // view weakly keeps a detached card from being kept alive by its own interaction.
        private readonly WeakReference<View> owner;
        private readonly IPreferences? preferences;

        public PencilTapHandler(View owner, IPreferences? preferences)
        {
            this.owner = new WeakReference<View>(owner);
            this.preferences = preferences;
        }

        public override void DidTap(UIPencilInteraction interaction)
        {
            if (UIPencilInteraction.PreferredTapAction != UIPencilPreferredAction.SwitchEraser
                || !IsEnabled(preferences)
                || !owner.TryGetTarget(out var view)
                || view is not InkCanvasHostView host)
            {
                return;
            }

            host.IsEraserMode = !host.IsEraserMode;
        }

        private static bool IsEnabled(IPreferences? preferences) =>
            !bool.TryParse(preferences?.Get(PreferenceKeys.PencilDoubleTapEnabled, "true"), out var enabled) || enabled;
    }
}
#endif
