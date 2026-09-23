using Microsoft.Maui.Controls.Shapes;
using Penban.Maui.Views.Controls;
using Penban.Maui.Views.Ink;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;
using Penban.ViewModels;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Dedicated ink editor for one card, opened from the board to keep Kanban card templates lightweight.
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
    /// Where the finger has to arrive before letting go leaves the editor: four fifths of the way
    /// down the cell the card is drawn in, the bottom fifth of the screen. Measured from the screen
    /// rather than from the note, so the same swipe means the same thing however large the note is
    /// drawn - and far enough down that a hand merely crossing the note cannot reach it.
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
    /// leaving, and with an easing that overshoots, so a drag that was called off reads as the card
    /// being let go rather than as it stopping dead halfway.
    /// </summary>
    private const uint CloseSwipeReturnMilliseconds = 280;

    /// <summary>
    /// Ink is dark on paper in both themes, so the ring that marks the picked pen colour cannot
    /// come from the theme's accent - it would vanish on a dark swatch.
    /// </summary>
    private const string InkColor = "#1E2230";

    /// <summary>
    /// Pen colours offered in the editor, dark enough to read on every paper colour. The picker
    /// scrolls sideways, so the palette can be as wide as a real pen case.
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

    private readonly CardViewModel cardViewModel;
    private readonly IPreferences preferences;
    private readonly List<StickyNoteBorder> swatches = [];
    private readonly List<Border> penSwatches = [];
    private int selectedPenColorIndex;
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

    public CardInkEditorPage(CardViewModel cardViewModel, IPreferences preferences)
    {
        InitializeComponent();
        this.cardViewModel = cardViewModel;
        this.preferences = preferences;

        // Attaching the store also restores the renderer and the drawing settings, so it has to
        // happen before anything is loaded into the host.
        InkHost.Preferences = preferences;
        InkHost.LoadStrokes(cardViewModel.InkCanvas.Strokes);
        InkHost.StrokeCompleted += OnStrokeCompleted;
        InkHost.IsEraserModeChanged += OnEraserModeChanged;

        // A finger dragged down takes the card with it and lets it go: the renderer raises both,
        // because only it sees the individual fingers. It is only ever read with finger drawing off,
        // where the finger has no stroke to draw.
        InkHost.CloseSwipeMoved += OnCloseSwipeMoved;
        InkHost.CloseSwipeEnded += OnCloseSwipeEnded;
        InkHost.CloseOnSwipeDown = true;

        NoteSurface.NoteColorIndex = cardViewModel.NoteColorIndex;
        NoteArea.SizeChanged += OnNoteAreaSizeChanged;
        BuildColorPicker();
        BuildPenColorPicker();
        UpdateToolButtons();

#if IOS
        // Apple Pencil double-tap switches to the eraser, and the pencil's tilt feeds the
        // calligraphy effect. Only iOS exposes the double-tap; Android's S Pen button has no
        // equivalent.
        InkHost.Behaviors.Add(new PencilTapBehavior(preferences));
        InkHost.Behaviors.Add(new PenTiltBehavior(preferences));

        // Two-finger tap undoes, three-finger tap redoes.
        InkHost.Behaviors.Add(new MultiFingerTapBehavior());
#endif

#if WINDOWS
        // The eraser end of a Surface Pen erases while it is held against the surface, and writes
        // again as soon as it is lifted. Two-finger tap undoes, three-finger tap redoes, and the
        // pen's tilt feeds the calligraphy effect.
        InkHost.Behaviors.Add(new PenTailEraserBehavior(preferences));
        InkHost.Behaviors.Add(new MultiFingerTapBehavior());
        InkHost.Behaviors.Add(new PenTiltBehavior(preferences));
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

    /// <summary>Repaints the writing surface and remembers the choice on the card.</summary>
    private void ApplyNoteColor(int index)
    {
        // Stored before the guard: the next new note should start in this colour even if this
        // card already happened to have it.
        preferences.Set(PreferenceKeys.NoteLastColorIndex, index.ToString());

        if (cardViewModel.NoteColorIndex == index)
        {
            return;
        }

        cardViewModel.NoteColorIndex = index;
        NoteSurface.NoteColorIndex = index;
        UpdateColorPicker();

        // The colour travels with the card, so it has to reach the database like a stroke does.
        QueueSave();
    }

    /// <summary>Marks the active colour: the chosen note is raised and outlined, like a picked-up slip.</summary>
    private void UpdateColorPicker()
    {
        var chosen = cardViewModel.NoteColorIndex;
        for (var index = 0; index < swatches.Count; index++)
        {
            var isChosen = index == chosen;
            swatches[index].Stroke = isChosen ? new SolidColorBrush(Color.FromArgb("#1E2230")) : null;
            swatches[index].StrokeThickness = isChosen ? 3 : 0;
            swatches[index].Scale = isChosen ? 1.12 : 1;
        }
    }

    /// <summary>
    /// Fills the picker with one round swatch per pen colour, so the choice is made on the colour
    /// itself rather than on its name.
    /// </summary>
    private void BuildPenColorPicker()
    {
        for (var index = 0; index < PenColors.Length; index++)
        {
            var (hex, name) = PenColors[index];

            var swatch = new Border
            {
                BackgroundColor = Color.FromArgb(hex),
                StrokeShape = new Ellipse(),
                WidthRequest = PenSwatchSize,
                HeightRequest = PenSwatchSize,
            };
            SemanticProperties.SetDescription(swatch, name);

            var chosen = index;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => ApplyPenColor(chosen);
            swatch.GestureRecognizers.Add(tap);

            penSwatches.Add(swatch);
            PenColorPicker.Add(swatch);
        }

        ApplyPenColor(selectedPenColorIndex);
    }

    /// <summary>Colours new strokes and marks the swatch, without touching strokes already drawn.</summary>
    private void ApplyPenColor(int index)
    {
        selectedPenColorIndex = index;
        InkHost.StrokeColor = PenColors[index].Hex;

        for (var i = 0; i < penSwatches.Count; i++)
        {
            var isChosen = i == index;
            penSwatches[i].Stroke = isChosen ? new SolidColorBrush(Color.FromArgb(InkColor)) : null;
            penSwatches[i].StrokeThickness = isChosen ? 3 : 0;
            penSwatches[i].Scale = isChosen ? 1.18 : 1;
        }
    }

    /// <summary>Keeps the tool buttons showing which mode is active and what can be undone.</summary>
    private void UpdateToolButtons()
    {
        var resources = Application.Current!.Resources;
        PenButton.Style = (Style)resources[InkHost.IsEraserMode ? "GhostButton" : "PrimaryButton"];
        EraserButton.Style = (Style)resources[InkHost.IsEraserMode ? "PrimaryButton" : "GhostButton"];
        FingerButton.Style = (Style)resources[InkHost.AllowFingerDrawing ? "PrimaryButton" : "GhostButton"];

        // Nothing to take back or to put back yet, so the buttons say so instead of doing nothing.
        UndoButton.IsEnabled = InkHost.CanUndo;
        RedoButton.IsEnabled = InkHost.CanRedo;
    }

    private void OnStrokeCompleted(object? sender, EventArgs e)
    {
        cardViewModel.InkCanvas.Clear();
        foreach (var stroke in InkHost.GetStrokes())
        {
            cardViewModel.InkCanvas.AddStroke(stroke);
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
        if (cardViewModel.IsDeleted)
        {
            return;
        }

        await cardViewModel.SaveCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        if (isClosing)
        {
            isClosing = false;
            return;
        }

        // Leaving without "Fertig" (hardware back, swipe-down on iPad) still keeps the ink.
        _ = SaveAsync();
    }

    private void OnPenClicked(object? sender, EventArgs e) => InkHost.IsEraserMode = false;

    private void OnEraserClicked(object? sender, EventArgs e) => InkHost.IsEraserMode = true;

    /// <summary>
    /// The double-tap on the Apple Pencil flips the mode without a button being pressed, so the
    /// toolbar follows the host rather than the click handlers.
    /// </summary>
    private void OnEraserModeChanged(object? sender, EventArgs e) => UpdateToolButtons();

    /// <summary>
    /// Turns finger drawing on or off for this card. The choice goes straight into the same
    /// preference the settings page reads, so both entry points stay in step.
    /// </summary>
    private void OnFingerClicked(object? sender, EventArgs e)
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

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        await cardViewModel.DeleteCommand.ExecuteAsync(null);
        if (cardViewModel.IsDeleted)
        {
            await Navigation.PopModalAsync();
        }
    }

    private async void OnDoneClicked(object? sender, EventArgs e) => await CloseAsync();

    /// <summary>
    /// A finger dragging the card down. The card follows the finger and shrinks on the way, the way
    /// a sheet that is being put away does, and how far it has come is read off where the finger is
    /// on the screen - so the same swipe means the same thing however the window is shaped.
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

        // The finger has to reach the bottom fifth of the screen. A finger that was already there
        // when the swipe began has nothing left to cross, so the drag still has to be a deliberate
        // one of its own.
        var limit = Math.Max(
            (CloseSwipeBottomShare * NoteArea.Height) - startY,
            CloseSwipeMinimumTravel * NoteArea.Height);

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
            await MoveCardAsync(0, 1, CloseSwipeReturnMilliseconds, Easing.SpringOut);
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
    /// </summary>
    private Task MoveCardAsync(double translationY, double scale, uint milliseconds, Easing easing) =>
        Task.WhenAll(
            NoteFrame.TranslateToAsync(0, translationY, milliseconds, easing),
            NoteFrame.ScaleToAsync(scale, milliseconds, easing));

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
        await Navigation.PopModalAsync();
    }
}
