using Penban.Models;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Penban.Maui.Views.Ink;

/// <summary>
/// Ink renderer backed by a raw <see cref="SKCanvasView"/>. Handles pointer/pen/touch
/// input manually via <see cref="SKCanvasView.Touch"/> so stylus pressure (reported through
/// <see cref="SKTouchEventArgs.Pressure"/> on platforms that support it) is preserved in the
/// stored <see cref="InkStroke"/> data.
/// <para>
/// This is the renderer that stores its coordinates in <see cref="InkDocument"/> space: the surface
/// pixels a touch arrives in are converted on the way in, and the document is scaled onto the surface
/// on the way out. A stroke therefore lands in the same place on the note and in the board preview.
/// </para>
/// </summary>
public class SkiaInkCanvasView : ContentView, IInkCanvasView
{
    /// <summary>Stroke width in document units (0.4% of the note side).</summary>
    private const float DefaultStrokeThickness = 4f;

    /// <summary>
    /// How far from the touch point a stroke is erased, in document units. Expressed in the document
    /// rather than in pixels so the eraser covers the same part of the note on every device.
    /// </summary>
    private const float EraseRadius = 14f;

    private const float HalfPi = MathF.PI / 2f;

    /// <summary>Tilt below this (about one degree) counts as "pen held upright".</summary>
    private const float MinimumTilt = 0.02f;

    /// <summary>How much wider the nib gets when the pen is laid flat against the surface.</summary>
    private const float NibWidening = 2.5f;

    /// <summary>Upper bound on nib stamps per segment, to keep long strokes cheap to redraw.</summary>
    private const int MaxNibStampsPerSegment = 12;

    /// <summary>
    /// How far a swipe may drift sideways before it counts as something else (a pan, a pinch) and is
    /// given up on.
    /// </summary>
    private const float MaxSwipeSkew = 0.6f;

    /// <summary>
    /// How far a finger has to travel before the swipe it started is judged at all, in document
    /// units. Below that the finger is still settling, and a pixel of noise to the side would give
    /// the swipe up before it had started.
    /// </summary>
    private const float SwipeSlop = 12f;

    /// <summary>
    /// How far a finger may travel and still count as the tap that asks for the menu, in document
    /// units. The same distance the swipe is judged at, because it answers the same question: has this
    /// finger moved, or has it only been put down and taken up again?
    /// </summary>
    private const float MenuTapSlop = SwipeSlop;

    private readonly SKCanvasView canvasView;
    private readonly InkCommandStack commands;

    /// <summary>Ids of the fingers currently down on the surface.</summary>
    private readonly HashSet<long> activeFingers = new();

    /// <summary>
    /// Ids of the contacts the platform has named as the pen - see <see cref="SetStylusContact"/>.
    /// Stays empty on every platform whose own touch events already tell a pen from a finger.
    /// </summary>
    private readonly HashSet<long> stylusContacts = new();

    /// <summary>
    /// The press of a contact the platform has not named yet, kept until it does: a pen that is
    /// confirmed a moment after it touched down still starts its stroke from that very press.
    /// </summary>
    private readonly Dictionary<long, SKTouchEventArgs> unnamedPresses = new();

    private InkStroke? currentStroke;
    private float penTilt;
    private float penAzimuth;

    /// <summary>Whether the stroke being drawn came from a finger rather than from a pen or mouse.</summary>
    private bool currentStrokeIsFinger;

    /// <summary>The contact the stroke being drawn belongs to, for as long as there is one.</summary>
    private long currentStrokeContactId;

    /// <summary>
    /// How many pens or mice are down, so a resting hand cannot interrupt one of them. Not counted
    /// where the platform names the pen instead - see <see cref="PlatformNamesStylusContacts"/> -
    /// because there the named contacts already are that count.
    /// </summary>
    private int activeStylusCount;

    /// <summary>
    /// True from the moment a second finger lands until the last one is lifted again. A two- or
    /// three-finger tap is a gesture (undo/redo) rather than a stroke, but the fingers reach the
    /// canvas before the gesture is recognised, so the drawing has to be held back here.
    /// </summary>
    private bool isMultiTouchGesture;

    /// <summary>
    /// True while the eraser end of a pen is held against the surface. Unlike
    /// <see cref="IsEraserMode"/> this is not a mode that stays on: it lasts exactly as long as the
    /// tail is down, so lifting it goes straight back to writing.
    /// </summary>
    private bool isPenTailErasing;

    /// <summary>
    /// The contacts that are down on a surface that does not draw with them, in document units. Kept
    /// apart from <see cref="activeFingers"/>, which counts the contacts of a drawing surface: here
    /// the finger has no stroke to draw and is followed for the page's own gesture instead - see
    /// <see cref="CloseOnSwipeDown"/>.
    /// </summary>
    private readonly Dictionary<long, SKPoint> fingerContacts = new();

