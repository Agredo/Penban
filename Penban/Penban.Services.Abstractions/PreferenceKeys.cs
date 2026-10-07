namespace Penban.Services.Abstractions;

/// <summary>
/// Every preference key the app stores, in one place. Keeping them together stops the same key
/// from being written as a string literal in several layers and silently drifting apart - the
/// settings page, the ink canvas host and the view models all have to agree on them.
/// </summary>
public static class PreferenceKeys
{
    /// <summary>Which <see cref="InkRenderer"/> a card's ink surface uses.</summary>
    public const string InkRenderer = "InkCanvas.Renderer";

    /// <summary>Paper colour the user picked last; a new note inherits it instead of a random one.</summary>
    public const string NoteLastColorIndex = "Note.LastColorIndex";

    /// <summary>Whether a finger draws. Off means only a pen draws and a finger pans the board.</summary>
    public const string AllowFingerDrawing = "Draw.AllowFingerDrawing";

    /// <summary>Whether an Apple Pencil double-tap switches to the eraser.</summary>
    public const string PencilDoubleTapEnabled = "Draw.PencilDoubleTapEnabled";

    /// <summary>Whether stylus pressure varies the line width along a stroke.</summary>
    public const string PressureSensitiveWidth = "Draw.PressureSensitiveWidth";

    /// <summary>
    /// Width of new strokes, in note units. Kept as the width itself rather than as a step of the
    /// slider, so a stroke drawn earlier keeps its width even if the range is ever changed. It is the
    /// width of the pen that is drawing, and follows <see cref="PenSlots"/> as that pen is edited.
    /// </summary>
    public const string PenThickness = "Draw.PenThickness";

    /// <summary>
    /// The pens on the editor's toolbar, written by <c>PenSlots.Format</c>: one colour and one width
    /// per pen, in the order they stand in the row.
    /// </summary>
    public const string PenSlots = "Draw.PenSlots";

    /// <summary>Which of the pens of <see cref="PenSlots"/> is drawing, as an index into the row.</summary>
    public const string ActivePenSlot = "Draw.ActivePenSlot";

    /// <summary>
    /// The colours used last, newest first, written by <c>PenColorHistory.Format</c>: the row of
    /// swatches at the head of the pen flyout, so a colour that was mixed once can be picked again
    /// without mixing it a second time. A colour is kept once a stroke has been drawn in it.
    /// </summary>
    public const string PenRecentColors = "Draw.PenRecentColors";

    /// <summary>
    /// Which of the two ways of reaching the drawing tools is in use, as a member of
    /// <see cref="InkToolUi"/>. The radial menu covers the page and takes every touch on it, and it
    /// offers what the tools and the two palettes of the row above the note offer, so the two are
    /// alternatives: the chosen one is shown and the other one is not.
    /// </summary>
    public const string InkToolUi = "Draw.InkToolUi";

    /// <summary>
    /// Whether the eraser end of a pen erases while it is held against the surface. Only Windows
    /// reports which end of the pen is down (a Surface Pen's tail); the setting is inert elsewhere.
    /// </summary>
    public const string PenTailEraserEnabled = "Draw.PenTailEraserEnabled";

    /// <summary>
    /// Whether the button on the pen itself picks strokes up while it is held - hold it, draw a loop
    /// around what is to be moved, drag it away. Only Windows says when that button is down (the
    /// barrel button of a Surface Pen); the setting is inert elsewhere.
    /// </summary>
    public const string PenButtonLassoEnabled = "Draw.PenButtonLassoEnabled";

    /// <summary>Whether cards grow to fit their ink instead of keeping a fixed note size.</summary>
    public const string AutoSizeCards = "Board.AutoSizeCards";

    /// <summary>
    /// Which of the two ways a new, empty card is written in, as a member of <c>CardContentMode</c> in
    /// lower case: <c>ink</c> or <c>text</c>. Only a wish for the next card - a card that already
    /// carries writing keeps the mode it was written in.
    /// </summary>
    public const string CardDefaultContentMode = "Card.DefaultContentMode";

    /// <summary>
    /// Whether the ink steps back while a note is being written on the keyboard. On by default: the
    /// two are the same note, and the ink showing through the field is the thing that makes a card
    /// hard to read while it is being typed into.
    /// </summary>
    public const string TextHideInkInTextMode = "Draw.HideInkInTextMode";

    /// <summary>Whether the board reads tilt data from the stylus.</summary>
    public const string TiltDetectionEnabled = "Draw.TiltDetectionEnabled";

    /// <summary>Whether stylus tilt is rendered as a calligraphy effect.</summary>
    public const string TiltRenderingEffect = "Draw.TiltRenderingEffect";

    /// <summary>
    /// Whether a stroke that is left to stand for a moment is read as the shape it was drawn as - a
    /// line, a rectangle or an ellipse - and replaced by it.
    /// </summary>
    public const string ShapeRecognitionEnabled = "Draw.ShapeRecognitionEnabled";

    /// <summary>Whether a two-finger tap on the writing surface takes back the last edit.</summary>
    public const string TwoFingerTapUndoEnabled = "Gesture.TwoFingerTapUndoEnabled";

    /// <summary>Whether a three-finger tap on the writing surface puts the last edit back.</summary>
    public const string ThreeFingerTapRedoEnabled = "Gesture.ThreeFingerTapRedoEnabled";

    /// <summary>
    /// The board a widget placed from the overview should start on. Only a wish: iOS places a widget
    /// for the user, never for the app, so this is what the configuration sheet pre-selects rather
    /// than something that puts a widget anywhere. Nothing writes it any more - the pick used to come
    /// from holding down a board card - so only a value left behind by an older version is read.
    /// </summary>
    public const string WidgetBoard = "Widget.PreferredBoard";

    /// <summary>
    /// Whether the app reads the handwriting of notes in the background so that their text can be
    /// found again. On by default: the search is the feature, and one that stays empty until a
    /// switch has been found is one nobody finds.
    /// </summary>
    public const string RecognitionEnabled = "Recognition.Enabled";

    /// <summary>
    /// Which area the app opens with, as one of the area constants of <c>AppRoutes</c> -
    /// <c>dashboard</c>, <c>projects</c> or <c>boards</c>. The dashboard is what a fresh install gets:
    /// it is the only one of the three that shows something of both of the others. A value that names
    /// no area - or none at all, as in a version that did not have this setting - falls back to it.
    /// </summary>
    public const string StartPage = "App.StartPage";
}
