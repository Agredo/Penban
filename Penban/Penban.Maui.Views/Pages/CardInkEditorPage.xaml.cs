using Microsoft.Maui.Controls.Shapes;
using Penban.Maui.Views.Controls;
using Penban.Maui.Views.Ink;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;
using Penban.ViewModels;
using System.Globalization;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Dedicated ink editor for one note, opened from the board to keep Kanban card templates
/// lightweight. The note is either a card of the board or the board's own note - both are written
/// the same way, and only the target behind <see cref="INoteEditorTarget"/> says which of the two
/// the ink is written back to.
/// </summary>
public partial class CardInkEditorPage : ContentPage
{
    /// <summary>Idle time after the last stroke before the card is written to the database.</summary>
    private const int SaveIdleMilliseconds = 800;

    /// <summary>Edge length of one paper colour swatch in the picker.</summary>
    private const double SwatchSize = 34;

    /// <summary>Diameter of one pen colour swatch.</summary>
    private const double PenSwatchSize = 26;

    /// <summary>
    /// How far a drag has to go before letting go leaves the editor, as a share of the note's own
    /// height. Measured on the note rather than from where the finger landed, so a swipe from the
    /// upper half of the note is asked for the same drag as one from the lower half - and so a
    /// larger note asks for a larger drag, the way putting a larger sheet away does.
    /// </summary>
    private const double CloseSwipeTravelShare = 0.35;

    /// <summary>
    /// Where the finger has to arrive at the latest: four fifths of the way down the cell the card
    /// is drawn in, the bottom fifth of the screen. It caps what a drag can be asked for, because a
    /// finger that landed low has only that much of the screen left to cross, and it is far enough
    /// down that a hand merely crossing the note cannot reach it.
    /// </summary>
    private const double CloseSwipeBottomShare = 0.8;

    /// <summary>
    /// The least a drag has to move, as a share of that cell's height, however low the finger
    /// started. A finger that landed inside the bottom fifth is already where the drag is supposed
    /// to end, and without a distance of its own the card would be thrown away by a touch that
    /// barely moved - nothing to see of the card leaving.
    /// </summary>
    private const double CloseSwipeMinimumTravel = 0.08;

    /// <summary>
    /// How much smaller the card gets by the time it has been dragged far enough to close. It keeps
    /// shrinking as it is carried past that point, down to <see cref="CloseSwipeExitScale"/>, so a
    /// card on its way out visibly recedes the way a sheet being put away does.
    /// </summary>
    private const double CloseSwipeShrink = 0.35;

    /// <summary>Smallest the card gets while it is being dragged, however far the finger goes.</summary>
    private const double CloseSwipeExitScale = 0.5;

    /// <summary>
    /// How much of the finger's travel the card still follows once it has been dragged far enough
    /// to close. Past that point there is nothing left to reach, so it only gives a little instead
    /// of being pulled over the toolbar.
    /// </summary>
    private const double CloseSwipeOvershoot = 0.3;

    /// <summary>How long the card takes to slide the rest of the way out of the editor.</summary>
    private const uint CloseSwipeLeaveMilliseconds = 200;

    /// <summary>
    /// How long the card takes to spring back when the drag did not go far enough. Longer than
    /// leaving, so a drag that was called off reads as the card being let go rather than as it
    /// stopping dead halfway. Only growing back springs: an easing that overshot would carry the
    /// card past the place it rests and over the row above it, by as much of the drag as was
    /// carried out, which reads as the card being thrown rather than let go.
    /// </summary>
    private const uint CloseSwipeReturnMilliseconds = 280;

    /// <summary>
    /// Ink is dark on paper in both themes, so the ring that marks the picked pen colour cannot
    /// come from the theme's accent - it would vanish on a dark swatch.
    /// </summary>
    private const string InkColor = "#1E2230";

    /// <summary>
    /// Edge length of the largest dot the ring's width block draws, in device units. The widest widths
    /// would be drawn wider than the segment carrying them, so past a point the dots only hint at the
    /// width - the number that stands in the middle of the menu is what says it exactly.
    /// </summary>
    private const double ThicknessDotMaxSize = 26;

    /// <summary>
    /// Width the pen flyout is drawn at: wide enough for five swatches of the palette in a row and for
    /// a hex code to be typed into, and narrow enough to hang over the note without covering it whole.
    /// </summary>
    private const double PenFlyoutWidth = 272;

    /// <summary>How far the flyout is kept from the top and the bottom of the cell it hangs in.</summary>
    private const double PenFlyoutMargin = 12;

    /// <summary>
    /// How far a drag of a pen has to go to cross the whole range a pen can be set to, in device units.
    /// Short enough that a drag made on a pen in a row at the top of the screen reaches both ends
    /// without the finger having to leave it, and long enough that the width does not jump about under
    /// a finger that is unsteady rather than being dragged.
    /// </summary>
    private const double PenDragTravel = 120;

    /// <summary>Width a drag of a pen gives one device unit it is dragged up by.</summary>
    private const float PenWidthPerPoint = (PenThickness.Maximum - PenThickness.Minimum) / (float)PenDragTravel;

    /// <summary>
    /// How long after a pen was dragged a press of it is read as the drag being let go rather than as a
    /// press. Only long enough to swallow the click a drag ends on where a platform sends one.
    /// </summary>
    private const long PenDragPressGrace = 300;

    /// <summary>
    /// The blocks of the radial menu, in the order they sit on the ring from the top clockwise: the
    /// two that hold choices, then the three that act the moment they are taken.
    /// </summary>
    private const int ColorArc = 0;
    private const int ThicknessArc = 1;
    private const int RedoArc = 2;
    private const int UndoArc = 3;
    private const int ModeArc = 4;

    /// <summary>
    /// Pen colours offered in the flyout and on the ring, dark enough to read on every paper colour.
    /// The palette wraps in the flyout and fans out on the ring, so it can be as wide as a real pen
    /// case without the row above the note growing with it.
    /// </summary>
    private static readonly (string Hex, string Name)[] PenColors =
    [
        ("#000000", Strings.PenColorBlack),
        ("#1B4FD8", Strings.PenColorBlue),
        ("#C62828", Strings.PenColorRed),
        ("#1B7F3B", Strings.PenColorGreen),
        ("#E07000", Strings.PenColorOrange),
        ("#6A3FC0", Strings.PenColorPurple),
        ("#0F7B8A", Strings.PenColorTeal),
        ("#6D4C2F", Strings.PenColorBrown),
        ("#C2185B", Strings.PenColorPink),
        ("#4A5568", Strings.PenColorGray),
    ];

    private readonly INoteEditorTarget noteTarget;
    private readonly IPreferences preferences;
    private readonly SettingsViewModel settingsViewModel;
    private readonly FeedbackViewModel feedbackViewModel;
    private readonly List<StickyNoteBorder> swatches = [];
    private readonly Dictionary<string, Button> tagButtons = [];
    private readonly List<Border> penSlotFrames = [];
    private readonly List<PenGlyphView> penSlotGlyphs = [];
    private readonly List<Label> penSlotChevrons = [];

    /// <summary>The width drag of each pen, held here so it can be put on the pen in hand alone.</summary>
    private readonly List<PanGestureRecognizer> penSlotPans = [];
    private readonly List<Border> paletteSwatches = [];
    private readonly List<Border> recentSwatches = [];

    /// <summary>The pens of the row, in the order they stand in it. Read once, written back on every edit.</summary>
    private PenSlot[] penSlots = [];

    /// <summary>The colours used last, newest first - the row at the head of the flyout.</summary>
    private IReadOnlyList<string> recentColors = [];

    /// <summary>Which of <see cref="penSlots"/> is drawing. What the row frames and the flyout edits.</summary>
    private int activePenSlot;

    /// <summary>
    /// Set while the pen's own controls are being written to - the flyout's and the row's - so a
    /// control that is being moved onto the pen does not report the move as a choice and write it
    /// straight back.
    /// </summary>
    private bool isUpdatingPenControls;

    /// <summary>
    /// Width the pen was set to when the drag of it began, and which pen it was dragging. The width
    /// the finger is on is measured from where the drag started rather than added up as it goes, so
    /// half a drag up is the same width however fast it was made, and a drag that wanders sideways
    /// and back leaves the pen where it was.
    /// </summary>
    private float penDragThickness;
    private int penDragSlot = -1;