    /// <summary>The finger that started a downward swipe in pen-only mode, if there is one.</summary>
    private long? closeSwipeId;

    /// <summary>Where that finger touched down, in document units.</summary>
    private SKPoint closeSwipeStart;

    /// <summary>Whether that finger is still a candidate for closing the card.</summary>
    private bool isCloseSwipeTracking;

    /// <summary>The finger that may still turn out to be the tap that asks for the menu, if there is one.</summary>
    private long menuTapId;

    /// <summary>Where that finger went down, in document units.</summary>
    private SKPoint menuTapOrigin;

    public SkiaInkCanvasView()
    {
        canvasView = new SKCanvasView { EnableTouchEvents = true };
        canvasView.Touch += OnTouch;
        canvasView.PaintSurface += OnPaintSurface;
        commands = new InkCommandStack(Strokes);
        Content = canvasView;
    }

    public ObservableStrokeCollection Strokes { get; } = new();

    public event EventHandler? StrokeCompleted;

    /// <summary>
    /// Raised while a finger drags the card down, with where that finger is on the note - see
    /// <see cref="CloseSwipeMove"/>. The surface does not decide what that means: how far the finger
    /// has to go before letting go closes the editor is a property of the page the card is drawn on,
    /// so the page is told where the finger is and answers with the drag.
    /// </summary>
    public event EventHandler<CloseSwipeMove>? CloseSwipeMoved;

    /// <summary>
    /// Raised when the finger of that drag is gone again - lifted, joined by a second finger, or
    /// given up on because it turned sideways. The page uses it to put the card back where it was,
    /// or to let it finish leaving.
    /// </summary>
    public event EventHandler? CloseSwipeEnded;

    /// <summary>
    /// Raised when the menu is asked for from the note itself: a finger was tapped on it while only
    /// the pen draws, or the right mouse button was pressed on it - see
    /// <see cref="RadialMenuEnabled"/>. Where the ask came from is in <see cref="InkDocument"/> units,
    /// so the page can put its menu around that very point. The surface opens nothing itself: what
    /// such a menu holds and where it may stand are the page's, and only the page can put it up over
    /// the note.
    /// </summary>
    public event EventHandler<MenuRequest>? MenuRequested;

    public string StrokeColor { get; set; } = "#000000";

    /// <summary>Width of new strokes, in document units.</summary>
    public float StrokeThickness { get; set; } = DefaultStrokeThickness;

    public bool IsEraserMode { get; set; }

    /// <summary>
    /// When false, a finger only scrolls and pans; only a pen or the mouse draws. Useful when the
    /// hand rests on the screen while writing.
    /// </summary>
    public bool AllowFingerDrawing { get; set; } = true;

    /// <summary>
    /// Whether a finger swiped down drags the card out of the editor - see
    /// <see cref="CloseSwipeMoved"/>. Only ever tracks with <see cref="AllowFingerDrawing"/> off: a
    /// finger that draws means the stroke, not the card. It is off by default, because only the page
    /// that can be left by such a swipe knows what the swipe is for.
    /// </summary>
    public bool CloseOnSwipeDown { get; set; }

    /// <summary>
    /// Whether the menu can be asked for from the note itself - see <see cref="MenuRequested"/>. Where
    /// only the pen draws, a finger has nothing else to do there, so a tap of it asks for the menu;
    /// where there is a mouse, the right button does. A surface that draws with the finger is left
    /// alone: there the tap is a stroke. Not a preference, because only a page that has such a menu
    /// can want this, and it is the page that says so.
    /// </summary>
    public bool RadialMenuEnabled { get; set; }

    /// <summary>
    /// Whether the line width follows the pressure reported per point. Off means one width for the
    /// whole stroke, which is also what a device that reports no pressure produces.
    /// </summary>
    public bool PressureSensitiveWidth { get; set; } = true;

    /// <summary>
    /// Whether the pen tilt recorded in <see cref="InkPoint.Tilt"/>/<see cref="InkPoint.Azimuth"/> is
    /// turned into a calligraphy look. Has no visible effect on strokes whose tilt is zero, so a
    /// device that reports no tilt draws exactly as before.
    /// </summary>
    public bool TiltRenderingEffect { get; set; }

    /// <summary>
    /// Tilt and lean direction the stylus currently reports, fed in by the platform input handler.
    /// Both are radians; <c>0</c> means "upright" and "unknown", which is what every input device that
    /// does not report tilt leaves behind.
    /// </summary>
    public void SetStylusTilt(float tilt, float azimuth)
    {
        penTilt = tilt;
        penAzimuth = azimuth;
    }

