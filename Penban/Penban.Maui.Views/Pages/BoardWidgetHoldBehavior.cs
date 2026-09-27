#if IOS
using Foundation;
using ObjCRuntime;
using Penban.ViewModels;
using UIKit;
#endif

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Holding a board card asks for a home screen widget showing that board.
/// <para>
/// MAUI's own long press - the context flyout - is drawn where the platform expects a context menu,
/// on a collection or table row, and a card in this list is neither: a hold on it stayed silent on
/// iOS however long it was held. The hold is therefore read from the card itself. iOS places a widget
/// for the person using the phone and offers an app no way to do it, so the gesture can only note
/// which board is wanted and explain the rest of the way - see
/// <see cref="BoardsPage.RequestWidgetForBoard"/>.
/// </para>
/// <para>
/// Widgets are an iOS feature, so on the other platforms this behavior has nothing to attach to and
/// stays quiet; it exists there only so the cards can name it in markup.
/// </para>
/// </summary>
public class BoardWidgetHoldBehavior : Behavior<View>
{
    /// <summary>
    /// How long a finger has to rest on a card to count as a hold. Short enough to feel like the hold
    /// the system uses, long enough that a tap cannot reach it.
    /// </summary>
    private const double HoldSeconds = 0.5;

#if IOS
    private WeakReference<View>? card;
    private UIView? platformView;
    private UILongPressGestureRecognizer? recognizer;
    private BoardHoldHandler? handler;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);

        card = new WeakReference<View>(bindable);

        // A card in a list is built without a platform view behind it, and the list rebuilds the
        // view whenever it scrolls, so the recogniser is put on whenever one is there.
        bindable.HandlerChanged += OnHandlerChanged;
        Attach(bindable);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);

        bindable.HandlerChanged -= OnHandlerChanged;
        Detach();

        card = null;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        Detach();

        if (sender is View view)
        {
            Attach(view);
        }
    }

    private void Attach(View view)
    {
        if (view.Handler?.PlatformView is not UIView host)
        {
            return;
        }

        platformView = host;
        handler = new BoardHoldHandler(this);
        recognizer = new UILongPressGestureRecognizer(handler, new Selector("HandleHold:"))
        {
            MinimumPressDuration = HoldSeconds,

            // The hold takes the card's touch away the moment it is accepted: the finger that came
            // down to hold would otherwise also be read as the tap that opens the board, and the
            // explanation for the widget would open with the board behind it.
            CancelsTouchesInView = true,
        };

        host.AddGestureRecognizer(recognizer);
    }

    private void Detach()
    {
        if (platformView is not null && recognizer is not null)
        {
            platformView.RemoveGestureRecognizer(recognizer);
            recognizer.Dispose();
        }

        platformView = null;
        recognizer = null;

        // UIKit holds the target of a recogniser weakly, so it has to be kept alive here.
        handler = null;
    }

    /// <summary>
    /// Reports the hold of the card this behavior sits on.
    /// </summary>
    private void HoldAccepted()
    {
        if (card?.TryGetTarget(out var view) is not true || view.BindingContext is not BoardViewModel board)
        {
            return;
        }

        // The card knows the page only through its parents, so the request travels up the tree
        // instead of being wired up when the page is built.
        for (Element? parent = view; parent is not null; parent = parent.Parent)
        {
            if (parent is BoardsPage page)
            {
                page.RequestWidgetForBoard(board);
                return;
            }
        }
    }

    /// <summary>
    /// The hold itself. Owned by UIKit through the recogniser, so the behavior is held weakly to keep
    /// a closed list from being kept alive by its own recognisers.
    /// </summary>
    private sealed class BoardHoldHandler : NSObject
    {
        private readonly WeakReference<BoardWidgetHoldBehavior> behavior;

        public BoardHoldHandler(BoardWidgetHoldBehavior behavior) => this.behavior = new WeakReference<BoardWidgetHoldBehavior>(behavior);

        [Export("HandleHold:")]
        public void HandleHold(UILongPressGestureRecognizer recognizer)
        {
            // UIKit reports one gesture from the finger going down to the finger coming up, and only
            // the moment the hold was accepted means anything here.
            if (recognizer.State == UIGestureRecognizerState.Began && behavior.TryGetTarget(out var owner))
            {
                owner.HoldAccepted();
            }
        }
    }
#endif
}