    /// <summary>
    /// Whether the drag has moved far enough up or down to be about the width at all, and which pen
    /// was dragged last and when. A drag that only went sideways is not one: the row of tools scrolls
    /// that way, and a finger crossing the pen must not leave a different width behind it. The pen
    /// that was just dragged is also held off from taking a press for a moment, because the click that
    /// ends a drag comes through as a press of its own on some platforms.
    /// </summary>
    private bool penDragAdjusted;
    private int lastPenDragSlot = -1;
    private long lastPenDragTicks;

    private int saveVersion;
    private bool isClosing;

    /// <summary>
    /// How far the card has been dragged towards leaving, <c>0</c> to <c>1</c>. Kept so the card
    /// knows, when the finger lifts, whether it was let go far enough down to leave or has to go
    /// back to where it was.
    /// </summary>
    private double swipeProgress;

    /// <summary>
    /// How far down the card is being held right now, and whether a finger is holding it. The offset
    /// is remembered here rather than read back off the card, because the card may already be in the
    /// middle of an animation by the time the finger lets go.
    /// </summary>
    private double dragOffset;
    private bool isDraggingCard;

    /// <summary>
    /// Where the radial menu is standing, in the units of the page, or <c>null</c> while it is down.
    /// Kept because the menu is put up again in the same place whenever a choice is taken, so it
    /// stays where the finger left it instead of jumping to the middle of the page.
    /// </summary>
    private Point? radialMenuOrigin;

    public CardInkEditorPage(
        INoteEditorTarget noteTarget,
        IPreferences preferences,
        SettingsViewModel settingsViewModel,
        FeedbackViewModel feedbackViewModel,
        string boardTitle)
    {
        InitializeComponent();
        this.noteTarget = noteTarget;
        this.preferences = preferences;
        this.settingsViewModel = settingsViewModel;
        this.feedbackViewModel = feedbackViewModel;

        // The shell's bar stands above this page and reads its title, and the title that says where the
        // back button leads is the board the card came from.
        Title = boardTitle;

        // Attaching the store also restores the renderer and the drawing settings, so it has to
        // happen before anything is loaded into the host.
        InkHost.Preferences = preferences;
        InkHost.LoadStrokes(noteTarget.InkCanvas.Strokes);
        InkHost.StrokeCompleted += OnStrokeCompleted;

        // One event for the tool, whichever way it was changed: a button, a pen button, a pencil tap.
        // Whether the lasso holds anything is its own question - the offer to throw it away comes and
        // goes with it - so that is a second one.
        InkHost.ToolChanged += OnToolChanged;
        InkHost.SelectionChanged += OnSelectionChanged;

        // A finger dragged down takes the card with it and lets it go: the renderer raises both,
        // because only it sees the individual fingers. It is only ever read with finger drawing off,
        // where the finger has no stroke to draw.
        InkHost.CloseSwipeMoved += OnCloseSwipeMoved;
        InkHost.CloseSwipeEnded += OnCloseSwipeEnded;
        InkHost.CloseOnSwipeDown = true;

        // The menu is asked for from the note itself - a tap of a finger that is not drawing, or a
        // press of the right mouse button where there is one - and the page is the side that owns it,
        // so the request comes here and the ring is put up by the page. See OnMenuRequested. Whether
        // the note asks for it at all is not decided here but by ApplyToolUi, further down.
        InkHost.MenuRequested += OnMenuRequested;
        RadialMenu.ChoiceRequested += OnRadialMenuChoiceRequested;

        NoteSurface.NoteColorIndex = noteTarget.NoteColorIndex;
        NoteArea.SizeChanged += OnNoteAreaSizeChanged;

        // The pens are read before the row is built from them, and the pen that was drawing is handed
        // to the surface before anything can be drawn with it. The row's width slider walks the range a
        // pen can be set to, read from the one place that range is written down and not repeated in the
        // markup, so the row and the flyout can never offer two different ranges. Giving a slider its
        // range moves its value - from zero onto the narrowest width there is - and that move is
        // reported like a drag of the control, so the range is given without being read back: a width
        // reported from here, before there is a pen to put it into, would be written into no pen at all.
        penSlots = ReadPenSlots();
        WithoutPenFeedback(() =>
        {
            ToolbarThicknessSlider.Minimum = PenThickness.Minimum;
            ToolbarThicknessSlider.Maximum = PenThickness.Maximum;
        });
        recentColors = PenColorHistory.Parse(preferences.Get(PreferenceKeys.PenRecentColors, string.Empty));
        activePenSlot = ReadActivePenSlot();
        BuildColorPicker();
        BuildTagPicker();
        BuildPenSlots();
        BuildPenFlyout();
        ApplyPenSlot();
        ApplyToolUi();
        UpdateToolButtons();

#if IOS
        // Apple Pencil double-tap switches to the eraser, and the pencil's tilt feeds the
        // calligraphy effect while how hard it is pressed feeds the line width. Only iOS exposes the
        // double-tap; Android's S Pen button has no equivalent.
        InkHost.Behaviors.Add(new PencilTapBehavior(preferences));
        InkHost.Behaviors.Add(new PenInputBehavior(preferences));

        // Two-finger tap undoes, three-finger tap redoes.
        InkHost.Behaviors.Add(new MultiFingerTapBehavior());
#endif

#if WINDOWS
        // The eraser end of a Surface Pen erases while it is held against the surface, and writes
        // again as soon as it is lifted; the button on its side picks strokes up for as long as it is
        // held down. Two-finger tap undoes, three-finger tap redoes, and the pen's tilt and pressure
        // feed the calligraphy effect and the line width.
        InkHost.Behaviors.Add(new PenTailEraserBehavior(preferences));
        InkHost.Behaviors.Add(new PenButtonLassoBehavior(preferences));
        InkHost.Behaviors.Add(new MultiFingerTapBehavior());
        InkHost.Behaviors.Add(new PenInputBehavior(preferences));
#endif

        // Android needs nothing here: its tilt and its two- and three-finger taps are read out of
        // the activity's touch stream instead, because the drawing surface keeps every touch to
        // itself - see AndroidInkInput.
    }

    /// <summary>
    /// Keeps the writing surface square, exactly like the note on the board. The available space is
    /// read from the surrounding cell so the note size can never influence the cell size, which
    /// would otherwise make the two sizes chase each other on every layout pass.
    /// </summary>
    private void OnNoteAreaSizeChanged(object? sender, EventArgs e)
    {
        // The flyout is placed against the cell it hangs in, so a cell that changed size puts it
        // somewhere the pen it belongs to no longer is.
        ClosePenFlyout();

        var padding = NoteFrame.Padding;
        var side = Math.Floor(Math.Min(NoteArea.Width - padding.HorizontalThickness, NoteArea.Height - padding.VerticalThickness));
        if (side <= 0 || Math.Abs(NoteSurface.WidthRequest - side) < 1)
        {
            return;
        }

        NoteSurface.WidthRequest = side;
        NoteSurface.HeightRequest = side;
    }

    /// <summary>
    /// How wide the note is drawn, in device units. The note is a square and the ink document is a
    /// square of <see cref="InkDocument.Size"/>, so this is what turns the surface's document units
    /// into the distance a finger has actually travelled on the glass - one to one, however the
    /// window happens to be shaped.
    /// </summary>
    private double NoteSide => NoteSurface.WidthRequest > 0
        ? NoteSurface.WidthRequest
        : Math.Min(NoteArea.Width, NoteArea.Height);

    /// <summary>
    /// Fills the picker with one small note per paper colour, so choosing a colour looks like
    /// picking a note off the pad instead of operating a control.
    /// </summary>
    private void BuildColorPicker()
    {
        for (var index = 0; index < StickyNoteBorder.NoteColors.Count; index++)
        {
            var swatch = new StickyNoteBorder
            {
                NoteColorIndex = index,
                NoteTilt = 0,
                WidthRequest = SwatchSize,
                HeightRequest = SwatchSize,
            };

            var chosen = index;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => ApplyNoteColor(chosen);
            swatch.GestureRecognizers.Add(tap);

            swatches.Add(swatch);
            ColorPicker.Add(swatch);
        }

        UpdateColorPicker();
    }