    /// <summary>
    /// Whether the platform names the contacts that come from the pen, through
    /// <see cref="SetStylusContact"/>. Apple's SkiaSharp backend reports every contact as
    /// <see cref="SKTouchDeviceType.Touch"/> - the device type is the same for the pencil and for a
    /// finger lying on the note - so there the canvas has to be told which contact is which before it
    /// can decide what to do with one. Elsewhere the device type already is that answer, and nothing
    /// is waited for.
    /// </summary>
    public bool PlatformNamesStylusContacts { get; set; }

    /// <summary>
    /// Names the contact with this id as the pen, so the canvas stops reading it as a finger. Called
    /// for every contact the platform recognises as the pen, on each touch it reports.
    /// <para>
    /// The answer can arrive after the press it belongs to, so a stroke that was already started from
    /// that press - or a press that was held back because only a pen draws - is put right here.
    /// </para>
    /// </summary>
    public void SetStylusContact(long contactId)
    {
        stylusContacts.Add(contactId);

        if (activeFingers.Remove(contactId))
        {
            // A pen coming down is not a finger joining in, and a hand resting next to it must not be
            // read as a two-finger tap either.
            isMultiTouchGesture = false;
        }

        if (fingerContacts.Remove(contactId))
        {
            // The contact that was being followed as a finger turns out to be the pen, so it is no
            // candidate for the menu either - a tap of it is the beginning of a word.
            if (menuTapId == contactId)
            {
                menuTapId = 0;
            }

            if (closeSwipeId == contactId)
            {
                // A hand that is writing must not drag the card out of the editor.
                EndCloseSwipe();
            }
        }

        if (currentStroke is not null && currentStrokeContactId == contactId)
        {
            // This contact is drawing already, it just was not known to be the pen yet.
            currentStrokeIsFinger = false;
            unnamedPresses.Remove(contactId);
            return;
        }

        if (!unnamedPresses.Remove(contactId, out var press))
        {
            return;
        }

        // Nothing is being drawn for this contact yet: its press was held back or taken by a gesture,
        // so the stroke starts here instead, where the pen touched down.
        if (IsEraserMode || isPenTailErasing)
        {
            HandleEraseTouch(press);
            return;
        }

        StartStroke(press, isFinger: false);
    }

    /// <summary>
    /// Drops the contact again once the platform reports that it has left the surface, so that the id
    /// it used cannot be mistaken for the pen when the next contact is given it.
    /// <para>
    /// A contact that is still drawing is kept: its own release has to be the last thing that arrives,
    /// and until then the canvas cannot tell the end of the contact from the platform simply getting
    /// its report in first.
    /// </para>
    /// </summary>
    public void EndStylusContact(long contactId)
    {
        if (currentStroke is not null && currentStrokeContactId == contactId)
        {
            return;
        }

        stylusContacts.Remove(contactId);
    }

    public void Clear()
    {
        Strokes.Clear();
        commands.Reset();
        ResetInputState();
        canvasView.InvalidateSurface();
    }

    public bool CanUndo => commands.CanUndo;

    public bool CanRedo => commands.CanRedo;

    public void Undo()
    {
        if (!commands.Undo())
        {
            return;
        }

        canvasView.InvalidateSurface();
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!commands.Redo())
        {
            return;
        }

        canvasView.InvalidateSurface();
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void LoadStrokes(IEnumerable<InkStroke> strokes)
    {
        Strokes.Clear();
        foreach (var stroke in strokes)
        {
            Strokes.Add(stroke);
        }

        // The strokes now in the set were never drawn here, so there is nothing meaningful to undo.
        commands.Reset();
        ResetInputState();
        canvasView.InvalidateSurface();
    }

