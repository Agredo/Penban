#if IOS
using Penban.Services.Abstractions;
using UIKit;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Tells the note what the user asked of the pencil itself: a double-tap, and on a Pencil Pro a
/// squeeze, change the tool the way the iOS settings for those two say they should.
/// <para>
/// MAUI has no API for this, so it is done with iOS' own <c>UIPencilInteraction</c> (iOS 12.1+, Apple
/// Pencil 2 / Pencil Pro). What the user picked for the double-tap - and on a Pencil Pro for the
/// squeeze - in the iOS settings is respected rather than always switching to the eraser: Apple
/// expects apps to honour it, and a user who set it to "show palette" should not get an eraser.
/// "Previous tool" is the one the pencil then reaches the lasso through, once it has been picked with
/// the lasso button.
/// </para>
/// <para>
/// iOS only: Android (S Pen button) exposes no equivalent event, and the simulator never raises a
/// pencil tap, so this needs a real device to test. The Windows counterparts, which erase while the
/// eraser end of a Surface Pen is held against the surface and pick strokes up while its button is
/// held, are <see cref="PenTailEraserBehavior"/> and <see cref="PenButtonLassoBehavior"/>.
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

        /// <summary>
        /// The tool in use before the one in use now, for the "previous tool" setting - the way the
        /// pencil gets to the lasso, which no gesture of its own selects.
        /// </summary>
        private InkTool previousTool = InkTool.Pen;

        public PencilTapHandler(View owner, IPreferences? preferences)
        {
            this.owner = new WeakReference<View>(owner);
            this.preferences = preferences;
        }

        public override void DidTap(UIPencilInteraction interaction) =>
            Change(UIPencilInteraction.PreferredTapAction);

        /// <summary>
        /// The double-tap as iOS 17.5 and later reports it, where <see cref="DidTap"/> is deprecated.
        /// Both are implemented because the app still runs on iOS 15, which only knows the old one -
        /// iOS calls whichever belongs to it.
        /// </summary>
        public override void DidReceiveTap(UIPencilInteraction interaction, UIPencilInteractionTap tap) =>
            Change(UIPencilInteraction.PreferredTapAction);

        /// <summary>
        /// A squeeze, which a Pencil Pro reports several times while it is being squeezed, so only its
        /// first report changes anything - hence the phase check.
        /// </summary>
        public override void DidReceiveSqueeze(UIPencilInteraction interaction, UIPencilInteractionSqueeze squeeze)
        {
            // Both the squeeze and what the user asked of it are iOS 17.5, which the app is not
            // limited to.
            var squeezed = OperatingSystem.IsIOSVersionAtLeast(17, 5) || OperatingSystem.IsMacCatalystVersionAtLeast(17, 5);
            if (!squeezed || squeeze.Phase != UIPencilInteractionPhase.Began)
            {
                return;
            }

            Change(UIPencilInteraction.PreferredSqueezeAction);
        }

        private void Change(UIPencilPreferredAction action)
        {
            if (!IsEnabled(preferences)
                || !owner.TryGetTarget(out var view)
                || view is not InkCanvasHostView host)
            {
                return;
            }

            var current = host.Tool;
            var wanted = action switch
            {
                UIPencilPreferredAction.SwitchPrevious => previousTool,

                // The eraser is always what a press of the pencil lands on, whichever of the three
                // tools is in use, so a lasso that is being held stays reachable by hand.
                UIPencilPreferredAction.SwitchEraser => current == InkTool.Eraser ? InkTool.Pen : InkTool.Eraser,

                // The palette and the shortcuts the user can pick instead ask for something this note
                // has no room for; every other value means the pencil does nothing.
                _ => current,
            };

            if (wanted == current)
            {
                return;
            }

            previousTool = current;
            host.Tool = wanted;
        }

        private static bool IsEnabled(IPreferences? preferences) =>
            !bool.TryParse(preferences?.Get(PreferenceKeys.PencilDoubleTapEnabled, "true"), out var enabled) || enabled;
    }
}
#endif
