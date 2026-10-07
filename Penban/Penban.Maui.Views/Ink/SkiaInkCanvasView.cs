using Microsoft.Maui.Dispatching;
using Penban.Models;
using Penban.Recognition;
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

    /// <summary>
    /// How much wider the stroke itself gets when the pen is laid flat, as a share of its width. A pen
    /// that is stood upright leaves a thin line and one that is held flat a broad one: half again as
    /// wide at the 45° a pen is usually held at, twice as wide lying on the surface.
    /// </summary>
    private const float TiltWidthWidening = 1f;

    /// <summary>
    /// Upper bound on nib stamps per segment. It only has to stop a segment that crosses a large part
    /// of the note in a single event - a flick, or a stroke the platform reports as one long jump -
    /// from being drawn with thousands of stamps; it has to stay far above what a pen moving at
    /// writing speed asks for, because too few stamps leave a row of dots instead of a line.
    /// </summary>
    private const int MaxNibStampsPerSegment = 256;

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

    /// <summary>
    /// How far the pen has to travel before another point of the lasso is kept, in document units.
    /// The loop is only ever read as a shape - which points it encloses - so a point per reported
    /// position would cost a great deal of testing later on and show nothing more than one every few
    /// units does.
    /// </summary>
    private const float LassoPointSpacing = 5f;

    /// <summary>Width of the dotted frame drawn around the picked-up strokes, in document units.</summary>
    private const float SelectionFrameWidth = 3f;

    /// <summary>
    /// How far the frame around a picked-up group stands off the strokes themselves, in document
    /// units. It is also how far outside the group a contact may start and still count as being on
    /// the selection, so it has to be wide enough to be hit on purpose.
    /// </summary>
    private const float SelectionFrameMargin = 14f;

    /// <summary>Corner radius of that frame.</summary>
    private const float SelectionFrameCorner = 12f;

    /// <summary>Dash and gap of the frame and of the loop being drawn, in document units.</summary>
    private const float SelectionDash = 16f;
    private const float SelectionGap = 12f;

    /// <summary>Width of the loop being drawn, in document units.</summary>
    private const float LassoLoopWidth = 2.5f;

    /// <summary>
    /// How long a stroke has to be left alone - with the pen still on the note - before it is read as a
    /// shape - see <see cref="StartShapeHold"/>. Long enough that writing on, which never leaves the pen
    /// standing anywhere this long, is never read as a shape; short enough that someone waiting for a
    /// shape to appear does not think the note missed it.
    /// </summary>
    private static readonly TimeSpan ShapeHoldDelay = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// How far the pen has to have travelled between two points of a stroke for that to count as the
    /// pen still writing rather than as the pen standing still - see <see cref="StartShapeHold"/>. A
    /// surface keeps reporting the pen at the very place it is resting, and the wait for a shape is
    /// restarted by movement, so without a floor under it a pen that is being held still would never
    /// be read as a shape at all.
    /// </summary>
    private const float ShapeHoldMovement = 2f;

    /// <summary>
    /// How far a contact that put a picked-up group down may go before it counts as drawing rather than
    /// as the tap that put the group down - see <see cref="putDownContactId"/>. Wider than
    /// <see cref="ShapeHoldMovement"/> because a tap of a pen is not perfectly still: it is reported a
    /// little away from where it touched down, and the mark that must not appear is not allowed to
    /// depend on how steady the hand was.
    /// </summary>
    private const float PutDownSlop = 6f;

    /// <summary>
    /// Colour of the frame and of the loop. Deliberately not one of the pen colours: these mark a
    /// selection rather than being written, so they must never be mistaken for ink.
    /// </summary>
    private static readonly SKColor SelectionColor = SKColor.Parse("#1B4FD8").WithAlpha(150);

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

    /// <summary>
    /// How hard the pen is pressed right now, <c>0</c> to <c>1</c>, fed in by the platform input
    /// handler; <c>0</c> means "unknown". Only Apple and Windows need it: SkiaSharp's own touch events
    /// carry a pressure of their own there but always fill it with <c>1</c>, so the value the platform
    /// knows would otherwise never arrive - see <see cref="SetStylusPressure"/>.
    /// </summary>
    private float penPressure;

    /// <summary>Whether the stroke being drawn came from a finger rather than from a pen or mouse.</summary>
    private bool currentStrokeIsFinger;

    /// <summary>The contact the stroke being drawn belongs to, for as long as there is one.</summary>
    private long currentStrokeContactId;

    /// <summary>
    /// The contact that put a picked-up group down and has not yet written anything of its own, for as
    /// long as it is the contact that is down. A group is put down by touching anywhere but on it, and
    /// a touch that only says "this group is done with" must not leave a mark on the note as well -
    /// see <see cref="HandlePickedUpTouch"/>. The contact stops being that one as soon as it goes
    /// somewhere, because then it is drawing and the dot it began with is the start of a line.
    /// </summary>
    private long putDownContactId;

    /// <summary>
    /// Whether the first point of the stroke being drawn was recorded before the platform had reported
    /// how hard the pen is pressed, which is the usual case - see <see cref="SetStylusPressure"/>. The
    /// point is put right the moment the value arrives, so a stroke does not begin with a point at
    /// full width, which the nib draws as a blob where the pen went down.
    /// </summary>
    private bool firstPointAwaitsPressure;

    /// <summary>
    /// The same for the pen's lean. Without it the first point of a stroke is recorded upright, so the
    /// first segment of a calligraphy stroke would be drawn as a plain full-width line instead of as
    /// the nib.
    /// </summary>
    private bool firstPointAwaitsTilt;

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
    /// The eraser contact that is down, if there is one. One contact is one edit: what the sweep takes
    /// is only remembered here and goes into the history as a whole once the contact ends, so undoing
    /// an erase brings back every stroke the sweep took instead of one stroke per place it touched.
    /// </summary>
    private EraseSession? eraseSession;

    /// <summary>The contacts that are down on a surface that does not draw with them, in document units. Kept
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

    /// <summary>Which of the tools a contact is taken as - see <see cref="Tool"/>.</summary>
    private InkTool tool = InkTool.Pen;

    /// <summary>
    /// The loop the lasso is drawing right now, in document units, and whether one is being drawn at
    /// all. Kept while the contact is down so the loop can be seen - it is the only hint of where the
    /// selection will end up - and read once the contact ends, which is when it picks the strokes up.
    /// </summary>
    private readonly List<SKPoint> lassoPath = new();
    private bool isLassoDrawing;

    /// <summary>The contact that is drawing that loop, for as long as there is one.</summary>
    private long lassoContactId;

    /// <summary>The strokes the lasso has picked up. Held as the strokes themselves, so a selection
    /// survives the set around it being added to or erased from.</summary>
    private readonly HashSet<InkStroke> selection = new();

    /// <summary>What is being carried right now, or <c>null</c> when nothing is - see
    /// <see cref="BeginSelectionDrag"/>.</summary>
    private SelectionDrag? selectionDrag;

    /// <summary>
    /// The stroke whose shape is still being waited for - see <see cref="StartShapeHold"/> - and the
    /// timer that waits for it. The stroke is kept rather than found again when the timer runs out,
    /// because by then the pen may have written over it: the wait belongs to the stroke that started
    /// it, not to whatever is on top of the note at the time.
    /// </summary>
    private InkStroke? shapeHoldStroke;

    private IDispatcherTimer? shapeHoldTimer;

    /// <summary>
    /// The shape the pen is still holding, if it is holding one - see <see cref="RecognizeShape"/>. It
    /// is only there for as long as the pen that drew the stroke is on the note: it is what the pen
    /// pulls while it is still down, and what goes into the history as one change when it is lifted.
    /// </summary>
    private ShapeDraft? shapeDraft;

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

    public event EventHandler<InkStroke>? StrokeDrawn;

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

    /// <summary>
    /// Which tool a contact on the surface is taken as. Both the eraser and the lasso stay on until
    /// another tool is picked, unlike the button on the pen itself, which is only down for as long as
    /// it is held - that one changes this for the length of the press and puts it back.
    /// </summary>
    public InkTool Tool
    {
        get => tool;
        set
        {
            if (tool == value)
            {
                return;
            }

            // A loop that is still being drawn belongs to the tool that is being left, and a group that
            // is being carried is put down where it stands: carrying it was already a change, and the
            // new tool must not make the note forget it. See EndSelectionDrag.
            LeaveLasso();

            tool = value;
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ToolChanged;

    /// <summary>
    /// The eraser, seen the way the two-tool world of the buttons and of the pen's own double-tap
    /// still speaks of it: <c>false</c> writes again. The lasso is not reachable through this - it is
    /// a tool of its own and only the lasso button, the ring and the pen's button ask for it.
    /// </summary>
    public bool IsEraserMode
    {
        get => Tool == InkTool.Eraser;
        set => Tool = value ? InkTool.Eraser : InkTool.Pen;
    }

    public int SelectionCount => selection.Count;

    /// <summary>
    /// Raised whenever the picked-up group changes, so a page can offer to throw it away - see
    /// <see cref="DeleteSelection"/>.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Takes the picked-up strokes off the note, as one edit - undoing it brings back every one of
    /// them, in the place it had. What the selection named is gone with it, so the selection is empty
    /// afterwards.
    /// </summary>
    public bool DeleteSelection()
    {
        // The same reason as EraseAll: an eraser sweep that is still open is one change of its own,
        // and it has to be in the history before this one is put on top of it.
        EndEraseSession();

        if (selection.Count == 0)
        {
            return false;
        }

        // A stroke that was waiting to be read as a shape is taken off the note here, so the wait is
        // given up with it - and a shape that is still being pulled goes back to what was written, so
        // that what is deleted is what is on the note.
        CancelShape();

        // Ascending, and carrying the position each stroke has right now, which is the same shape an
        // "erase everything" records its strokes in - see EraseAll.
        var removed = new List<InkStrokePlacement>(selection.Count);
        for (var index = 0; index < Strokes.Count; index++)
        {
            if (selection.Contains(Strokes[index]))
            {
                removed.Add(new InkStrokePlacement(index, Strokes[index]));
            }
        }

        if (removed.Count == 0)
        {
            ClearSelection();
            return false;
        }

        foreach (var placement in removed)
        {
            Strokes.Remove(placement.Stroke);
        }

        commands.RecordErased(removed);

        // The strokes the selection named are gone, so there is nothing left to frame or to carry.
        ClearSelection();
        canvasView.InvalidateSurface();

        // The note changed, so this is raised the way any other change to the stroke set is.
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Drops the picked-up group without changing the note. The strokes stay where they are; only the
    /// mark around them and the offer to throw them away go.
    /// </summary>
    public void ClearSelection()
    {
        if (selection.Count == 0)
        {
            return;
        }

        selection.Clear();

        // The frame is drawn over the note rather than written into it, so the note has to be drawn
        // again for the mark to be gone from what is seen: nothing else about the note changed, and
        // without this the surface keeps showing the frame until something else redraws it.
        canvasView.InvalidateSurface();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes every stroke that is no longer in the set out of the picked-up group. A group names the
    /// strokes themselves rather than their places, which is what lets it survive the set around it
    /// being added to - and it is also what makes this necessary, because nothing else tells a group
    /// that one of its strokes was erased from under it.
    /// <para>
    /// What is left behind without this is a mark around a stroke that is gone: the group keeps
    /// holding the erased stroke, so the mark stays drawn around the empty place it covered, and the
    /// next contact inside that mark takes hold of a group whose strokes are not on the note any more.
    /// That contact is swallowed - nothing is drawn where the pen was put down - and the move it makes
    /// records an edit that carries nothing and still costs one step of the history, which is what made
    /// undo appear to do nothing once and bring back old strokes the time after.
    /// </para>
    /// </summary>
    private void PruneSelection()
    {
        if (selection.Count == 0)
        {
            return;
        }

        if (selection.RemoveWhere(stroke => !Strokes.Contains(stroke)) == 0)
        {
            return;
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

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
    /// Whether the line width follows the pressure recorded per point. Off means one width for the
    /// whole stroke, which is also what a device that reports no pressure produces - see
    /// <see cref="SetStylusPressure"/> for which devices those are.
    /// </summary>
    public bool PressureSensitiveWidth { get; set; } = true;

    /// <summary>
    /// Whether the line width follows how flat the pen is held: upright draws thin, laid flat draws
    /// broad. The width stays the same along the stroke, whichever way the pen is dragged - the nib
    /// whose shape makes it depend on that is <see cref="TiltRenderingEffect"/>.
    /// </summary>
    public bool TiltSensitiveWidth { get; set; }

    /// <summary>
    /// Whether the pen tilt recorded in <see cref="InkPoint.Tilt"/>/<see cref="InkPoint.Azimuth"/> is
    /// turned into a calligraphy look. Has no visible effect on strokes whose tilt is zero, so a
    /// device that reports no tilt draws exactly as before.
    /// </summary>
    public bool TiltRenderingEffect { get; set; }

    /// <summary>
    /// Whether a stroke that is left to stand for a moment is read as the shape it was drawn as - a
    /// line, a rectangle or an ellipse - and replaced by that shape. Off leaves every stroke exactly
    /// as it was written, which is what someone drawing a deliberately rough diagram wants.
    /// </summary>
    public bool ShapeRecognition { get; set; } = true;

    /// <summary>
    /// Waits, while the pen is still on the note, for the stroke to be left alone - see
    /// <see cref="ShapeHoldDelay"/>. Held that long without the pen moving, it is taken to have been
    /// drawn as a shape rather than written, and is replaced by the shape it was read as.
    /// <para>
    /// The wait is what makes this liveable-with: someone writing a letter that happens to be round -
    /// an "o", a "0", the loop of a "g" - is moving the pen all the while, and every point of that
    /// movement starts the wait again. Only a pen that has come to rest, and is waiting for the shape
    /// to appear, gets one.
    /// </para>
    /// <para>
    /// It runs with the pen down rather than after it is lifted, because the pen that is still there is
    /// what pulls the shape into its size - see <see cref="ShapeResize"/>. Read after the pen was
    /// lifted, a shape would be stuck at the size it happened to be drawn at, and ending a note on a
    /// shape would mean drawing another stroke on top of it.
    /// </para>
    /// </summary>
    private void StartShapeHold(InkStroke stroke)
    {
        CancelShapeHold();

        // Only the stroke the pen is still on: the wait is for the pen to come to rest over what it is
        // drawing, and there is nothing to come to rest over before the pen has moved at all.
        if (!ShapeRecognition || stroke != currentStroke || stroke.Points.Count < 2 || !Strokes.Contains(stroke))
        {
            return;
        }

        shapeHoldStroke = stroke;

        if (shapeHoldTimer is null)
        {
            shapeHoldTimer = Dispatcher.CreateTimer();
            shapeHoldTimer.IsRepeating = false;
            shapeHoldTimer.Tick += OnShapeHoldElapsed;
        }

        shapeHoldTimer.Interval = ShapeHoldDelay;
        shapeHoldTimer.Start();
    }

    /// <summary>
    /// Gives up the wait for a shape. Called wherever the note is changed from somewhere other than
    /// the pen - an undo, an erase, a load - because the stroke that was waiting to be read as a shape
    /// is no longer the stroke that was written.
    /// </summary>
    private void CancelShapeHold()
    {
        shapeHoldTimer?.Stop();
        shapeHoldStroke = null;
    }

    /// <summary>
    /// Gives up on a shape altogether: the wait for one, and one that is being pulled into size - see
    /// <see cref="AbandonShapeDraft"/>. Called wherever the note is changed from somewhere other than
    /// the pen that is holding the shape.
    /// </summary>
    private void CancelShape()
    {
        CancelShapeHold();
        AbandonShapeDraft();
    }

    private void OnShapeHoldElapsed(object? sender, EventArgs e)
    {
        shapeHoldTimer?.Stop();

        var stroke = shapeHoldStroke;
        shapeHoldStroke = null;

        // The pen has been standing still long enough - but only if it is still standing there at all:
        // the stroke that is no longer the one being drawn is one whose pen is gone.
        if (stroke is not null && stroke == currentStroke)
        {
            RecognizeShape(stroke);
        }
    }

    /// <summary>
    /// Replaces <paramref name="stroke"/> by the shape it was read as, if it was read as one, and hands
    /// the pen the shape to pull into size. The stroke object itself stays and only its points are
    /// written over: the group the lasso picked up holds the stroke itself, the history names it, and
    /// the note is saved from the set - so a stroke that was read as a shape is the very stroke that was
    /// written, drawn differently.
    /// </summary>
    private void RecognizeShape(InkStroke stroke)
    {
        if (!Strokes.Contains(stroke))
        {
            return;
        }

        var shape = ShapeRecognizer.Recognize(stroke.Points);
        if (shape is null)
        {
            return;
        }

        var freehand = stroke.Points.ToList();
        var pen = freehand[^1];
        var resize = ShapeResize.Begin(shape, pen.X, pen.Y);

        if (resize is null)
        {
            return;
        }

        // Nothing goes into the history here: the size the shape is to have is not decided until the
        // pen is lifted, and a change recorded before then would be a step of the history for a shape
        // that never existed. What is recorded at the end is the whole of it, as one change - see
        // FinishShapeDraft.
        stroke.Points = shape.Points.ToList();
        shapeDraft = new ShapeDraft(stroke, freehand, resize);

        canvasView.InvalidateSurface();
    }

    /// <summary>
    /// Follows the pen that is pulling the shape it is holding: the shape is drawn the size the pen
    /// asks for, so what is seen while the pen is still down is what is kept when it is lifted.
    /// </summary>
    private void PullShapeDraft(ShapeDraft draft, SKTouchEventArgs e)
    {
        var pen = ToDocument(e.Location);
        draft.Stroke.Points = draft.Resize.At(pen.X, pen.Y).ToList();
    }

    /// <summary>
    /// Puts the shape the pen is holding into the history, as the single change it was: the stroke as it
    /// was written, and the stroke as it is now that the pen is done pulling it. Undoing a shape is
    /// therefore one step, and the stroke it brings back is the one that was written.
    /// </summary>
    private void FinishShapeDraft()
    {
        if (shapeDraft is not { } draft)
        {
            return;
        }

        shapeDraft = null;

        // A stroke that is no longer on the note has nothing to put right - an eraser took it, and the
        // erase is the change the history has of it.
        if (!Strokes.Contains(draft.Stroke))
        {
            return;
        }

        commands.RecordMoved([new InkStrokeMove(draft.Stroke, draft.Freehand, draft.Stroke.Points.ToList())]);
        canvasView.InvalidateSurface();
    }

    /// <summary>
    /// Gives up on a shape that is being pulled and puts the stroke back the way it was written. Called
    /// wherever the note is changed from somewhere other than the pen that is holding the shape: the
    /// points the shape was read into were never recorded as a change, so a shape left standing would be
    /// a note that no undo step knows about.
    /// </summary>
    private void AbandonShapeDraft()
    {
        if (shapeDraft is not { } draft)
        {
            return;
        }

        shapeDraft = null;

        if (!Strokes.Contains(draft.Stroke))
        {
            return;
        }

        draft.Stroke.Points = draft.Freehand.ToList();
        canvasView.InvalidateSurface();
    }

    /// <summary>
    /// A shape that has been read and is being pulled into size by the pen that drew it, before that pen
    /// has been lifted: the stroke, the points it was written with, and where it is being pulled from.
    /// <see cref="Freehand"/> is kept because it is what the single change recorded at the end of the
    /// pull starts from - and what the stroke goes back to if the shape is given up on.
    /// </summary>
    private sealed class ShapeDraft
    {
        public ShapeDraft(InkStroke stroke, List<InkPoint> freehand, ShapeResize resize)
        {
            Stroke = stroke;
            Freehand = freehand;
            Resize = resize;
        }

        /// <summary>The stroke the shape was read from, which is the stroke it is drawn as.</summary>
        public InkStroke Stroke { get; }

        /// <summary>The points that stroke had before it was read as a shape.</summary>
        public List<InkPoint> Freehand { get; }

        /// <summary>Where the shape is being pulled from, and to where the pen says.</summary>
        public ShapeResize Resize { get; }
    }

    /// <summary>
    /// Tilt and lean direction the stylus currently reports, fed in by the platform input handler.
    /// Both are radians; <c>0</c> means "upright" and "unknown", which is what every input device that
    /// does not report tilt leaves behind.
    /// </summary>
    public void SetStylusTilt(float tilt, float azimuth)
    {
        penTilt = tilt;
        penAzimuth = azimuth;

        // The lean of the point the stroke started with was not known when that point was recorded, so
        // it is written into it now - while it is still the only point of the stroke, which is when it
        // is still the point the value belongs to.
        if (firstPointAwaitsTilt && currentStroke is { Points.Count: 1 } stroke)
        {
            var point = stroke.Points[0];
            point.Tilt = tilt;
            point.Azimuth = azimuth;
            stroke.Points[0] = point;
            firstPointAwaitsTilt = false;
            canvasView.InvalidateSurface();
        }
    }

    /// <summary>
    /// How hard the stylus is pressed, <c>0</c> to <c>1</c>, fed in by the platform input handler.
    /// <c>0</c> - or never calling this at all - means the platform has nothing to add and the value
    /// SkiaSharp reports is used instead.
    /// <para>
    /// It has to be fed in on Apple and on Windows: SkiaSharp 4.150.1 calls its own touch event with a
    /// constructor that hard-codes the pressure to <c>1</c> on both of them, so every point would be
    /// recorded at full width however lightly the pen was pressed. Android is the one platform whose
    /// backend reads the real pressure out of the touch stream, so there this is never called.
    /// </para>
    /// </summary>
    public void SetStylusPressure(float pressure)
    {
        penPressure = pressure;

        // A pen that is lifted takes its press with it, so the point the next stroke starts with is
        // recorded before the platform has said how hard that pen is now pressed and ends up at full
        // width. The press of the stroke that is starting is known here.
        if (pressure > 0 && firstPointAwaitsPressure && currentStroke is { Points.Count: 1 } stroke)
        {
            var point = stroke.Points[0];
            point.Pressure = pressure;
            stroke.Points[0] = point;
            firstPointAwaitsPressure = false;
            canvasView.InvalidateSurface();
        }
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

        if (Tool == InkTool.Lasso)
        {
            // The loop starts from the press the pen made before the platform named it, not from where
            // the pen has got to by now - the same reason the stroke starts there.
            HandleLassoTouch(press);
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

    public bool EraseAll()
    {
        // A sweep that is still open is closed first, so the strokes it took are recorded before the
        // rest of the note is: the two are two changes to the set, and each of them is a step of the
        // history that can be taken back on its own. Left open, the sweep would be dropped by the
        // reset below and its strokes would be gone for good.
        EndEraseSession();

        if (Strokes.Count == 0)
        {
            return false;
        }

        // Every stroke goes into one edit, carrying the position it had, so undo puts the whole note
        // back in the order it was drawn in - the same shape the eraser records its strokes in.
        var removed = new List<InkStrokePlacement>(Strokes.Count);
        for (var index = 0; index < Strokes.Count; index++)
        {
            removed.Add(new InkStrokePlacement(index, Strokes[index]));
        }

        Strokes.Clear();
        commands.RecordErased(removed);
        ResetInputState();
        canvasView.InvalidateSurface();
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Undo()
    {
        // A shape that has not appeared yet must not appear on a note that has just been wound back: the
        // stroke it would be read from may be gone, or may have been put back as it was first written.
        // A shape that is being pulled goes back to what was written, for the same reason: it is not in
        // the history yet, so a shape left standing here would be a note no undo step knows about.
        CancelShape();

        // An eraser sweep that is still open is the most recent change to the set, so it is what this
        // undo is asking for: closing it records it, and the undo below takes it back. Left open, the
        // undo would wind back the change before the sweep and the sweep would be recorded afterwards,
        // out of order.
        EndEraseSession();

        if (!commands.Undo())
        {
            return;
        }

        // What the selection named may have just been taken off the note or put back on it, and the
        // strokes it holds are not the ones that were carried anywhere by that edit.
        ClearSelection();
        canvasView.InvalidateSurface();
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        CancelShape();
        EndEraseSession();

        if (!commands.Redo())
        {
            return;
        }

        ClearSelection();
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

        // A stroke that was waiting to be read as a shape belongs to the set that is being replaced,
        // and the history is being reset alongside this - reading it now would record an edit for a
        // stroke the history no longer knows about. A shape that was being pulled goes back to what was
        // written for the same reason: the change that would have recorded it is never coming.
        CancelShape();

        // A sweep that was being remembered belongs to the set that has just been replaced, so it is
        // dropped rather than recorded - the same reason the press behind it is.
        eraseSession = null;
        currentStrokeIsFinger = false;
        currentStrokeContactId = 0;
        firstPointAwaitsPressure = false;
        firstPointAwaitsTilt = false;
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

        // So does anything the lasso was in the middle of: a loop named strokes of the set that is
        // gone, and a group that was being carried is gone with it. Nothing is recorded for either of
        // them - the history is being reset alongside this, so an edit here would name strokes that
        // are no longer anywhere.
        CancelLasso();
        ClearSelection();
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        var isStylus = IsStylusContact(e);

        // The pen - or the finger - has come back to the surface, which answers the question the shape
        // wait was asking, whatever it came back to do: picking up a group, erasing, writing. Nothing
        // was read as a shape then - see StartShapeHold.
        if (e.ActionType == SKTouchAction.Pressed)
        {
            CancelShapeHold();

            // Whatever put a group down before this contact began, this contact is not it: the flag
            // only ever names the contact that is down - see putDownContactId.
            putDownContactId = 0;

            // A press that is not the pen that is holding a shape means that pen is gone: the change
            // that would have kept the shape is never coming, so what was being pulled goes back to
            // what was written - see AbandonShapeDraft.
            if (currentStroke is null)
            {
                AbandonShapeDraft();
            }
        }

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

        // The eraser end of the pen comes first whatever else is chosen: which tool is picked says
        // nothing about which end of the pen is against the surface.
        if (isPenTailErasing)
        {
            HandleEraseTouch(e);
            return;
        }

        if (IsEraserMode)
        {
            HandleEraseTouch(e);
            return;
        }

        // A group that the lasso has picked up is carried by the pen whatever tool is in hand - see
        // HandlePickedUpTouch - so the lasso itself only ever reads its own loop here.
        if (Tool != InkTool.Lasso && HandlePickedUpTouch(e, isStylus))
        {
            return;
        }

        if (Tool == InkTool.Lasso)
        {
            HandleLassoTouch(e);
            return;
        }

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                StartStroke(e, isFinger: !isStylus);
                e.Handled = true;
                break;

            case SKTouchAction.Moved:
                if (shapeDraft is not null && e.InContact)
                {
                    // The pen is on a shape it has just been given: it is no longer drawing, it is
                    // saying how big the shape is to be - see PullShapeDraft.
                    PullShapeDraft(shapeDraft, e);
                    canvasView.InvalidateSurface();
                }
                else if (currentStroke is not null && e.InContact)
                {
                    // A contact that put a picked-up group down has not gone anywhere yet, so it is
                    // still the tap that put the group down and nothing is written for it - see
                    // DropPutDownTap. The moment it does go somewhere it is drawing, and what it draws
                    // from there on is a stroke like any other.
                    if (e.Id == putDownContactId)
                    {
                        if (!LeftPutDownSlop(currentStroke, ToDocument(e.Location)))
                        {
                            e.Handled = true;
                            break;
                        }

                        putDownContactId = 0;
                    }

                    AddPoint(currentStroke, e);

                    // Writing again, so the wait for a shape starts over - but only for a pen that has
                    // actually gone somewhere: a surface reports a pen that is resting on the note over
                    // and over at the same place, and a shape is drawn by holding the pen still - see
                    // StartShapeHold.
                    if (Traveled(currentStroke))
                    {
                        StartShapeHold(currentStroke);
                    }

                    canvasView.InvalidateSurface();
                }

                e.Handled = true;
                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                // A shape that was being pulled is done being pulled, and what it looks like now is
                // what the pen asked for, so this is where it becomes the change the history keeps.
                FinishShapeDraft();

                // A contact that only put a picked-up group down leaves nothing of its own behind - see
                // DropPutDownTap. This has to happen before the stroke is closed off, because what it
                // drops is a stroke that was never drawn.
                DropPutDownTap(e.Id);

                if (currentStroke is not null)
                {
                    var finished = currentStroke;
                    currentStroke = null;
                    currentStrokeContactId = 0;

                    // The pen is off the note, so there is no longer anything standing still over the
                    // stroke for it to be read as a shape: a stroke is read as one while the pen that
                    // drew it is still on it - see StartShapeHold.
                    CancelShapeHold();
                    canvasView.InvalidateSurface();

                    // Reached only by a stroke that was drawn: the eraser, the lasso and a contact that
                    // carries a picked-up group all leave this loop before a stroke is started, and a
                    // contact that turns out to be a gesture has its stroke thrown away instead - see
                    // DropCurrentStroke. What is on the note from here on is a stroke the pen wrote.
                    StrokeDrawn?.Invoke(this, finished);
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
        // An eraser sweep that is still open is the change before this one, and it has to be in the
        // history before the stroke is. The sweep is only recorded once the eraser is lifted, so a
        // stroke recorded before that would sit *below* it - and taking that stroke back would bring
        // back the strokes the sweep took, which were erased long before it.
        EndEraseSession();

        // Whatever was being written before this contact, this is a new stroke - and a new stroke is
        // the pen coming back to the note, so it is never a shape being pulled. Nothing is recorded for
        // a shape given up here: the stroke goes back to what was written and that is what the history
        // already has.
        AbandonShapeDraft();

        currentStroke = new InkStroke
        {
            Color = StrokeColor,
            Thickness = StrokeThickness,
            // The stroke is given the setting of this moment, so that the width it is drawn in stays its
            // own for good.
            PressureSensitiveWidth = PressureSensitiveWidth,
        };
        currentStrokeContactId = e.Id;
        currentStrokeIsFinger = isFinger;
        AddPoint(currentStroke, e);

        // The platform reports the press and the lean of the pen after the canvas has already handled
        // the point they belong to, so the point this stroke starts with is written without them and
        // put right as soon as they arrive - see SetStylusPressure and SetStylusTilt.
        firstPointAwaitsPressure = true;
        firstPointAwaitsTilt = true;

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

                        // A loop that this finger was drawing belongs to a touch that has just turned
                        // out to be a gesture: nothing is picked up by it, and a group it had already
                        // picked up and was carrying is put down where it stands.
                        LeaveLasso();
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

        // A shape that was being pulled by this stroke goes with it. The stroke object is the same one
        // that was written, so it is enough to forget the pull: the stroke is about to leave the note
        // and its entry in the history is about to be taken back with it.
        if (shapeDraft is not null && shapeDraft.Stroke == currentStroke)
        {
            shapeDraft = null;
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

        // The contact is over - the eraser was lifted, or the platform gave up on it, which on a
        // handheld is also how a pen that leaves the screen reports itself. Either way the sweep is
        // finished, and its whole edit goes into the history now.
        if (!e.InContact || e.ActionType is SKTouchAction.Released or SKTouchAction.Cancelled)
        {
            EndEraseSession();
            e.Handled = true;
            return;
        }

        if (e.ActionType is not (SKTouchAction.Pressed or SKTouchAction.Moved))
        {
            e.Handled = true;
            return;
        }

        // One contact is one edit: the sweep is opened with the strokes as they stand now. A press is
        // the start of a contact, so an edit that is still open from the contact before it - one whose
        // end the platform never reported - is closed here rather than stretched over this one.
        if (e.ActionType == SKTouchAction.Pressed)
        {
            EndEraseSession();

            // A shape that is still being pulled goes back to what was written before the eraser
            // starts: what the eraser takes off the note has to be what is on the note, and a shape
            // whose points were never recorded as a change must not be the thing that gets recorded as
            // erased - see AbandonShapeDraft.
            AbandonShapeDraft();
        }

        BeginEraseSession();

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

        // The dropped stroke is what is being replaced here, so the sweep is opened after it: a tail
        // held down is one edit like any other eraser contact, see the note on the field.
        BeginEraseSession();

        var erased = hasTouched && EraseStrokesAt(touched.X, touched.Y);
        canvasView.InvalidateSurface();

        if (erased)
        {
            StrokeCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Ends the eraser-end contact; the next touch writes again.</summary>
    public void EndPenTailErase()
    {
        isPenTailErasing = false;
        EndEraseSession();
    }

    /// <summary>
    /// Opens the edit an eraser contact makes, with the strokes as they stand at that moment. Doing
    /// nothing while a sweep is already open is what keeps a contact that the platform reports as many
    /// presses of the same place, and a pen's tail that is held down, one single edit.
    /// </summary>
    private void BeginEraseSession()
    {
        var session = eraseSession ??= new EraseSession();
        session.Order ??= new List<InkStroke>(Strokes);
    }

    /// <summary>
    /// Closes the edit an eraser contact made: everything the sweep took goes into the history as one
    /// entry, so one undo brings back every stroke of it. A contact that took nothing leaves nothing
    /// behind, and neither does a session that was already closed by the other end of the contact.
    /// </summary>
    private void EndEraseSession()
    {
        var session = eraseSession;
        eraseSession = null;

        if (session?.Order is not { } order || session.Removed.Count == 0)
        {
            return;
        }

        // Each swept stroke is put back where it stood when the eraser touched down: at the number of
        // the strokes of that moment that are still standing before it. Counting it from the order of
        // that moment - rather than keeping the position every touch found the stroke in - is what
        // makes the sweep one entry: those positions were measured against a set that the sweep itself
        // was already shrinking.
        var removed = new HashSet<InkStroke>(session.Removed);
        var placements = new List<InkStrokePlacement>(removed.Count);
        var standing = 0;

        foreach (var stroke in order)
        {
            if (removed.Contains(stroke))
            {
                placements.Add(new InkStrokePlacement(standing, stroke));
            }
            else if (Strokes.Contains(stroke))
            {
                standing++;
            }
        }

        if (placements.Count == 0)
        {
            return;
        }

        commands.RecordErased(placements);

        // The edit exists only now, so this is where the caller learns of it - the row that offers to
        // take it back has something to offer from here on, and the card is written as after any other
        // change to the stroke set.
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
    }

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
            if (!StrokeMeets(stroke, x, y))
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

        // Whatever the sweep took out of the set goes out of the picked-up group with it - see
        // PruneSelection. Erasing is the one way a stroke leaves the set while a group is still up.
        PruneSelection();

        if (eraseSession is { } session)
        {
            // The contact that made this sweep is still down, so what it took is only remembered: the
            // whole sweep is recorded when that contact ends, see EndEraseSession. Nothing but the set
            // of strokes is needed for that, because the places are measured there.
            foreach (var placement in removed)
            {
                session.Removed.Add(placement.Stroke);
            }

            return true;
        }

        // No contact to hold the sweep together - an edit of its own, the way the eraser always
        // recorded one.
        removed.Reverse();
        commands.RecordErased(removed);
        return true;
    }

    /// <summary>
    /// A stroke set as it stood when an eraser touched down, and what the sweep that followed took
    /// from it. Kept as a plain object rather than as a change to the history: what the sweep means
    /// as a whole is only known once the eraser is up again - see
    /// <see cref="SkiaInkCanvasView.EndEraseSession"/>.
    /// </summary>
    private sealed class EraseSession
    {
        /// <summary>The strokes in their order at the start of the sweep, remembered on the way in.</summary>
        public List<InkStroke>? Order { get; set; }

        /// <summary>What the sweep has taken since, in the order it was taken.</summary>
        public List<InkStroke> Removed { get; } = new();
    }

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// Whether a stroke comes within <see cref="EraseRadius"/> of the point: near one of the points it
    /// was written with, or near one of the lines drawn between two of them.
    /// <para>
    /// The lines have to be measured as well, because a stroke that was read as a shape keeps nothing
    /// but the corners of that shape: the straight edges the shape is drawn with are not written down
    /// anywhere, only the points they start and end at. A hit test that only looked at the points could
    /// therefore only ever rub such a stroke out where its corners are, and not along the sides that
    /// are what is seen of it.
    /// </para>
    /// </summary>
    private static bool StrokeMeets(InkStroke stroke, float x, float y)
    {
        var points = stroke.Points;

        if (points.Count == 0)
        {
            return false;
        }

        for (var i = 1; i < points.Count; i++)
        {
            if (DistanceToLineSegment(points[i - 1], points[i], x, y) <= EraseRadius)
            {
                return true;
            }
        }

        // A stroke of one point - a dot - has no line to be near, so its point is looked at on its own.
        return Distance(points[0].X, points[0].Y, x, y) <= EraseRadius;
    }

    /// <summary>
    /// How far the point lies from the line between two points of a stroke, measured to the nearer end
    /// of that line once the point is past it, so a stroke is not met from beyond its own ends.
    /// </summary>
    private static float DistanceToLineSegment(InkPoint from, InkPoint to, float x, float y)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared <= 0f)
        {
            // Both ends in the same place: the line is that place.
            return Distance(from.X, from.Y, x, y);
        }

        var share = Math.Clamp((((x - from.X) * dx) + ((y - from.Y) * dy)) / lengthSquared, 0f, 1f);
        return Distance(from.X + (share * dx), from.Y + (share * dy), x, y);
    }

    /// <summary>
    /// Follows a contact on a note that holds a picked-up group: on the group itself the contact
    /// carries it, and anywhere else it says the group is done with.
    /// <para>
    /// A picked-up group is carried by the pen whatever tool is in hand, because the button on a pen
    /// only holds the lasso for as long as it is down. The flow that button starts - a loop around the
    /// ink, the button let go, the group carried where it belongs - would otherwise hand the note back
    /// to the pen with the group still picked up and nothing but a new stroke to touch it with, which
    /// is the one thing a group that is being carried is not for. Taking hold of the group with the
    /// pen is also what makes the lasso a tool that can be left behind: a tap or a stroke away from
    /// the group is the note being written on again, and it puts the group down first.
    /// </para>
    /// </summary>
    /// <returns>
    /// Whether the contact was taken over. A contact that lands away from the group is handed back to
    /// the tool in hand, with the group put down on the way, so it goes on to draw or to erase as it
    /// would have with nothing picked up.
    /// </returns>
    private bool HandlePickedUpTouch(SKTouchEventArgs e, bool isStylus)
    {
        // A finger only carries a group where it draws at all: a finger that is left to the scroll has
        // nothing to say about what is on the note.
        if (selection.Count == 0 || (!isStylus && !AllowFingerDrawing))
        {
            return false;
        }

        if (e.ActionType == SKTouchAction.Pressed)
        {
            var touch = ToDocument(e.Location);

            if (!IsInsideSelection(touch))
            {
                // Anywhere but the group itself puts it down before the contact does its own work, so
                // the stroke this press is about to start is drawn and not added to the group. The
                // contact is remembered as the one that put the group down: if it only goes on to lift
                // again, it said no more than "this group is done with", and a mark left behind for
                // that would be one nobody asked for - see DropPutDownTap.
                putDownContactId = e.Id;
                ClearSelection();
                canvasView.InvalidateSurface();
                return false;
            }

            BeginSelectionDrag(touch);
        }
        else if (selectionDrag is null)
        {
            // Nothing is being carried, so a contact that is not the press that takes hold of the
            // group is none of this one's business.
            return false;
        }
        else if (e.ActionType == SKTouchAction.Moved && e.InContact)
        {
            DragSelection(ToDocument(e.Location));
        }
        else if (!e.InContact || e.ActionType is SKTouchAction.Released or SKTouchAction.Cancelled)
        {
            // The contact that was carrying the group is over, so the group is where it was carried
            // to and the move is recorded - see EndSelectionDrag.
            EndSelectionDrag();
        }

        canvasView.InvalidateSurface();
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// Follows a contact of the lasso: while it is down it draws a loop, and when it comes up the loop
    /// picks up the strokes it encloses. A contact that starts on what is already picked up carries
    /// that group instead, which is how a group is taken somewhere else on the note.
    /// <para>
    /// The loop is only ever read when the contact ends, so a loop that is abandoned - the contact
    /// cancelled, a second finger turning the touch into a gesture, another tool being picked - picks
    /// up nothing at all and leaves the selection as it was.
    /// </para>
    /// </summary>
    private void HandleLassoTouch(SKTouchEventArgs e)
    {
        if (IsLeftToTheScroll(e))
        {
            e.Handled = false;
            return;
        }

        // Either the contact is over or it is still down and somewhere else; there is nothing else a
        // touch can be.
        if (!e.InContact || e.ActionType is SKTouchAction.Released or SKTouchAction.Cancelled)
        {
            EndLassoContact();
            e.Handled = true;
            return;
        }

        var touch = ToDocument(e.Location);

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:

                // One contact is one thing at a time: a loop that is still open - its end was never
                // reported - is settled before this contact starts its own.
                EndLassoContact();

                if (selection.Count > 0 && IsInsideSelection(touch))
                {
                    BeginSelectionDrag(touch);
                }
                else
                {
                    BeginLasso(e.Id, touch);
                }

                break;

            case SKTouchAction.Moved:

                if (selectionDrag is not null)
                {
                    DragSelection(touch);
                }
                else if (isLassoDrawing)
                {
                    AddLassoPoint(touch);
                }

                break;
        }

        canvasView.InvalidateSurface();
        e.Handled = true;
    }

    /// <summary>Ends the contact that is down: what it drew is read, or what it carried is recorded.</summary>
    private void EndLassoContact()
    {
        if (selectionDrag is not null)
        {
            EndSelectionDrag();
            return;
        }

        if (!isLassoDrawing)
        {
            return;
        }

        isLassoDrawing = false;
        lassoContactId = 0;

        var picked = StrokesInsideLoop();
        lassoPath.Clear();

        if (Select(picked))
        {
            canvasView.InvalidateSurface();
        }
    }

    /// <summary>
    /// Reads the loop being drawn the way lifting the pen does, without the contact that drew it having
    /// to be lifted: this is the pen's own button being let go. On Windows the button is what draws the
    /// loop, and letting it go is the gesture that says the loop is done - so what it encloses has to be
    /// picked up there and then. The contact that drew it is usually still on the note at that moment,
    /// which is why the loop cannot simply be waited out: the tool goes back to the pen the instant the
    /// button comes up, and the point that ends the loop would otherwise be written as the first dot of
    /// a new stroke.
    /// <para>
    /// The contact that is still down does not go on to carry the group it has just picked up: it is
    /// already a stroke's worth of touching the note, and reading it as a drag would move the group by
    /// however far the loop travelled. Pressing the group carries it, exactly as it does after a loop
    /// that was closed by lifting the pen.
    /// </para>
    /// </summary>
    public void FinishLasso()
    {
        if (isLassoDrawing)
        {
            EndLassoContact();
        }
    }

    /// <summary>
    /// Puts down whatever the lasso is in the middle of. A group that is being carried is recorded as
    /// a move - its points have already been written over, so dropping it would leave the note changed
    /// with nothing to undo - and a loop that is being drawn is thrown away.
    /// </summary>
    private void LeaveLasso()
    {
        EndSelectionDrag();
        CancelLasso();
    }

    /// <summary>
    /// Drops the loop being drawn and the drag underway without recording either. Only for what the
    /// lasso was working on being gone anyway - the stroke set replaced underneath it, the history
    /// reset with it - where an edit would name strokes that are nowhere.
    /// </summary>
    private void CancelLasso()
    {
        isLassoDrawing = false;
        lassoContactId = 0;
        lassoPath.Clear();
        selectionDrag = null;
    }

    private void BeginLasso(long contactId, SKPoint from)
    {
        lassoContactId = contactId;
        isLassoDrawing = true;
        lassoPath.Clear();
        lassoPath.Add(from);
    }

    /// <summary>
    /// Keeps a point of the loop, close enough to the last one to describe the same shape. A pen
    /// reports far more positions than a loop needs, and every point of it is tested against every
    /// point of every stroke once the loop is read.
    /// </summary>
    private void AddLassoPoint(SKPoint point)
    {
        if (lassoPath.Count > 0)
        {
            var last = lassoPath[^1];
            if (Distance(last.X, last.Y, point.X, point.Y) < LassoPointSpacing)
            {
                return;
            }
        }

        lassoPath.Add(point);
    }

    /// <summary>
    /// The strokes the loop encloses: every point of a stroke has to lie inside it, so a stroke that
    /// crosses the loop is not picked up - it is not inside it, it only goes through it.
    /// </summary>
    private HashSet<InkStroke> StrokesInsideLoop()
    {
        var picked = new HashSet<InkStroke>();

        // Three points are the least that can enclose anything at all.
        if (lassoPath.Count < 3)
        {
            return picked;
        }

        var bounds = LoopBounds();
        foreach (var stroke in Strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            var inside = true;
            foreach (var point in stroke.Points)
            {
                // The loop is a plain polygon, so the box around it settles most points without
                // walking its edges for each of them.
                if (point.X < bounds.Left || point.X > bounds.Right || point.Y < bounds.Top || point.Y > bounds.Bottom
                    || !IsInsideLoop(point.X, point.Y))
                {
                    inside = false;
                    break;
                }
            }

            if (inside)
            {
                picked.Add(stroke);
            }
        }

        return picked;
    }

    /// <summary>Whether one point lies inside the loop drawn by the pen - the usual ray test.</summary>
    private bool IsInsideLoop(float x, float y)
    {
        var inside = false;

        for (int i = 0, j = lassoPath.Count - 1; i < lassoPath.Count; j = i++)
        {
            var a = lassoPath[i];
            var b = lassoPath[j];

            // Counted only by edges that straddle the line through the point, and then only when the
            // crossing lies to its right.
            if ((a.Y > y) != (b.Y > y) && x < ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private SKRect LoopBounds()
    {
        var left = float.MaxValue;
        var top = float.MaxValue;
        var right = float.MinValue;
        var bottom = float.MinValue;

        foreach (var point in lassoPath)
        {
            left = MathF.Min(left, point.X);
            top = MathF.Min(top, point.Y);
            right = MathF.Max(right, point.X);
            bottom = MathF.Max(bottom, point.Y);
        }

        return new SKRect(left, top, right, bottom);
    }

    /// <summary>
    /// Picks a set of strokes up, or reports that the selection is already exactly that. Reading the
    /// loop is what makes this the moment the selection changes - which the page is told, because the
    /// offer to throw the selection away stands and falls with it.
    /// </summary>
    /// <returns>Whether the selection changed.</returns>
    private bool Select(HashSet<InkStroke> picked)
    {
        if (selection.Count == picked.Count && selection.SetEquals(picked))
        {
            return false;
        }

        selection.Clear();
        selection.UnionWith(picked);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Whether the contact started on the picked-up group rather than outside it.</summary>
    private bool IsInsideSelection(SKPoint point) =>
        TryBounds(selection, SelectionFrameMargin, out var bounds)
        && point.X >= bounds.Left && point.X <= bounds.Right
        && point.Y >= bounds.Top && point.Y <= bounds.Bottom;

    /// <summary>
    /// Starts carrying the picked-up group from this point. Where every stroke stood is kept as it is
    /// now, because undo has to bring the group back exactly there - see <see cref="EndSelectionDrag"/>.
    /// </summary>
    private void BeginSelectionDrag(SKPoint from)
    {
        var drag = new SelectionDrag { Start = from };

        foreach (var stroke in selection)
        {
            drag.Strokes.Add(stroke);
            drag.Before.Add(stroke.Points.ToList());
        }

        selectionDrag = drag;
    }

    /// <summary>
    /// Carries the picked-up group by however far the contact has moved since it touched down. The
    /// points are written over as the contact moves - that is what makes the group follow the pen
    /// without the note being redrawn from a copy of it - and the points the group had are the ones
    /// from the moment it was picked up, not from the move before this one.
    /// </summary>
    private void DragSelection(SKPoint to)
    {
        if (selectionDrag is not { } drag)
        {
            return;
        }

        var dx = to.X - drag.Start.X;
        var dy = to.Y - drag.Start.Y;

        if (dx == 0 && dy == 0)
        {
            return;
        }

        drag.Moved = true;

        for (var s = 0; s < drag.Strokes.Count; s++)
        {
            // Every point is worked out from the one the group had when it was picked up plus how far
            // the contact has come since, rather than from the move before this one: the group then
            // stands exactly where the pen is however many moves it took to get there.
            var before = drag.Before[s];
            var points = drag.Strokes[s].Points;

            for (var i = 0; i < before.Count && i < points.Count; i++)
            {
                var point = before[i];
                point.X += dx;
                point.Y += dy;
                points[i] = point;
            }
        }
    }

    /// <summary>
    /// Ends a drag: the group is where it was carried to, and the move goes into the history as one
    /// edit, however far it went and however many strokes it carried. A contact that carried the group
    /// nowhere but touched down on it is not a move at all: it leaves no entry behind and puts the
    /// group down, because a group that stays picked up would swallow the next touch on it.
    /// </summary>
    private void EndSelectionDrag()
    {
        if (selectionDrag is not { } drag)
        {
            return;
        }

        selectionDrag = null;

        if (!drag.Moved)
        {
            // A contact that went down on the group and up again without carrying it anywhere said no
            // more than "this group is done with", the same as one that landed beside it - see
            // DropPutDownTap. It is put down there and then. Left picked up, the frame would be drawn
            // around the strokes and take hold of the next contact that lands inside it: what is drawn
            // there would be moved instead of written, and what is rubbed out there would be moved
            // instead of erased, which is a frame that clings to its strokes.
            ClearSelection();
            return;
        }

        var moves = new List<InkStrokeMove>(drag.Strokes.Count);
        for (var i = 0; i < drag.Strokes.Count; i++)
        {
            moves.Add(new InkStrokeMove(drag.Strokes[i], drag.Before[i], drag.Strokes[i].Points.ToList()));
        }

        // An eraser sweep that is still open is the change before this one, so it goes into the history
        // first - see StartStroke.
        EndEraseSession();

        commands.RecordMoved(moves);

        // The strokes are the same ones in the same places of the set, but where on the note they are
        // has changed, so this is a change to the note like any other.
        StrokeCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An axis-aligned box around a set of strokes, in document units, wide enough to take in
    /// the width they are drawn in and standing <paramref name="margin"/> off them.</summary>
    private static bool TryBounds(IEnumerable<InkStroke> strokes, float margin, out SKRect bounds)
    {
        var left = float.MaxValue;
        var top = float.MaxValue;
        var right = float.MinValue;
        var bottom = float.MinValue;
        var any = false;

        foreach (var stroke in strokes)
        {
            var reach = margin + (stroke.Thickness / 2f);

            foreach (var point in stroke.Points)
            {
                left = MathF.Min(left, point.X - reach);
                top = MathF.Min(top, point.Y - reach);
                right = MathF.Max(right, point.X + reach);
                bottom = MathF.Max(bottom, point.Y + reach);
                any = true;
            }
        }

        bounds = any ? new SKRect(left, top, right, bottom) : SKRect.Empty;
        return any;
    }

    /// <summary>
    /// A group that is being carried, together with where every stroke of it stood when it was picked
    /// up. Kept as a plain object rather than as an entry in the history: it only becomes an edit once
    /// the contact ends - see <see cref="EndSelectionDrag"/>.
    /// </summary>
    private sealed class SelectionDrag
    {
        /// <summary>Where the contact that carries the group touched down, in document units.</summary>
        public SKPoint Start { get; init; }

        /// <summary>The strokes being carried, in the order their own points were copied.</summary>
        public List<InkStroke> Strokes { get; } = new();

        /// <summary>The points each of them had when it was picked up.</summary>
        public List<List<InkPoint>> Before { get; } = new();

        /// <summary>Whether the group went anywhere at all.</summary>
        public bool Moved { get; set; }
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

    /// <summary>
    /// Whether the last point of <paramref name="stroke"/> is far enough from the one before it for the
    /// pen to have been going somewhere - see <see cref="ShapeHoldMovement"/>.
    /// </summary>
    private static bool Traveled(InkStroke stroke)
    {
        var points = stroke.Points;
        if (points.Count < 2)
        {
            return false;
        }

        var dx = points[^1].X - points[^2].X;
        var dy = points[^1].Y - points[^2].Y;
        return (dx * dx) + (dy * dy) > ShapeHoldMovement * ShapeHoldMovement;
    }

    /// <summary>
    /// Whether the contact that put a picked-up group down has gone anywhere since it touched down,
    /// which is what it has to do before it writes: the press that puts a group down is a tap on the
    /// note like any other, and one that only says the group is done with must not leave a mark of its
    /// own - see <see cref="DropPutDownTap"/>.
    /// </summary>
    private bool LeftPutDownSlop(InkStroke stroke, SKPoint to)
    {
        if (stroke.Points.Count == 0)
        {
            return false;
        }

        var dx = to.X - stroke.Points[0].X;
        var dy = to.Y - stroke.Points[0].Y;
        return (dx * dx) + (dy * dy) > PutDownSlop * PutDownSlop;
    }

    /// <summary>
    /// Takes back the dot a contact began that never went anywhere, for the contact that put a
    /// picked-up group down: it said no more than "this group is done with", and a mark left behind for
    /// that would be one nobody asked for. A contact that went somewhere cleared itself as the one that
    /// put the group down as it did so, so what it drew is a stroke and stays.
    /// </summary>
    private void DropPutDownTap(long contactId)
    {
        if (contactId == 0 || contactId != putDownContactId)
        {
            return;
        }

        putDownContactId = 0;
        DropCurrentStroke();
    }

    private void AddPoint(InkStroke stroke, SKTouchEventArgs e)
    {
        var point = ToDocument(e.Location);
        stroke.Points.Add(new InkPoint
        {
            X = point.X,
            Y = point.Y,

            // What the platform reported wins over what came with the touch event, because on Apple
            // and on Windows the latter is a constant 1 - see SetStylusPressure. A device that has
            // neither - a finger, a mouse, a pen with no force sensor - leaves the point at full
            // width rather than at nothing, so the stroke is drawn as wide as the ladder says.
            Pressure = penPressure > 0 ? penPressure : e.Pressure > 0 ? e.Pressure : 1f,
            Tilt = penTilt,
            Azimuth = penAzimuth,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });

        // A device that reports nothing for the first point of a stroke never puts it right while it
        // is still the only one - see SetStylusPressure. The stroke has to move on without it then,
        // and the press of the point just added is the closest thing to the one that is missing: the
        // pen is pressed about as hard a millisecond later, and that is far closer than the full
        // width the first point would otherwise keep for the whole stroke.
        if (stroke.Points.Count > 1 && (firstPointAwaitsPressure || firstPointAwaitsTilt))
        {
            var first = stroke.Points[0];

            if (firstPointAwaitsPressure)
            {
                var pressure = penPressure > 0 ? penPressure : e.Pressure;
                if (pressure > 0)
                {
                    first.Pressure = pressure;
                }

                firstPointAwaitsPressure = false;
            }

            if (firstPointAwaitsTilt)
            {
                first.Tilt = penTilt;
                first.Azimuth = penAzimuth;
                firstPointAwaitsTilt = false;
            }

            stroke.Points[0] = first;
        }
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
            var baseThickness = TiltSensitiveWidth ? stroke.Thickness * TiltWidthFactor(first.Tilt) : stroke.Thickness;

            // Whether the pressure shapes this stroke is asked of the stroke itself, not of the setting:
            // the setting says how the stroke that is drawn next looks, and it must not reach back into
            // the strokes that are already on the note - that is what made a note change its appearance
            // when the switch was flipped. Only strokes from before they carried this have no answer of
            // their own and keep following the setting, exactly as they did when they were drawn.
            var pressureShapesWidth = stroke.PressureSensitiveWidth ?? PressureSensitiveWidth;

            if (stroke.Points.Count == 1)
            {
                // Render a dot for a single tap - the nib itself, so a tilted pen leaves the mark its
                // shape suggests.
                if (TiltRenderingEffect && first.Tilt > MinimumTilt)
                {
                    StampNib(canvas, nibPaint, first.X, first.Y, first.Tilt, first.Azimuth, baseThickness);
                }
                else
                {
                    canvas.DrawCircle(first.X, first.Y, Math.Max(baseThickness / 2f, 1f), nibPaint);
                }

                continue;
            }

            // One path with a single width is only right when nothing varies the width along the
            // stroke - neither the pressure, nor the lean, nor the nib of the tilt effect.
            if (!pressureShapesWidth && !TiltSensitiveWidth && !TiltRenderingEffect)
            {
                // One width for the whole stroke, as before either setting existed.
                using var pathBuilder = new SKPathBuilder();
                pathBuilder.MoveTo(new SKPoint(first.X, first.Y));
                for (int i = 1; i < stroke.Points.Count; i++)
                {
                    var point = stroke.Points[i];
                    pathBuilder.LineTo(new SKPoint(point.X, point.Y));
                }

                using var path = pathBuilder.Detach();

                paint.StrokeWidth = stroke.Thickness;
                canvas.DrawPath(path, paint);
                continue;
            }

            // Something varies the width along the stroke, so it is drawn segment by segment: one path
            // with a single StrokeWidth would flatten the whole line to one value - to whatever the pen
            // pressed last, if the width is not meant to follow the pressure at all.
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                var from = stroke.Points[i - 1];
                var to = stroke.Points[i];

                // Where the width does follow the pressure, each segment gets the mean of its two
                // endpoints' pressure, which keeps neighbouring segments close enough in width that
                // the joins are not visible. The lean is taken from the start of the segment - it
                // changes too slowly for a mean of the two to be worth it.
                var thickness = stroke.Thickness;
                if (TiltSensitiveWidth)
                {
                    thickness *= TiltWidthFactor(from.Tilt);
                }

                if (pressureShapesWidth)
                {
                    var pressure = (from.Pressure + to.Pressure) / 2f;
                    // The minimum width scales with the pen size too (a share of it), so a thick pen
                    // does not need a hard press to get a thick line.
                    const float MinPressureWidthShare = 0.4f;
                    pressure = pressure > 0 ? Math.Clamp(pressure, 0f, 1f) : 1f;
                    thickness *= MinPressureWidthShare + (1f - MinPressureWidthShare) * pressure;
                }

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

        // Both of these mark what the lasso is doing rather than write anything, so they go over the
        // ink and never into it - see DrawSelectionFrame and DrawLassoLoop.
        DrawSelectionFrame(canvas);
        DrawLassoLoop(canvas);

        canvas.Restore();
    }

    /// <summary>
    /// Puts a dashed frame around the strokes the lasso has picked up, so what a drag is about to
    /// carry - and what the row's bin would take away - is visible on the note itself rather than only
    /// in the row above it.
    /// </summary>
    private void DrawSelectionFrame(SKCanvas canvas)
    {
        if (selection.Count == 0 || !TryBounds(selection, SelectionFrameMargin, out var bounds))
        {
            return;
        }

        using var frame = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = SelectionFrameWidth,
            Color = SelectionColor,
            PathEffect = SKPathEffect.CreateDash(new[] { SelectionDash, SelectionGap }, 0f),
        };

        canvas.DrawRoundRect(bounds, SelectionFrameCorner, SelectionFrameCorner, frame);
    }

    /// <summary>
    /// Draws the loop the pen is making while it is still down. The closing edge is drawn with it: that
    /// edge is what decides which strokes are inside, and it would otherwise appear only after the pen
    /// has been lifted, which is exactly when it is too late to change anything about it.
    /// </summary>
    private void DrawLassoLoop(SKCanvas canvas)
    {
        if (lassoPath.Count < 2)
        {
            return;
        }

        using var builder = new SKPathBuilder();
        builder.MoveTo(lassoPath[0]);
        for (var i = 1; i < lassoPath.Count; i++)
        {
            builder.LineTo(lassoPath[i]);
        }

        using var path = builder.Detach();
        using var loop = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = LassoLoopWidth,
            Color = SelectionColor,
            PathEffect = SKPathEffect.CreateDash(new[] { SelectionDash, SelectionGap }, 0f),
        };

        canvas.DrawPath(path, loop);
        canvas.DrawLine(lassoPath[^1], lassoPath[0], loop);
    }

    /// <summary>
    /// Draws one segment by stamping the pen's nib along it. The nib is an ellipse whose long axis
    /// follows the lean direction and whose length grows as the pen is laid flatter, so dragging the
    /// nib sideways leaves a broad mark and dragging it along its own long axis leaves a thin one.
    /// </summary>
    private void StampNibSegment(SKCanvas canvas, SKPaint paint, InkPoint from, InkPoint to, float thickness)
    {
        var distance = Distance(from.X, from.Y, to.X, to.Y);

        // Enough stamps that the nib footprints overlap, whichever way the pen is dragged: the nib is
        // at its narrowest along its own long axis, so its width is what the spacing has to stay below.
        // Without that a fast stroke comes out as a row of dots. The pen's lean is taken from the start
        // of the segment - it changes too slowly for interpolating it per segment to be worth the cost.
        var spacing = MathF.Max(thickness * 0.75f, 0.5f);
        var steps = Math.Clamp((int)MathF.Ceiling(distance / spacing), 1, MaxNibStampsPerSegment);

        for (var step = 0; step <= steps; step++)
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
        var flatten = Flatten(tilt);
        var major = minor * (1f + (NibWidening * flatten));

        // Drawn around the origin and then rotated and moved into place, so the rotation cannot shift
        // the centre of the ellipse.
        canvas.Save();
        canvas.Translate(x, y);
        canvas.RotateRadians(azimuth);
        canvas.DrawOval(0f, 0f, major, minor, paint);
        canvas.Restore();
    }

    /// <summary>How flat the pen lies, <c>0</c> upright and <c>1</c> resting on the surface.</summary>
    private static float Flatten(float tilt) => Math.Clamp(tilt / HalfPi, 0f, 1f);

    /// <summary>
    /// What the pen's lean does to the width of the stroke itself - see
    /// <see cref="TiltSensitiveWidth"/>. An upright pen leaves the width as it is, a flat one leaves
    /// it up to <see cref="TiltWidthWidening"/> wider.
    /// </summary>
    private static float TiltWidthFactor(float tilt) => 1f + (TiltWidthWidening * Flatten(tilt));
}