    /// <summary>
    /// Forgets which contacts are down. Needed whenever the stroke set is replaced underneath a
    /// possibly ongoing touch, so a half-finished stroke cannot write into a set it is no longer in.
    /// </summary>
    private void ResetInputState()
    {
        currentStroke = null;
        currentStrokeIsFinger = false;
        currentStrokeContactId = 0;
        activeFingers.Clear();
        activeStylusCount = 0;
        isMultiTouchGesture = false;

        // A card that was being dragged down goes back to where it rests: the finger that was holding
        // it is gone with the strokes it belonged to.
        fingerContacts.Clear();
        menuTapId = 0;
        EndCloseSwipe();

        // A press that was held back belongs to the stroke set that has just been replaced, so it goes
        // with it. The named contacts are not dropped: those are contacts that are still down, and
        // only the platform's own end report can say otherwise.
        unnamedPresses.Clear();
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        var isStylus = IsStylusContact(e);

        // A drawing surface has nothing else to do with the right mouse button, so on a desktop that
        // is how the menu is asked for: there is no finger to tap with and the pen is drawing.
        if (e.DeviceType == SKTouchDeviceType.Mouse && e.MouseButton == SKMouseButton.Right)
        {
            if (RadialMenuEnabled && e.ActionType == SKTouchAction.Pressed)
            {
                var asked = ToDocument(e.Location);
                MenuRequested?.Invoke(this, new MenuRequest(asked.X, asked.Y));
            }

            e.Handled = true;
            return;
        }

        if (IsLeftToTheScroll(e))
        {
            e.Handled = false;
            return;
        }

        if (!AllowFingerDrawing && !isStylus)
        {
            // Only reached where the platform names the pen, and there "not the pen" is not known yet:
            // Apple reports the pencil as a finger, so a press is only settled once the platform has
            // named it, which can be after the press arrived. The press is kept until then, and the
            // contact stays handled meanwhile, because on Apple a touch that is left unhandled is
            // dropped for good - taking the pencil's whole stroke with it.
            if (PlatformNamesStylusContacts)
            {
                HoldBack(e);
            }

            // A finger that has nothing to draw is followed for the gestures the page wants from it -
            // see CloseOnSwipeDown and RadialMenuEnabled. On Apple a contact that turns out to be the
            // pen is taken out of this again by SetStylusContact, so writing cannot drag the card out.
            TrackFingerGestures(e);
            TrackMenuTap(e);
            e.Handled = true;
            return;
        }

        if (TrackMultiTouch(e))
        {
            if (PlatformNamesStylusContacts && e.ActionType == SKTouchAction.Pressed)
            {
                // A gesture must never swallow the pencil: the press that the first finger of a
                // two-finger tap started is kept, in case the platform names it as the pen later.
                unnamedPresses[e.Id] = e;
            }

            e.Handled = true;
            return;
        }

        if (IsEraserMode || isPenTailErasing)
        {
            HandleEraseTouch(e);
            return;
        }

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                StartStroke(e, isFinger: !isStylus);
                e.Handled = true;
                break;

            case SKTouchAction.Moved:
                if (currentStroke is not null && e.InContact)
                {
                    AddPoint(currentStroke, e);
                    canvasView.InvalidateSurface();
                }

                e.Handled = true;
                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                if (currentStroke is not null)
                {
                    currentStroke = null;
                    currentStrokeContactId = 0;
                    canvasView.InvalidateSurface();
                    StrokeCompleted?.Invoke(this, EventArgs.Empty);
                }

                // The contact is over, so what was learned about it is over too: Apple hands the same
                // id to the next contact, and a stale one would let a finger draw like the pen that
                // used it before.
                stylusContacts.Remove(e.Id);
                unnamedPresses.Remove(e.Id);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Whether this touch comes from the pen. Where the touch already carries a device type that
    /// answers it, that is the answer; where it does not, the platform names the pen instead - see
    /// <see cref="SetStylusContact"/>.
    /// </summary>
    private bool IsStylusContact(SKTouchEventArgs e) =>
        e.DeviceType != SKTouchDeviceType.Touch || stylusContacts.Contains(e.Id);

    /// <summary>Whether a pen or mouse is down right now - see <see cref="activeStylusCount"/>.</summary>
    private bool IsStylusDown => activeStylusCount > 0 || stylusContacts.Count > 0;

    /// <summary>
    /// Whether the touch has to be left to the surrounding scroll/pan instead of being drawn with,
    /// which is what a finger gets while only the pen draws. Where the platform names the pen, the
    /// touch alone cannot answer that, so nothing is left to the scroll there and the canvas waits for
    /// the platform's answer instead - see <see cref="PlatformNamesStylusContacts"/>. A surface whose
    /// page wants the finger for a gesture of its own keeps it as well - see
    /// <see cref="CloseOnSwipeDown"/> and <see cref="RadialMenuEnabled"/> - because a touch that has
    /// been handed to the scroll is gone for good and could never be read as a swipe or a tap.
    /// </summary>
    private bool IsLeftToTheScroll(SKTouchEventArgs e) =>
        !AllowFingerDrawing && !IsStylusContact(e) && !PlatformNamesStylusContacts && !CloseOnSwipeDown
        && !RadialMenuEnabled;

    /// <summary>
    /// Keeps the press of a contact that only draws once it turns out to be the pen, until the
    /// platform has said which it is - see <see cref="PlatformNamesStylusContacts"/>. Everything else
    /// such a contact does is watched and nothing more, so a hand resting on the note leaves nothing
    /// behind.
    /// </summary>
    private void HoldBack(SKTouchEventArgs e)
    {
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                unnamedPresses[e.Id] = e;
                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                unnamedPresses.Remove(e.Id);
                break;
        }
    }