    /// <summary>Fills the tag row with one button per emoji; a pinned tag wears a tinted background.</summary>
    private void BuildTagPicker()
    {
        TagSeparator.IsVisible = noteTarget.SupportsTags;
        TagPicker.IsVisible = noteTarget.SupportsTags;
        if (!noteTarget.SupportsTags)
        {
            return;
        }

        foreach (var tag in CardTags.Palette)
        {
            var button = new Button
            {
                Text = tag,
                FontSize = 20,
                Style = (Style)Application.Current!.Resources["GhostIconButton"],
            };
            button.Clicked += (_, _) => ApplyTag(tag);
            tagButtons[tag] = button;
            TagPicker.Add(button);
        }

        UpdateTagPicker();
    }

    private void ApplyTag(string tag)
    {
        if (!noteTarget.ToggleTag(tag))
        {
            return;
        }

        UpdateTagPicker();
        QueueSave();
    }

    private void UpdateTagPicker()
    {
        foreach (var (tag, button) in tagButtons)
        {
            button.BackgroundColor = noteTarget.Tags.Contains(tag) ? Color.FromArgb("#33808080") : Colors.Transparent;
        }
    }

    /// <summary>Repaints the writing surface and remembers the choice on the card.</summary>
    private void ApplyNoteColor(int index)
    {
        // Stored before the guard: the next new note should start in this colour even if this
        // card already happened to have it.
        preferences.Set(PreferenceKeys.NoteLastColorIndex, index.ToString());

        if (noteTarget.NoteColorIndex == index)
        {
            return;
        }

        noteTarget.NoteColorIndex = index;
        NoteSurface.NoteColorIndex = index;
        UpdateColorPicker();

        // The colour travels with the card, so it has to reach the database like a stroke does.
        QueueSave();
    }

    /// <summary>Marks the active colour: the chosen note is raised and outlined, like a picked-up slip.</summary>
    private void UpdateColorPicker()
    {
        var chosen = noteTarget.NoteColorIndex;
        for (var index = 0; index < swatches.Count; index++)
        {
            var isChosen = index == chosen;
            swatches[index].Stroke = isChosen ? new SolidColorBrush(Color.FromArgb("#1E2230")) : null;
            swatches[index].StrokeThickness = isChosen ? 3 : 0;
            swatches[index].Scale = isChosen ? 1.12 : 1;
        }
    }

    /// <summary>
    /// Puts the pens in the row: one per stored slot, drawn as the pen itself - a cap and a tip in the
    /// pen's colour with a white blade between them, the blade as wide as the pen writes - and a small
    /// chevron at the corner of the one that is drawing, which is what says that pressing it again
    /// opens it. The row is built once and the pens are edited in place from then on, see
    /// <see cref="UpdatePenSlots"/>.
    /// <para>
    /// Each pen also carries the drag that changes its width, but only the pen that is drawing wears
    /// it: a drag of the other pen would either do nothing or take it up and change it in one go, and
    /// either way the row would stop scrolling under a finger that landed on it.
    /// </para>
    /// </summary>
    private void BuildPenSlots()
    {
        for (var index = 0; index < penSlots.Length; index++)
        {
            var glyph = new PenGlyphView { Style = (Style)Application.Current!.Resources["PenGlyph"] };
            var chevron = new Label
            {
                Text = IconFont.ChevronDown,
                Style = (Style)Application.Current!.Resources["IconLabel"],
                FontSize = 12,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(0, 0, 1, 1),
            };

            var frame = new Border
            {
                Style = (Style)Application.Current!.Resources["PenSlot"],
                Content = new Grid { Children = { glyph, chevron } },
            };

            var chosen = index;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => OnPenSlotTapped(chosen);
            frame.GestureRecognizers.Add(tap);

            // Kept to one side rather than hung on the frame, because it is put on and taken off again
            // whenever the pen that is drawing changes - see UpdatePenSlots.
            var drag = new PanGestureRecognizer();
            drag.PanUpdated += (_, e) => OnPenSlotPanned(chosen, e);

            penSlotFrames.Add(frame);
            penSlotGlyphs.Add(glyph);
            penSlotChevrons.Add(chevron);
            penSlotPans.Add(drag);
            PenSlotRow.Add(frame);
        }

        UpdatePenSlots();
    }

    /// <summary>
    /// What a press on one of the pens does. The pen that is not in hand is taken up - one press
    /// switches pens - and the pen that already is opens its own flyout, so the row switches pens
    /// without a second press and still has a way into the colour and the width of the pen in hand.
    /// <para>
    /// A press that arrives right behind a drag of the same pen is the drag being let go, and is
    /// dropped: a pen that was just pulled wider is not then to open its flyout over the note.
    /// </para>
    /// </summary>
    private void OnPenSlotTapped(int index)
    {
        if (index == lastPenDragSlot && Environment.TickCount64 - lastPenDragTicks < PenDragPressGrace)
        {
            return;
        }

        if (index != activePenSlot)
        {
            activePenSlot = index;
            ApplyPenSlot();
            UpdatePenSlots();
            return;
        }

        if (PenFlyout.IsVisible)
        {
            ClosePenFlyout();
            return;
        }

        ShowPenFlyout();
    }