    /// <summary>
    /// Follows the fingers of a surface that does not draw with them, and reads the swipe out of the
    /// editor off them - see <see cref="CloseOnSwipeDown"/>. Nothing else is asked of the finger here:
    /// the page decides what its position means.
    /// <para>
    /// A contact is given up on once the finger is really gone: lifted or cancelled. A finger that
    /// leaves the note while it is still down is still the finger of the gesture that was following
    /// it, and the gesture has to see it arrive below the note to be measured there at all. Only a
    /// contact that is gone without being released is dropped on the way out, which is what is left
    /// of a surface that does not keep the touch.
    /// </para>
    /// </summary>
    private void TrackFingerGestures(SKTouchEventArgs e)
    {
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
            {
                var pressed = ToDocument(e.Location);
                fingerContacts[e.Id] = pressed;
                BeginCloseSwipe(e.Id, pressed);
                break;
            }

            // A finger that was dragged off the note and back is still down, and this surface is the
            // only one that sees it return.
            case SKTouchAction.Entered:
                if (e.InContact)
                {
                    fingerContacts[e.Id] = ToDocument(e.Location);
                }

                break;

            case SKTouchAction.Moved:
                if (e.InContact)
                {
                    var moved = ToDocument(e.Location);
                    fingerContacts[e.Id] = moved;
                    TrackCloseSwipe(e.Id, moved);
                }

                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                ForgetFinger(e.Id);
                break;

            // Leaving the note is not letting go: a finger that is still down has its release ahead
            // of it, and the gesture keeps following it until then.
            case SKTouchAction.Exited:
                if (!e.InContact)
                {
                    ForgetFinger(e.Id);
                }

                break;
        }
    }

    /// <summary>
    /// Reads the tap that asks for the menu off a finger of a surface that does not draw with it - see
    /// <see cref="RadialMenuEnabled"/>. A tap is a press that comes back up where it went down, so the
    /// contact is only remembered here and the question is settled when it is lifted: a finger that
    /// travels is the swipe out of the editor instead, and a second finger is the two-finger tap that
    /// is read as undo elsewhere. Nothing is settled at the press, because on Apple a contact is not
    /// known to be the pen until after it has arrived.
    /// </summary>
    private void TrackMenuTap(SKTouchEventArgs e)
    {
        if (!RadialMenuEnabled || AllowFingerDrawing)
        {
            return;
        }

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                // The only finger down, or no candidate at all: two fingers are a gesture.
                menuTapId = fingerContacts.Count == 1 ? e.Id : 0;
                menuTapOrigin = ToDocument(e.Location);
                break;

            case SKTouchAction.Released:
            {
                var isTap = menuTapId == e.Id && !HasLeftMenuTap(e);
                menuTapId = 0;

                if (isTap)
                {
                    var asked = ToDocument(e.Location);
                    MenuRequested?.Invoke(this, new MenuRequest(asked.X, asked.Y));
                }

                break;
            }

            // A contact that was taken away instead of lifted - or that was dragged off the note and
            // never came back to it - asks for nothing.
            case SKTouchAction.Cancelled:
                if (menuTapId == e.Id)
                {
                    menuTapId = 0;
                }

                break;
        }
    }

    /// <summary>Whether a finger has moved too far from where it went down to still be a tap.</summary>
    private bool HasLeftMenuTap(SKTouchEventArgs e)
    {
        var point = ToDocument(e.Location);
        var dx = point.X - menuTapOrigin.X;
        var dy = point.Y - menuTapOrigin.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) > MenuTapSlop;
    }

    /// <summary>
    /// Gives up on a contact of a surface that does not draw with it - whether it was lifted,
    /// cancelled or merely dragged off the note - and ends the swipe that was following it, so a card
    /// that was being dragged goes back to where it was.
    /// </summary>
    private void ForgetFinger(long id)
    {
        fingerContacts.Remove(id);

        if (closeSwipeId == id)
        {
            EndCloseSwipe();
        }
    }

    /// <summary>
    /// Starts following a finger for the swipe that drags the card out of the editor. Only a single
    /// finger that is the first one down qualifies: a second finger means a gesture (a tap, a pinch),
    /// not a swipe out of the editor.
    /// </summary>
    private void BeginCloseSwipe(long id, SKPoint location)
    {
        if (!CloseOnSwipeDown || fingerContacts.Count > 1)
        {
            EndCloseSwipe();
            return;
        }

        closeSwipeId = id;
        closeSwipeStart = location;
        isCloseSwipeTracking = true;
    }

    /// <summary>
    /// Follows the finger of a downward swipe and reports where it is - see
    /// <see cref="CloseSwipeMoved"/>. Nothing is asked of the card here: the page turns the finger's
    /// position into the drag the finger sees and decides when letting go closes the editor.
    /// <para>
    /// The swipe has to stay roughly vertical, and no pen may be on the surface, so writing with the
    /// pen cannot drag the card away by accident. A swipe that drifts sideways is given up on rather
    /// than followed, which tells the page to put the card back.
    /// </para>
    /// </summary>
    private void TrackCloseSwipe(long id, SKPoint location)
    {
        if (!isCloseSwipeTracking || closeSwipeId != id)
        {
            return;
        }

        if (fingerContacts.Count != 1 || IsStylusDown)
        {
            EndCloseSwipe();
            return;
        }

        var travelX = location.X - closeSwipeStart.X;
        var travelY = location.Y - closeSwipeStart.Y;

        // A finger that has barely moved is still arriving rather than swiping, and reporting it
        // would nudge the card for a touch that was only ever meant to be a touch.
        if (Math.Abs(travelY) <= SwipeSlop)
        {
            return;
        }

        // Judged only once the finger has actually travelled: before that it is still settling, and
        // a pixel of noise to the side would give the swipe up before it started.
        if (Math.Abs(travelX) > Math.Abs(travelY) * MaxSwipeSkew)
        {
            EndCloseSwipe();
            return;
        }

        CloseSwipeMoved?.Invoke(this, new CloseSwipeMove(travelY, closeSwipeStart.Y));
    }

    /// <summary>
    /// Gives up on the finger of a swipe out of the editor and tells the page, so a card that was
    /// being dragged can go back to where it was. Called whenever the finger lifts, a second contact
    /// arrives, or the drag turns out to be something else.
    /// </summary>
    private void EndCloseSwipe()
    {
        if (!isCloseSwipeTracking)
        {
            return;
        }

        isCloseSwipeTracking = false;
        closeSwipeId = null;
        CloseSwipeEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts a stroke from the press of a contact. The contact is remembered with the stroke, so the
    /// stroke is closed by its own release, and so that a later "this is the pen" still lands on the
    /// stroke it belongs to.
    /// </summary>
    private void StartStroke(SKTouchEventArgs e, bool isFinger)
    {
        currentStroke = new InkStroke { Color = StrokeColor, Thickness = StrokeThickness };
        currentStrokeContactId = e.Id;
        currentStrokeIsFinger = isFinger;
        AddPoint(currentStroke, e);
        Strokes.Add(currentStroke);
        commands.RecordAdded(currentStroke);
        canvasView.InvalidateSurface();
    }

    /// <summary>
    /// Follows the contacts on the surface and reports whether this touch belongs to a multi-finger
    /// gesture rather than to a stroke.
    /// <para>
    /// A two- or three-finger tap (undo/redo) is only recognised once it is over, by which time the
    /// fingers have long been reported as drawing input, so the stroke the first finger started has
    /// to be dropped here. A pen or mouse that is down at the same time wins: a resting hand must
    /// never cut a pen stroke short.
    /// </para>
    /// </summary>
    private bool TrackMultiTouch(SKTouchEventArgs e)
    {
        var isFinger = !IsStylusContact(e);

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                if (isFinger)
                {
                    activeFingers.Add(e.Id);
                    if (activeFingers.Count > 1 && !IsStylusDown)
                    {
                        isMultiTouchGesture = true;
                        if (currentStrokeIsFinger)
                        {
                            DropCurrentStroke();
                        }
                    }
                }
                else if (!PlatformNamesStylusContacts)
                {
                    activeStylusCount++;
                }

                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                if (isFinger)
                {
                    activeFingers.Remove(e.Id);
                    if (activeFingers.Count == 0)
                    {
                        isMultiTouchGesture = false;
                    }
                }
                else if (!PlatformNamesStylusContacts)
                {
                    activeStylusCount = Math.Max(activeStylusCount - 1, 0);
                }

                break;
        }

        return isMultiTouchGesture;
    }

    /// <summary>
    /// Throws the stroke that is currently being drawn away again, without a trace and without an
    /// entry in the history - for a touch that turns out not to be a stroke after all.
    /// </summary>
    private void DropCurrentStroke()
    {
        if (currentStroke is null)
        {
            return;
        }

        Strokes.Remove(currentStroke);
        commands.Discard(currentStroke);

        // The press that started it goes with it, so that a stroke dropped by a gesture cannot be
        // brought back to life by a "this is the pen" that arrives after the fact.
        unnamedPresses.Remove(currentStrokeContactId);
        currentStroke = null;
        currentStrokeContactId = 0;
        currentStrokeIsFinger = false;
        canvasView.InvalidateSurface();
    }

    /// <summary>Removes whole strokes that pass under the touch point - simpler and more
    /// forgiving than pixel-level erasing, and easy to reason about for handwritten notes.</summary>
    private void HandleEraseTouch(SKTouchEventArgs e)
    {
        if (IsLeftToTheScroll(e))
        {
            e.Handled = false;
            return;
        }

        if (e.ActionType is not (SKTouchAction.Pressed or SKTouchAction.Moved) || !e.InContact)
        {
            e.Handled = true;
            return;
        }

        var touched = ToDocument(e.Location);
        var erased = EraseStrokesAt(touched.X, touched.Y);

        canvasView.InvalidateSurface();
        e.Handled = true;

        if (erased)
        {
            // Reuse StrokeCompleted so callers persist the change the same way they persist
            // a freshly drawn stroke - erasing is just another edit to the stroke set.
            StrokeCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Flags the eraser end of the pen as being down, until <see cref="EndPenTailErase"/>. The
    /// erasing itself then needs nothing more: the flag routes every following touch to
    /// <see cref="HandleEraseTouch"/>, which converts the touch to document units like any other.
    /// </summary>
    public void BeginPenTailErase()
    {
        isPenTailErasing = true;

        if (currentStroke is null)
        {
            return;
        }

        // The platform reports the tail as an ordinary pen contact, so the press that brought it
        // down has already started a stroke by the time this is called. That stroke is dropped
        // again - the tail must not leave a mark - but its single point is where the tail touched
        // down, and erasing there is what makes a plain tap with the tail work as well as a drag.
        var hasTouched = currentStroke.Points.Count > 0;
        var touched = hasTouched ? currentStroke.Points[0] : default;

        DropCurrentStroke();

        var erased = hasTouched && EraseStrokesAt(touched.X, touched.Y);
        canvasView.InvalidateSurface();

        if (erased)
        {
            StrokeCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Ends the eraser-end contact; the next touch writes again.</summary>
    public void EndPenTailErase() => isPenTailErasing = false;

    /// <summary>
    /// Removes every stroke that passes within <see cref="EraseRadius"/> of the point and records them
    /// with the position they had, so undo can bring them back exactly as they were.
    /// </summary>
    private bool EraseStrokesAt(float x, float y)
    {
        List<InkStrokePlacement>? removed = null;
        for (var i = Strokes.Count - 1; i >= 0; i--)
        {
            var stroke = Strokes[i];
            if (!stroke.Points.Any(p => Distance(p.X, p.Y, x, y) <= EraseRadius))
            {
                continue;
            }

            // The loop runs backwards, so removing the stroke keeps the indices of the strokes that
            // are still to be looked at - and this index is the one the stroke had before the erase.
            removed ??= new List<InkStrokePlacement>();
            removed.Add(new InkStrokePlacement(i, stroke));
            Strokes.RemoveAt(i);
        }

        if (removed is null)
        {
            return false;
        }

        removed.Reverse();
        commands.RecordErased(removed);
        return true;
    }

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// The surface the touch coordinates arrive in. Before the first paint the surface size is not
    /// known yet, so the layout size stands in for it - a very early touch would otherwise be stored
    /// as a coordinate far outside the document.
    /// </summary>
    private SKSize SurfaceSize
    {
        get
        {
            var size = canvasView.CanvasSize;
            if (size.Width > 0 && size.Height > 0)
            {
                return size;
            }

            return new SKSize((float)Math.Max(Width, 1), (float)Math.Max(Height, 1));
        }
    }

    /// <summary>Converts a point on the surface into <see cref="InkDocument"/> units.</summary>
    private SKPoint ToDocument(SKPoint location)
    {
        var size = SurfaceSize;
        return new SKPoint(
            location.X / size.Width * InkDocument.Size,
            location.Y / size.Height * InkDocument.Size);
    }

    private void AddPoint(InkStroke stroke, SKTouchEventArgs e)
    {
        var point = ToDocument(e.Location);
        stroke.Points.Add(new InkPoint
        {
            X = point.X,
            Y = point.Y,
            Pressure = e.Pressure > 0 ? e.Pressure : 1f,
            Tilt = penTilt,
            Azimuth = penAzimuth,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var info = e.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        using var nibPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        // Everything below works in document units; the surface only has to scale them, and the note
        // is square so one factor is enough. This is what makes a stroke land in the same place in the
        // note editor and in the board preview.
        canvas.Save();
        canvas.Scale(info.Width / InkDocument.Size, info.Height / InkDocument.Size);

        foreach (var stroke in Strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            paint.Color = SKColor.Parse(stroke.Color);
            nibPaint.Color = paint.Color;

            var first = stroke.Points[0];

            if (stroke.Points.Count == 1)
            {
                // Render a dot for a single tap - the nib itself, so a tilted pen leaves the mark its
                // shape suggests.
                if (TiltRenderingEffect && first.Tilt > MinimumTilt)
                {
                    StampNib(canvas, nibPaint, first.X, first.Y, first.Tilt, first.Azimuth, stroke.Thickness);
                }
                else
                {
                    canvas.DrawCircle(first.X, first.Y, Math.Max(stroke.Thickness / 2f, 1f), nibPaint);
                }

                continue;
            }

            if (!PressureSensitiveWidth)
            {
                // Fallback: one width for the whole stroke, as before the setting existed.
                using var pathBuilder = new SKPathBuilder();
                pathBuilder.MoveTo(new SKPoint(first.X, first.Y));
                for (int i = 1; i < stroke.Points.Count; i++)
                {
                    var point = stroke.Points[i];
                    pathBuilder.LineTo(new SKPoint(point.X, point.Y));
                }

                using var path = pathBuilder.Detach();

                paint.StrokeWidth = stroke.Thickness * (stroke.Points[^1].Pressure > 0 ? stroke.Points[^1].Pressure : 1f);
                canvas.DrawPath(path, paint);
                continue;
            }

            // Pressure varies along the stroke, so the width has to as well - one path with a
            // single StrokeWidth would flatten the whole line to whatever the pen pressed last.
            // Each segment gets the mean of its two endpoints' pressure, which keeps neighbouring
            // segments close enough in width that the joins are not visible.
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                var from = stroke.Points[i - 1];
                var to = stroke.Points[i];
                var pressure = (from.Pressure + to.Pressure) / 2f;
                var thickness = stroke.Thickness * (pressure > 0 ? pressure : 1f);

                if (TiltRenderingEffect && from.Tilt > MinimumTilt)
                {
                    // A pen laid flat writes with the side of its nib: the mark is as wide as the nib
                    // is long and as thin as it is thick, so the width depends on the direction the pen
                    // is dragged in - which is exactly what stamping a rotated ellipse produces.
                    StampNibSegment(canvas, nibPaint, from, to, thickness);
                    continue;
                }

                paint.StrokeWidth = thickness;
                canvas.DrawLine(from.X, from.Y, to.X, to.Y, paint);
            }
        }

        canvas.Restore();
    }

    /// <summary>
    /// Draws one segment by stamping the pen's nib along it. The nib is an ellipse whose long axis
    /// follows the lean direction and whose length grows as the pen is laid flatter, so dragging the
    /// nib sideways leaves a broad mark and dragging it along its own long axis leaves a thin one.
    /// </summary>
    private void StampNibSegment(SKCanvas canvas, SKPaint paint, InkPoint from, InkPoint to, float thickness)
    {
        var distance = Distance(from.X, from.Y, to.X, to.Y);

        // Enough stamps that the nib footprints overlap; without that a fast stroke would come out as
        // a row of dots. The pen's lean is taken from the start of the segment - it changes too slowly
        // for interpolating it per segment to be worth the cost.
        var spacing = MathF.Max(thickness / 2f, 0.5f);
        var steps = Math.Clamp((int)MathF.Ceiling(distance / spacing), 1, MaxNibStampsPerSegment);

        for (var step = 1; step <= steps; step++)
        {
            var t = step / (float)steps;
            StampNib(
                canvas,
                paint,
                from.X + ((to.X - from.X) * t),
                from.Y + ((to.Y - from.Y) * t),
                from.Tilt,
                from.Azimuth,
                thickness);
        }
    }

    /// <summary>Stamps a single nib footprint at the given point.</summary>
    private void StampNib(SKCanvas canvas, SKPaint paint, float x, float y, float tilt, float azimuth, float thickness)
    {
        var minor = MathF.Max(thickness, 0.5f) / 2f;
        var flatten = Math.Clamp(tilt / HalfPi, 0f, 1f);
        var major = minor * (1f + (NibWidening * flatten));

        // Drawn around the origin and then rotated and moved into place, so the rotation cannot shift
        // the centre of the ellipse.
        canvas.Save();
        canvas.Translate(x, y);
        canvas.RotateRadians(azimuth);
        canvas.DrawOval(0f, 0f, major, minor, paint);
        canvas.Restore();
    }
}