    /// <summary>
    /// Drags the width of the pen that is drawing: up is wider, down is narrower, and the distance from
    /// where the drag began is what the width is, not how much the finger has moved since the last
    /// event. So the pen follows one-to-one and there is nothing to catch up on, and letting go of a
    /// drag that was taken back to where it started leaves the width it began with.
    /// <para>
    /// A drag that goes no further up or down than it does sideways is not about the width at all - the
    /// row of tools scrolls that way - so nothing is changed for one, and nothing is written to the
    /// store. Nothing is written while the drag runs either: the pen and the number beside it follow
    /// the finger, and the pen is only handed to the surface and to the store when it is let go.
    /// </para>
    /// </summary>
    private void OnPenSlotPanned(int index, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                penDragThickness = penSlots[index].Thickness;
                penDragAdjusted = false;
                penDragSlot = index;
                break;

            case GestureStatus.Running when penDragSlot == index:
                if (!penDragAdjusted && Math.Abs(e.TotalY) <= Math.Abs(e.TotalX))
                {
                    break;
                }

                penDragAdjusted = true;
                PreviewPenThickness(penDragThickness + ((float)-e.TotalY * PenWidthPerPoint));
                break;

            case GestureStatus.Completed or GestureStatus.Canceled when penDragSlot == index:
                lastPenDragSlot = index;
                lastPenDragTicks = Environment.TickCount64;
                penDragSlot = -1;

                if (penDragAdjusted)
                {
                    // One write to the store, and the pen in hand handed to the surface: the width was
                    // being shown all along, but it is only from here that it is the pen's.
                    SetPenThickness(penSlots[index].Thickness);
                }

                penDragAdjusted = false;
                break;
        }
    }

    /// <summary>
    /// Puts a width into the pen in hand without handing it to the surface or writing it to the store,
    /// for a width that is still under a finger - the pen and the number are what shows it, see
    /// <see cref="SetPenThickness"/> for the write that ends it.
    /// </summary>
    private void PreviewPenThickness(float thickness)
    {
        penSlots[activePenSlot] = penSlots[activePenSlot] with
        {
            Thickness = PenThickness.Snap(thickness, PenThickness.SliderStep),
        };

        UpdatePenSlots();

        if (PenFlyout.IsVisible)
        {
            UpdatePenFlyout();
        }
    }

    /// <summary>
    /// Draws the pens as they are set and frames the one that is drawing, so the row says which pen is
    /// in hand without a word for it: the frame stands out and the chevron inside it is the one that
    /// opens the pen. Each of them is named for a screen reader as the colour and the width it holds,
    /// because a pen is both of those together and neither of them is in its drawing alone.
    /// <para>
    /// The width of the pen in hand is shown beside the pens as well - the slider that walks it and the
    /// number it stands at - and every change to a pen comes through here, so those two can never show
    /// a width the pen is not set to. The drag that changes the width is hung on the pen in hand here
    /// too, and only on that one, see <see cref="BuildPenSlots"/>.
    /// </para>
    /// </summary>
    private void UpdatePenSlots()
    {
        var resources = Application.Current!.Resources;
        var active = penSlots[activePenSlot].Thickness;

        WithoutPenFeedback(() =>
        {
            ToolbarThicknessSlider.Value = active;
            ToolbarThicknessValue.Text = FormatThickness(active);
        });

        for (var index = 0; index < penSlotFrames.Count; index++)
        {
            var slot = penSlots[index];
            var isActive = index == activePenSlot;

            penSlotGlyphs[index].Color = Color.FromArgb(slot.ColorHex);
            penSlotGlyphs[index].Thickness = slot.Thickness;
            penSlotFrames[index].Style = (Style)resources[isActive ? "PenSlotActive" : "PenSlot"];
            penSlotChevrons[index].IsVisible = isActive;

            if (isActive)
            {
                if (!penSlotFrames[index].GestureRecognizers.Contains(penSlotPans[index]))
                {
                    penSlotFrames[index].GestureRecognizers.Add(penSlotPans[index]);
                }
            }
            else
            {
                penSlotFrames[index].GestureRecognizers.Remove(penSlotPans[index]);
            }

            SemanticProperties.SetDescription(
                penSlotFrames[index],
                string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.PenSlotFormat,
                    index + 1,
                    PenColorName(slot.ColorHex),
                    PenThickness.Format(slot.Thickness)));
        }
    }

    /// <summary>
    /// Hands the pen that is drawing to the surface and writes the whole row back to the store. The
    /// colour and the width of new strokes sit on the renderer rather than being read back out of the
    /// store the way the other drawing settings are, so every change to a pen has to come through
    /// here; a renderer that is swapped in later carries them across, see InkCanvasHostView.
    /// </summary>
    private void ApplyPenSlot()
    {
        var slot = penSlots[activePenSlot];

        InkHost.StrokeColor = slot.ColorHex;
        InkHost.StrokeThickness = slot.Thickness;

        preferences.Set(PreferenceKeys.ActivePenSlot, activePenSlot.ToString(CultureInfo.InvariantCulture));
        preferences.Set(PreferenceKeys.PenSlots, PenSlots.Format(penSlots));
    }

    /// <summary>
    /// Fills the flyout: the palette, the row of the colours used last, and the two pickers that are
    /// drawn here rather than bought in. The palette is the same ten colours the ring fans out and in
    /// the same order, so a colour stands in the same place whichever of the two was used to take it.
    /// </summary>
    private void BuildPenFlyout()
    {
        foreach (var (hex, name) in PenColors)
        {
            var swatch = new Border
            {
                BackgroundColor = Color.FromArgb(hex),
                StrokeShape = new Ellipse(),
                WidthRequest = PenSwatchSize,
                HeightRequest = PenSwatchSize,
            };
            SemanticProperties.SetDescription(swatch, name);

            var chosen = hex;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => SetPenColor(chosen);
            swatch.GestureRecognizers.Add(tap);

            paletteSwatches.Add(swatch);
            PenPalette.Add(swatch);
        }

        // One swatch for every colour that can be kept, hidden while there are fewer of them than that.
        for (var index = 0; index < PenColorHistory.Maximum; index++)
        {
            var swatch = new Border
            {
                StrokeShape = new Ellipse(),
                WidthRequest = PenSwatchSize,
                HeightRequest = PenSwatchSize,
            };

            var chosen = index;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                if (chosen < recentColors.Count)
                {
                    SetPenColor(recentColors[chosen]);
                }
            };
            swatch.GestureRecognizers.Add(tap);

            recentSwatches.Add(swatch);
            RecentColors.Add(swatch);
        }

        // Giving the slider its range moves its value: it starts at zero, which is below the narrowest
        // width there is, and the range pulls it up onto that minimum. The move is reported like a drag
        // of the control, so without the mark the narrowest width there is would be written into the pen
        // in hand and stored as its own - which is what made a width that was set fail to come back.
        WithoutPenFeedback(() =>
        {
            ThicknessSlider.Minimum = PenThickness.Minimum;
            ThicknessSlider.Maximum = PenThickness.Maximum;
        });

        HueStrip.HueChanged += OnHueChanged;
        ColorArea.ColorChanged += OnColorAreaChanged;

        UpdateRecentColors();
    }

    /// <summary>
    /// Says which of the recent colours are in use: the row is only ever as long as there are colours
    /// kept, so a swatch that has none is taken out of the layout rather than left standing empty.
    /// </summary>
    private void UpdateRecentColors()
    {
        for (var index = 0; index < recentSwatches.Count; index++)
        {
            var isUsed = index < recentColors.Count;
            recentSwatches[index].IsVisible = isUsed;

            if (isUsed)
            {
                recentSwatches[index].BackgroundColor = Color.FromArgb(recentColors[index]);
                SemanticProperties.SetDescription(recentSwatches[index], PenColorName(recentColors[index]));
            }
        }
    }

    /// <summary>
    /// Gives the pen in hand a colour. The pen in the other slot keeps its own and strokes already
    /// drawn keep the colour they were drawn with - it is the pen that is edited, not the ink. A code
    /// that cannot be read leaves the pen as it is.
    /// </summary>
    private void SetPenColor(string hex, bool fromFlyout = false)
    {
        if (!PenColor.TryNormalize(hex, out var normalized))
        {
            return;
        }

        penSlots[activePenSlot] = penSlots[activePenSlot] with { ColorHex = normalized };
        recentColors = PenColorHistory.Add(recentColors, normalized);
        preferences.Set(PreferenceKeys.PenRecentColors, PenColorHistory.Format(recentColors));

        // Choosing a colour is the pen being taken up again, so it takes the eraser - and the lasso
        // with it - out of the hand: whichever way the colour was picked - palette, ring, hue strip or
        // code - the next stroke is meant to be drawn in it, not rubbed out with it and not picked up
        // by it. Setting the tool is enough, the change is what puts the buttons in the toolbar back
        // in step.
        InkHost.Tool = InkTool.Pen;

        ApplyPenSlot();
        UpdatePenSlots();
        UpdateRecentColors();
        UpdatePenFlyout(fromFlyout);
    }

    /// <summary>
    /// Sets how wide the pen in hand writes. Strokes already drawn keep their width, and the width is
    /// kept on the step the slider walks in, so the stored pen is the one that was set rather than one
    /// that came out of a drag a hundredth of a point beside it.
    /// </summary>
    private void SetPenThickness(float thickness, bool fromSlider = false)
    {
        penSlots[activePenSlot] = penSlots[activePenSlot] with
        {
            Thickness = PenThickness.Snap(thickness, PenThickness.SliderStep),
        };

        ApplyPenSlot();
        UpdatePenSlots();
        UpdatePenFlyout(fromSlider);
    }

    /// <summary>
    /// Puts the pen in hand into the controls of the flyout, from wherever the pen was changed - the
    /// flyout itself, the ring, or the pen being taken up in the row - so the flyout and the pen can
    /// never disagree about what the pen is. The controls are marked as being written to while this
    /// runs, so writing to them does not come back as a choice that is applied a second time.
    /// <para>
    /// A change that was made on one of the flyout's own controls leaves all of them where they are.
    /// They already show it - the code being typed, the handle being dragged, the colour under the
    /// finger - and putting the value that was read out of them back into them would pull the work out
    /// from under the finger: a code rounded out of a half-typed one, or a handle put back onto a step
    /// while it is still being dragged. The code field is the one exception, and only in that it does
    /// not lose the mark of a code that cannot be read - that is cleared here, because a pen that was
    /// changed is a pen whose code can be read.
    /// </para>
    /// </summary>
    private void UpdatePenFlyout(bool keepControls = false)
    {
        var slot = penSlots[activePenSlot];

        WithoutPenFeedback(() =>
        {
            PenFlyoutTitle.Text = string.Format(CultureInfo.CurrentCulture, Strings.PenSlotTitleFormat, activePenSlot + 1);

            PenHexSwatch.BackgroundColor = Color.FromArgb(slot.ColorHex);
            PenHexError.IsVisible = false;

            ThicknessValue.Text = FormatThickness(slot.Thickness);
            ThicknessPreview.Color = Color.FromArgb(slot.ColorHex);
            ThicknessPreview.Thickness = slot.Thickness;

            if (!keepControls)
            {
                PenHexEntry.Text = slot.ColorHex;
                ThicknessSlider.Value = slot.Thickness;
                HueStrip.Hue = PenColor.ToHsv(slot.ColorHex).Hue;
                ColorArea.SetColor(slot.ColorHex);
            }
        });

        UpdatePenColorMarks(slot.ColorHex);
    }

    /// <summary>
    /// Writes to the pen's controls - the row's and the flyout's - without the writes being read back
    /// as choices. Every one of them reports a value that is put into it the same way it reports one a
    /// finger moves - and giving a width slider its range does move its value, from zero onto the
    /// narrowest width there is.
    /// </summary>
    private void WithoutPenFeedback(Action write)
    {
        isUpdatingPenControls = true;
        try
        {
            write();
        }
        finally
        {
            isUpdatingPenControls = false;
        }
    }

    /// <summary>
    /// Outlines the swatch that holds the colour the pen writes in - in the palette and in the row of
    /// the colours used last - and leaves the others plain.
    /// </summary>
    private void UpdatePenColorMarks(string hex)
    {
        for (var index = 0; index < paletteSwatches.Count; index++)
        {
            MarkSwatch(paletteSwatches[index], string.Equals(PenColors[index].Hex, hex, StringComparison.OrdinalIgnoreCase));
        }

        for (var index = 0; index < recentSwatches.Count; index++)
        {
            MarkSwatch(
                recentSwatches[index],
                index < recentColors.Count && string.Equals(recentColors[index], hex, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Marks a swatch as the colour the pen writes in, or leaves it plain.</summary>
    private static void MarkSwatch(Border swatch, bool isChosen)
    {
        swatch.Stroke = isChosen ? new SolidColorBrush(Color.FromArgb(InkColor)) : null;
        swatch.StrokeThickness = isChosen ? 3 : 0;
        swatch.Scale = isChosen ? 1.18 : 1;
    }

    /// <summary>The word for a colour of the palette, or the code itself for one that is not in it.</summary>
    private static string PenColorName(string hex)
    {
        foreach (var (candidate, name) in PenColors)
        {
            if (string.Equals(candidate, hex, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return hex;
    }

    /// <summary>
    /// Puts the flyout up. It hangs from the top of the note's cell, which is exactly where the row of
    /// pens above it ends, so only the sideways move is left to do and the note keeps its own height
    /// underneath. It is capped to the cell before it is shown, because how high it turns out is not
    /// known until the palette has wrapped and the note is what is left over.
    /// </summary>
    private void ShowPenFlyout()
    {
        if (NoteArea.Width > 2 * PenFlyoutMargin)
        {
            PenFlyout.WidthRequest = Math.Min(PenFlyoutWidth, NoteArea.Width - (2 * PenFlyoutMargin));
        }

        if (NoteArea.Height > 2 * PenFlyoutMargin)
        {
            PenFlyout.MaximumHeightRequest = NoteArea.Height - (2 * PenFlyoutMargin);
        }

        UpdatePenFlyout();
        PenFlyout.IsVisible = true;
        PenFlyoutBackdrop.IsVisible = true;
        PlacePenFlyout();
    }

    /// <summary>
    /// A tap outside the flyout - anywhere on the note, or on the cell around it - puts it away. The
    /// layer that catches the tap lies over the note, so the tap that dismisses the flyout does not
    /// draw on it: the first tap belongs to the flyout, which is what a popup that covers part of its
    /// own surface has to do.
    /// </summary>
    private void OnPenFlyoutBackdropTapped(object? sender, TappedEventArgs e) => ClosePenFlyout();

    /// <summary>
    /// Puts the flyout under the pen it belongs to, centred on it, with both edges of the cell
    /// respected - so a row scrolled to one end still leaves the flyout within the page rather than
    /// half outside it.
    /// </summary>
    private void PlacePenFlyout()
    {
        var width = PenFlyout.WidthRequest;
        var slot = penSlotFrames[activePenSlot];
        var centre = XInNoteArea(slot) + (slot.Width / 2);

        PenFlyout.TranslationX = Math.Clamp(
            centre - (width / 2),
            PenFlyoutMargin,
            Math.Max(PenFlyoutMargin, NoteArea.Width - width - PenFlyoutMargin));
    }

    /// <summary>
    /// Puts the flyout away. The colour and the width that were taken stay set and the flyout stays
    /// where it was, so asking for the same pen again opens it in the same place.
    /// </summary>
    private void ClosePenFlyout()
    {
        PenFlyout.IsVisible = false;
        PenFlyoutBackdrop.IsVisible = false;
    }

    /// <summary>
    /// Wipes the note in one edit, so a single undo brings the whole of it back - the same button that
    /// takes back a stroke takes back the wipe. Nothing is asked first: the button names what it does
    /// to the ink, and a question in front of it would only stand in the way of a note that is being
    /// started over. The row answers for the wipe afterwards by itself: with the ink gone there is
    /// nothing left to wipe, and undo is lit.
    /// </summary>
    private void OnClearClicked(object? sender, EventArgs e) => InkHost.EraseAll();

    /// <summary>
    /// A colour typed into the field. A code that can be read is applied at once, so the pen follows
    /// what is being typed and the note behind it can be drawn on straight away; one that cannot be
    /// read is not applied at all - the pen keeps what it had - and the field says so underneath
    /// instead of quietly taking something else.
    /// </summary>
    private void OnPenHexChanged(object? sender, TextChangedEventArgs e)
    {
        if (isUpdatingPenControls)
        {
            return;
        }

        var text = e.NewTextValue ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            // An empty field is not a mistake: it is what a code is typed into.
            PenHexError.IsVisible = false;
            return;
        }

        if (PenColor.TryNormalize(text, out var hex))
        {
            SetPenColor(hex, fromFlyout: true);
            return;
        }

        PenHexError.IsVisible = true;
    }

    /// <summary>The width the slider is dragged to, written into the pen while the finger is still down.</summary>
    private void OnThicknessSliderChanged(object? sender, ValueChangedEventArgs e)
    {
        if (isUpdatingPenControls)
        {
            return;
        }

        SetPenThickness((float)e.NewValue, fromSlider: true);
    }

    /// <summary>
    /// The width the slider in the row is dragged to. It is the flyout's slider by another way in, and
    /// goes through the same place, so dragging it keeps the pen in the row, the number beside it and
    /// the flyout - if it is open - showing the same width.
    /// </summary>
    private void OnToolbarThicknessChanged(object? sender, ValueChangedEventArgs e)
    {
        if (isUpdatingPenControls)
        {
            return;
        }

        SetPenThickness((float)e.NewValue, fromSlider: true);
    }

    /// <summary>
    /// Whether the pen's own pressure decides the width, as it is stored. Anything that cannot be read
    /// as a yes or a no is read as the yes the settings page starts from.
    /// </summary>
    private bool ReadPressureWidth() =>
        !bool.TryParse(preferences.Get(PreferenceKeys.PressureSensitiveWidth, true.ToString()), out var on) || on;

    /// <summary>
    /// Takes the pen's pressure out of the width, or puts it back. It is the same setting the settings
    /// page carries and it is written to the store the same way, so the switch there follows this one -
    /// and the surface is told here rather than waiting for the page to be opened again, because the
    /// note being drawn on is the one the change is meant for.
    /// </summary>
    private void OnPressureClicked(object? sender, EventArgs e)
    {
        var on = !ReadPressureWidth();
        preferences.Set(PreferenceKeys.PressureSensitiveWidth, on.ToString());
        InkHost.ApplyPreferences();
        UpdateToolButtons();
    }

    /// <summary>
    /// The hue of the bar being dragged. The square beside it is put onto that hue - it is the hue and
    /// the square together that make a colour - and the colour the two of them make is what the pen
    /// takes, so the pen follows the finger across the bar.
    /// </summary>
    private void OnHueChanged(object? sender, float hue)
    {
        if (isUpdatingPenControls)
        {
            return;
        }

        ColorArea.Hue = hue;
        SetPenColor(ColorArea.Color, fromFlyout: true);
    }

    /// <summary>The colour the square under the finger stands for, written into the pen as it is dragged.</summary>
    private void OnColorAreaChanged(object? sender, string hex)
    {
        if (isUpdatingPenControls)
        {
            return;
        }

        SetPenColor(hex, fromFlyout: true);
    }

    /// <summary>Keeps the tool buttons showing which mode is active and what can be undone.</summary>
    private void UpdateToolButtons()
    {
        var resources = Application.Current!.Resources;
        var tool = InkHost.Tool;
        var eraserDraws = tool == InkTool.Eraser;
        var lassoPicks = tool == InkTool.Lasso;
        var fingerDraws = InkHost.AllowFingerDrawing;

        // The one button for the two ways the pen draws carries the mode that is set - a finger
        // draws as well, or only the pencil does - and is lit while that is the finger, so the row
        // shows how the note is drawn without a second button for the mode that is not in use. It
        // goes quiet while the eraser or the lasso is the one in hand, so only ever one of the three
        // is lit.
        DrawModeButton.Text = fingerDraws ? IconFont.Finger : IconFont.Pen;
        DrawModeButton.Style =
            (Style)resources[fingerDraws && tool == InkTool.Pen ? "AccentIconButton" : "GhostIconButton"];
        SemanticProperties.SetDescription(DrawModeButton, fingerDraws ? Strings.FingerDrawing : Strings.Pen);
        EraserButton.Style = (Style)resources[eraserDraws ? "AccentIconButton" : "GhostIconButton"];
        LassoButton.Style = (Style)resources[lassoPicks ? "AccentIconButton" : "GhostIconButton"];

        // Throwing the picked-up ink away stands only while the lasso holds any: with nothing picked
        // up the button would be a second way to leave the note, and the bin it says it is would be
        // the wrong one.
        var pickedUp = InkHost.SelectionCount;
        SelectionDeleteButton.IsVisible = pickedUp > 0;
        SemanticProperties.SetDescription(SelectionDeleteButton, Strings.DeleteSelection);

        // The width is the pen's own, or it is what the pen was given and the pressure decides around
        // it - shown as the button being lit while the pen decides, and read out with it, because the
        // glyph says "pressure" without saying which way round it is set.
        var pressureWidth = ReadPressureWidth();
        PressureButton.Style = (Style)resources[pressureWidth ? "AccentIconButton" : "GhostIconButton"];
        SemanticProperties.SetDescription(
            PressureButton,
            pressureWidth ? Strings.PressureWidthOn : Strings.PressureWidthOff);

        // Nothing to take back or to put back yet, so the buttons say so instead of doing nothing.
        // The wipe is the same: with no ink on the note there is nothing for it to take away.
        UndoButton.IsEnabled = InkHost.CanUndo;
        RedoButton.IsEnabled = InkHost.CanRedo;
        ClearButton.IsEnabled = InkHost.GetStrokes().Count > 0;
    }

    private void OnStrokeCompleted(object? sender, EventArgs e)
    {
        noteTarget.InkCanvas.Clear();
        foreach (var stroke in InkHost.GetStrokes())
        {
            noteTarget.InkCanvas.AddStroke(stroke);
        }

        UpdateToolButtons();
        QueueSave();
    }

    /// <summary>
    /// Records the edit and schedules the write. Writing once per stroke reaches the database
    /// every few hundred milliseconds while drawing by hand, so a stroke only marks the card
    /// dirty and the write follows once the pen has been idle.
    /// </summary>
    private void QueueSave()
    {
        var version = ++saveVersion;
        _ = SaveAfterIdleAsync(version);
    }

    private async Task SaveAfterIdleAsync(int version)
    {
        await Task.Delay(SaveIdleMilliseconds);
        if (version != saveVersion)
        {
            // A later stroke superseded this write and queued its own.
            return;
        }

        await SaveAsync();
    }

    /// <summary>Writes the card immediately, superseding any queued idle write.</summary>
    private async Task SaveAsync()
    {
        saveVersion++;

        // A deleted card must never be written again: doing so would clear its soft-delete flag.
        if (noteTarget.IsDeleted)
        {
            return;
        }

        await noteTarget.SaveCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Reads the settings that this page does not hold itself back in. The settings page is opened
    /// from here and writes straight to the store, while the surface and the tool row were told their
    /// settings once, in the constructor - so a switch flipped over the card has to be handed to both
    /// of them here, or it would only show up on the next card. On the way in the first time this
    /// only reads the same values a second time.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        ApplyToolUi();
        InkHost.ApplyPreferences();

        // The row of tools carries two settings of its own - the width the pen is set to and whether
        // its pressure decides that width - and the second of them can be changed on the settings page
        // this page opens, so the row is put back in step with the store here, whatever was changed
        // while it was away.
        UpdatePenSlots();
        UpdateToolButtons();

        // The colour and the width of new strokes are not part of the drawing settings the surface
        // reads back - they sit on the renderer - so the pen in hand is handed to it again here,
        // whether the settings page swapped the renderer or not.
        ApplyPenSlot();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        if (isClosing)
        {
            isClosing = false;
            return;
        }

        // Leaving without "Fertig" - the shell bar's back button, the hardware back, the swipe down -
        // still keeps the ink.
        CloseRadialMenu();
        ClosePenFlyout();
        _ = SaveAsync();
    }

    /// <summary>
    /// The one button for the two ways the pen draws: it hands over between the finger drawing as
    /// well and only the pencil doing so. The glyph and the words on it follow the mode, so what is
    /// set is read off the button itself.
    /// </summary>
    private void OnDrawModeClicked(object? sender, EventArgs e) => ToggleFingerDrawing();

    /// <summary>
    /// The eraser on or off again. Off is the way back to drawing - with the finger or without it,
    /// whichever of the two the button beside it holds - so the mode the eraser took the place of is
    /// always one press away.
    /// </summary>
    private void OnEraserClicked(object? sender, EventArgs e) =>
        InkHost.Tool = InkHost.Tool == InkTool.Eraser ? InkTool.Pen : InkTool.Eraser;

    /// <summary>
    /// The lasso on or off again, the same way the eraser is. Nothing is picked up by choosing it: the
    /// loop is what picks ink up. What an earlier loop picked up is put down here, so the button that
    /// picked the group up is also the one that lets go of it, and a group is never held with no way of
    /// putting it down. The button on a pen is not this: it holds the lasso only for as long as it is
    /// down, and a group it picked up is carried by the pen afterwards - see HandlePickedUpTouch.
    /// </summary>
    private void OnLassoClicked(object? sender, EventArgs e)
    {
        InkHost.ClearSelection();

        InkHost.Tool = InkHost.Tool == InkTool.Lasso ? InkTool.Pen : InkTool.Lasso;
    }

    /// <summary>
    /// Throws the picked-up ink away as one edit, so undo brings all of it back at once - the same
    /// way a loop that picked it up is one edit when it carries it somewhere else.
    /// </summary>
    private void OnSelectionDeleteClicked(object? sender, EventArgs e)
    {
        if (InkHost.DeleteSelection())
        {
            UpdateToolButtons();
        }
    }

    /// <summary>
    /// The tool changed without a button being pressed - a pencil tap, the button on a pen, or the
    /// pen being taken up again by a colour - so the toolbar follows the host rather than the click
    /// handlers.
    /// </summary>
    private void OnToolChanged(object? sender, EventArgs e) => UpdateToolButtons();

    /// <summary>
    /// The lasso picked ink up or put it down. What it holds decides whether throwing it away is
    /// offered at all, so the one button that says so is put in step here.
    /// </summary>
    private void OnSelectionChanged(object? sender, EventArgs e) => UpdateToolButtons();

    /// <summary>
    /// Flips which tool draws, and remembers it. The radial menu offers the same switch as a block of
    /// its own, so both go through here.
    /// </summary>
    private void ToggleFingerDrawing()
    {
        var allow = !InkHost.AllowFingerDrawing;
        InkHost.AllowFingerDrawing = allow;
        preferences.Set(PreferenceKeys.AllowFingerDrawing, allow.ToString());
        UpdateToolButtons();
    }

    private void OnUndoClicked(object? sender, EventArgs e)
    {
        InkHost.Undo();
        UpdateToolButtons();
    }

    private void OnRedoClicked(object? sender, EventArgs e)
    {
        InkHost.Redo();
        UpdateToolButtons();
    }

    /// <summary>
    /// How far a view of the row above the note sits from the left edge of the note's cell. A pen hangs
    /// in the scrolling row, so its own position is measured from the stack of pens it is in and not
    /// from anything the page can use as it stands: the offsets of the views it hangs under are added
    /// up, the scroll of the row is taken off on the way - scrolling moves the pens without moving the
    /// stack that holds them - and the cell, which begins to the right of the page's own padding, is
    /// taken off at the end.
    /// </summary>
    private double XInNoteArea(VisualElement view)
    {
        double x = 0;
        for (Element? node = view; node is VisualElement element && element != NoteArea; node = element.Parent)
        {
            x += element.X;

            if (element is ScrollView scroll)
            {
                x -= scroll.ScrollX;
            }
        }

        return x - NoteArea.X;
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        await noteTarget.DeleteCommand.ExecuteAsync(null);
        if (noteTarget.IsDeleted)
        {
            await Navigation.PopAsync();
        }
    }

    private async void OnDoneClicked(object? sender, EventArgs e) => await CloseAsync();

    /// <summary>
    /// Opens the settings over the card. The card is a page of the shell's stack, so the settings page
    /// goes on that same stack and its own back button returns here. It is the same settings page as
    /// everywhere else; coming back to this one hands what was changed on it to the note, see
    /// <see cref="OnAppearing"/>.
    /// </summary>
    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage(settingsViewModel, feedbackViewModel));
    }

    /// <summary>
    /// A finger dragging the card down. The card follows the finger and shrinks on the way, the way
    /// a sheet that is being put away does, and how far it has come is measured against a drag of
    /// its own - a share of the note's height, capped by what is left of the screen below the finger
    /// - so the same swipe means the same thing wherever on the note it began.
    /// </summary>
    private void OnCloseSwipeMoved(object? sender, CloseSwipeMove move)
    {
        if (isClosing || NoteSide <= 0 || NoteArea.Height <= 0)
        {
            return;
        }

        // The finger is measured in the note's own square of 1000 units, the card moves in the cell
        // around it. The note is a square centred in that cell, which is where the two meet.
        var scale = NoteSide / InkDocument.Size;
        var noteTop = (NoteArea.Height - NoteSide) / 2;

        // A drag that begins while the card is still springing back from the last one takes the
        // card over. Without dropping that animation the two write the same two properties at once
        // and the card shakes between them.
        if (!isDraggingCard)
        {
            isDraggingCard = true;
            NoteFrame.CancelAnimations();
        }

        // Only downwards: the drag takes the card out of the editor, so a finger that wanders up
        // while it is being put down must not lift the card off the note.
        var travelled = Math.Max(0, move.Travel * scale);
        var startY = noteTop + (move.StartY * scale);

        // The drag has to be a deliberate one, and the same one wherever on the note the finger
        // landed: a share of the note's own height. A finger that landed low has less of the screen
        // left to cross than that, so the bottom fifth caps the drag there - a shorter way to close,
        // never a longer one. A finger that landed inside the bottom fifth has nothing left to cross
        // at all, and there the least a drag has to move stands in, as far as the cell still reaches.
        var line = CloseSwipeBottomShare * NoteArea.Height;
        var room = Math.Max(NoteArea.Height - startY, 0);
        var limit = Math.Max(
            Math.Min(CloseSwipeTravelShare * NoteSide, Math.Max(line - startY, 0)),
            Math.Min(CloseSwipeMinimumTravel * NoteArea.Height, room));

        // Never zero: it is what the drag is measured against.
        limit = Math.Max(limit, 1);

        // Unclamped, so the card can keep giving and shrinking past the limit instead of stopping
        // the moment it is far enough to leave.
        var reached = travelled / limit;
        swipeProgress = Math.Min(reached, 1);

        var offset = reached <= 1
            ? travelled
            : limit + ((travelled - limit) * CloseSwipeOvershoot);

        dragOffset = offset;
        NoteFrame.TranslationY = offset;
        NoteFrame.Scale = Math.Max(CloseSwipeExitScale, 1 - (CloseSwipeShrink * reached));
    }

    /// <summary>
    /// The finger of such a drag is gone. Either it took the card far enough down that letting go
    /// means leaving - then the card slides the rest of the way out and the editor closes - or it
    /// did not, and the card springs back to where it was.
    /// </summary>
    private async void OnCloseSwipeEnded(object? sender, EventArgs e)
    {
        if (isClosing)
        {
            return;
        }

        var leaving = swipeProgress >= 1;
        var travelled = dragOffset;
        swipeProgress = 0;
        isDraggingCard = false;

        if (!leaving)
        {
            await MoveCardAsync(0, 1, CloseSwipeReturnMilliseconds, Easing.CubicOut, Easing.SpringOut);
            return;
        }

        // Carried on from where the finger let go, so the card keeps moving the way it was already
        // going instead of stopping the moment it is released. It travels at least the height of
        // the space it is drawn in, so it is out of sight however tall the window is.
        await MoveCardAsync(
            Math.Max(travelled, NoteArea.Height),
            CloseSwipeExitScale,
            CloseSwipeLeaveMilliseconds,
            Easing.CubicIn);

        await CloseAsync();
        ResetCard();
    }

    /// <summary>
    /// Moves the card to where a drag has taken it. The note frame carries the movement rather than
    /// the note itself: the note is a square centred in that cell, so moving it inside a cell of its
    /// own size would have it cut off at the edges on the way.
    /// <para>
    /// Growing back can be given an easing of its own: the way back into place must not overshoot,
    /// or the card would be carried past the place it rests, while growing back may - that is what
    /// the spring is for.
    /// </para>
    /// </summary>
    private Task MoveCardAsync(
        double translationY,
        double scale,
        uint milliseconds,
        Easing easing,
        Easing? scaleEasing = null) =>
        Task.WhenAll(
            NoteFrame.TranslateToAsync(0, translationY, milliseconds, easing),
            NoteFrame.ScaleToAsync(scale, milliseconds, scaleEasing ?? easing));

    /// <summary>Puts the card back where it rests when it is not being dragged.</summary>
    private void ResetCard()
    {
        swipeProgress = 0;
        dragOffset = 0;
        isDraggingCard = false;
        NoteFrame.TranslationY = 0;
        NoteFrame.Scale = 1;
    }

    /// <summary>
    /// The menu was asked for from the note itself - a tap of a finger while only the pen draws, or a
    /// press of the right mouse button where there is one. The point arrives in the units of the ink
    /// document, so it is turned into a place on the page before the ring is put around it.
    /// </summary>
    private void OnMenuRequested(object? sender, MenuRequest request)
    {
        var point = DocumentToMenu(request.X, request.Y);
        ShowRadialMenu(point.X, point.Y);
    }

    /// <summary>
    /// The button that stays in the row above the note while the menu is the way in. It is what is
    /// left when the note cannot be tapped - there is no free finger while the finger draws, and there
    /// is no right mouse button on a tablet - so the ring is always one press away. It comes up over
    /// the middle of the note.
    /// </summary>
    private void OnMenuClicked(object? sender, EventArgs e)
    {
        var point = DocumentToMenu(InkDocument.Size / 2, InkDocument.Size / 2);
        ShowRadialMenu(point.X, point.Y);
    }

    private void OnRadialMenuChoiceRequested(object? sender, RadialMenuHit hit) => ApplyRadialMenuChoice(hit);

    /// <summary>
    /// Puts the menu up around a point of the page - or up again in the same place after a choice was
    /// taken. The blocks are built fresh every time, so the middle of the menu always names what is
    /// set at the moment and the block that switches the tool offers the one that is not drawing.
    /// </summary>
    private void ShowRadialMenu(double x, double y)
    {
        radialMenuOrigin = new Point(x, y);
        RadialMenu.Open(BuildMenuGroups(), x, y);
    }

    /// <summary>
    /// The blocks of the ring, in the order of the arc constants: the colours and the pen widths,
    /// which open a fan of choices, then redo, undo and the pen/finger switch, which are taken on the
    /// spot and hold nothing.
    /// <para>
    /// Both fans set the pen that is drawing rather than a colour and a width beside it, so the middle
    /// of each block names what that pen is now - the row of pens and the ring are two ways into the
    /// same pen. The widths are offered in the ring's own steps, which are coarser than the slider's
    /// because they are picked by sliding a finger across a wedge, and each of them is named by the
    /// number it stands for, since a dot alone no longer tells a finger the width it would take.
    /// </para>
    /// </summary>
    private IReadOnlyList<RadialMenuGroup> BuildMenuGroups()
    {
        var slot = penSlots[activePenSlot];

        var colors = new RadialMenuEntry[PenColors.Length];
        for (var index = 0; index < PenColors.Length; index++)
        {
            colors[index] = new RadialMenuEntry(PenColors[index].Name, PenColors[index].Hex, 0);
        }

        var steps = PenThickness.Steps(PenThickness.RadialStep);
        var widths = new RadialMenuEntry[steps];
        for (var index = 0; index < steps; index++)
        {
            widths[index] = new RadialMenuEntry(
                PenThickness.Format(PenThickness.ValueAt(index, PenThickness.RadialStep)),
                null,
                (float)ThicknessDotDiameter(index));
        }

        // The tool block offers the tool that is not drawing at the moment, so what pressing it does
        // is read off the icon and off its name rather than having to be worked out.
        var drawsWithFinger = InkHost.AllowFingerDrawing;

        return
        [
            new RadialMenuGroup(
                Strings.RadialMenuColors,
                PenColorName(slot.ColorHex),
                colors,
                IconFont.Color),
            new RadialMenuGroup(
                Strings.PenThicknessTitle,
                FormatThickness(slot.Thickness),
                widths,
                IconFont.LineThickness),
            new RadialMenuGroup(Strings.Redo, string.Empty, [], IconFont.Redo, IsAction: true),
            new RadialMenuGroup(Strings.Undo, string.Empty, [], IconFont.Undo, IsAction: true),
            new RadialMenuGroup(
                drawsWithFinger ? Strings.Pen : Strings.FingerDrawing,
                string.Empty,
                [],
                drawsWithFinger ? IconFont.Pen : IconFont.Finger,
                IsAction: true),
        ];
    }

    /// <summary>A width as it is read - the number and the unit together, so it is never bare.</summary>
    private static string FormatThickness(float thickness) => string.Format(
        CultureInfo.CurrentCulture,
        Strings.PenThicknessValueFormat,
        PenThickness.Format(thickness));

    /// <summary>
    /// What was taken in the menu. A block that holds choices opens them onto the outer ring, a block
    /// that acts does so at once, and a choice inside a block is applied - which closes that block's
    /// ring again and nothing else. A press in the middle closes the ring that is out, and takes the
    /// menu down with it once the ring of blocks is the only one left. The menu otherwise stays up
    /// through all of it: apart from the middle, only a press beside it puts it away. After a choice
    /// the ring is built again, so the middle of it names what is set from now on.
    /// </summary>
    private void ApplyRadialMenuChoice(RadialMenuHit hit)
    {
        switch (hit.Kind)
        {
            case RadialMenuHitKind.Outside:
                CloseRadialMenu();
                return;

            case RadialMenuHitKind.Center when RadialMenu.IsDetailOpen:
                RadialMenu.CloseDetail();
                return;

            case RadialMenuHitKind.Center:
                CloseRadialMenu();
                return;

            case RadialMenuHitKind.Group when hit.Group == ColorArc || hit.Group == ThicknessArc:
                RadialMenu.ToggleDetail(hit.Group);
                return;

            case RadialMenuHitKind.Group when hit.Group == RedoArc:
                InkHost.Redo();
                UpdateToolButtons();
                break;

            case RadialMenuHitKind.Group when hit.Group == UndoArc:
                InkHost.Undo();
                UpdateToolButtons();
                break;

            case RadialMenuHitKind.Group when hit.Group == ModeArc:
                ToggleFingerDrawing();
                break;

            case RadialMenuHitKind.Entry when hit.Group == ColorArc:
                SetPenColor(PenColors[hit.Index].Hex);
                break;

            case RadialMenuHitKind.Entry when hit.Group == ThicknessArc:
                ApplyThickness(hit.Index);
                break;

            default:
                return;
        }

        if (radialMenuOrigin is { } origin)
        {
            ShowRadialMenu(origin.X, origin.Y);
        }
    }

    /// <summary>
    /// Takes the menu down. Nothing else happens with it: the menu is not a mode, and every choice it
    /// offers has already been applied where it was taken. Which rows are shown is not decided here
    /// either - that is the way in the settings page picked, see <see cref="ApplyToolUi"/>.
    /// </summary>
    private void CloseRadialMenu()
    {
        radialMenuOrigin = null;
        RadialMenu.Close();
    }

    /// <summary>
    /// Which of the two ways of reaching the drawing tools is in use, as written by the settings page.
    /// A value that is not a member of the enum - an old one, or one written by a newer version - is
    /// read as the radial menu, which is the default.
    /// </summary>
    private InkToolUi ReadToolUi()
    {
        var stored = preferences.Get(PreferenceKeys.InkToolUi, InkToolUi.RadialMenu.ToString());
        return Enum.TryParse<InkToolUi>(stored, out var parsed) ? parsed : InkToolUi.RadialMenu;
    }

    /// <summary>
    /// Puts the ring in or out of reach, following the setting. The row of tools - <see
    /// cref="ToolBar"/> holds the tools, the pens and the paper colours together - is always drawn,
    /// because it shares its row with the buttons that leave the card: taking it away takes no
    /// height off the note, it only puts the tools out of reach. What the setting picks is whether the
    /// ring is there as well. With it, the note is what a free finger taps and the button beside the
    /// row is what is left when there is no free finger or no right mouse button. Without it, neither
    /// the tap on the note nor the right mouse button asks for a ring, and the button goes with it.
    /// The two buttons that leave the card stand outside the row and are not touched here, because
    /// the ring cannot delete or close a card.
    /// </summary>
    private void ApplyToolUi()
    {
        var isToolBar = ReadToolUi() == InkToolUi.ToolBar;

        MenuButton.IsVisible = !isToolBar;
        InkHost.RadialMenuEnabled = !isToolBar;

        if (isToolBar)
        {
            // A ring that is up would be left there without a way of being asked for again, since
            // neither the note nor the button beside the row asks for one in this mode. The pens are
            // not touched: they stay in the row either way, and so does the flyout they open.
            CloseRadialMenu();
        }
    }

    /// <summary>
    /// Sets the width of the pen in hand from the ring's width block. The block offers the range in
    /// steps of its own - coarser than the slider's, because it is picked by sliding a finger across a
    /// wedge rather than by dragging a handle - and the width taken here is the width the pen writes
    /// with from now on, in the flyout as well. There is one pen, not a width beside it.
    /// </summary>
    private void ApplyThickness(int index) =>
        SetPenThickness(PenThickness.ValueAt(index, PenThickness.RadialStep));

    /// <summary>
    /// Width of the dot that stands for a width on the ring, in device units. Scaled up from the width
    /// itself so the finest one is still a visible dot, and capped so the widest one stays inside the
    /// segment that carries it.
    /// </summary>
    private static double ThicknessDotDiameter(int index) =>
        Math.Clamp(PenThickness.ValueAt(index, PenThickness.RadialStep) * 1.6, 5, ThicknessDotMaxSize);

    /// <summary>
    /// The pens the row shows, as they were left. A store that has none yet - the first run, or one
    /// written before the pens were slots - keeps the width the version before the slots remembered
    /// for the first pen, so an update does not quietly hand the user a different pen than the one
    /// they were drawing with.
    /// </summary>
    private PenSlot[] ReadPenSlots()
    {
        var stored = preferences.Get(PreferenceKeys.PenSlots, string.Empty);
        if (!string.IsNullOrEmpty(stored))
        {
            return PenSlots.Parse(stored).ToArray();
        }

        var slots = PenSlots.Parse(null).ToArray();
        var previous = preferences.Get(PreferenceKeys.PenThickness, string.Empty);
        if (float.TryParse(previous, NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness))
        {
            slots[0] = slots[0] with { Thickness = PenThickness.Clamp(thickness) };
        }

        return slots;
    }

    /// <summary>
    /// Which pen was drawing. An index that is not one of the pens - a store written by a version that
    /// had a different number of them - falls back on the first, so the row always has one in hand.
    /// </summary>
    private int ReadActivePenSlot()
    {
        var stored = preferences.Get(PreferenceKeys.ActivePenSlot, string.Empty);
        return int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && index >= 0
            && index < penSlots.Length
                ? index
                : 0;
    }

    /// <summary>
    /// Turns a point of the ink document into a point of the radial menu. The document is a square of
    /// <see cref="InkDocument.Size"/> units drawn as the note, which sits centred in the cell it is
    /// measured in and inside the frame around it; the menu covers the whole page. Both views hang
    /// under the same grid, so the only thing left to take off is where the menu itself sits.
    /// </summary>
    private Point DocumentToMenu(double x, double y)
    {
        var scale = NoteSide / InkDocument.Size;
        var padding = NoteFrame.Padding;
        var originX = NoteArea.X + ((NoteArea.Width - NoteSide - padding.HorizontalThickness) / 2) + padding.Left;
        var originY = NoteArea.Y + ((NoteArea.Height - NoteSide - padding.VerticalThickness) / 2) + padding.Top;

        return new Point(
            originX + (x * scale) - RadialMenu.X,
            originY + (y * scale) - RadialMenu.Y);
    }

    /// <summary>
    /// Leaves the editor, keeping the ink: the card is written before the page goes, so a swipe that
    /// closed it has nothing left to save by the time the page is gone.
    /// </summary>
    private async Task CloseAsync()
    {
        if (isClosing)
        {
            return;
        }

        isClosing = true;
        await SaveAsync();
        await Navigation.PopAsync();
    }
}
