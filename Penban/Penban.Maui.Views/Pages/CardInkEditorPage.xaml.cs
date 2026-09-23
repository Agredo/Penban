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
    /// Edge length of the largest dot the pen width ring draws, in device units. The widest rungs of
    /// the ladder would be drawn wider than the segment carrying them, so past a point the dots only
    /// hint at the width.
    /// </summary>
    private const double ThicknessDotMaxSize = 26;

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

    /// <summary>
    /// Names of the pen widths, in the order of <see cref="PenThickness.Presets"/>. The ring offers
    /// them as dots drawn at the width itself, so these are only ever read in the middle of the menu.
    /// </summary>
    private static readonly string[] ThicknessNames =
    [
        Strings.PenThicknessFine,
        Strings.PenThicknessNormal,
        Strings.PenThicknessMedium,
        Strings.PenThicknessBold,
        Strings.PenThicknessExtraBold,
    ];

    private readonly CardViewModel cardViewModel;
    private readonly IPreferences preferences;
    private readonly List<StickyNoteBorder> swatches = [];
    private readonly List<Border> penSwatches = [];
    private int selectedPenColorIndex;
    private int selectedThicknessIndex;
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

        // The menu is asked for from the note itself - a tap of a finger that is not drawing, or a
        // press of the right mouse button where there is one - and the page is the side that owns it,
        // so the request comes here and the ring is put up by the page. See OnMenuRequested. Whether
        // the note asks for it at all is not decided here but by ApplyToolUi, further down.
        InkHost.MenuRequested += OnMenuRequested;
        RadialMenu.ChoiceRequested += OnRadialMenuChoiceRequested;

        NoteSurface.NoteColorIndex = cardViewModel.NoteColorIndex;
        NoteArea.SizeChanged += OnNoteAreaSizeChanged;
        BuildColorPicker();
        BuildPenColorPicker();
        ApplyToolUi();
        ApplyThickness(PenThickness.NearestIndex(ReadStoredThickness()));
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
        PenButton.Style = (Style)resources[InkHost.IsEraserMode ? "GhostIconButton" : "AccentIconButton"];
        EraserButton.Style = (Style)resources[InkHost.IsEraserMode ? "AccentIconButton" : "GhostIconButton"];
        FingerButton.Style = (Style)resources[InkHost.AllowFingerDrawing ? "AccentIconButton" : "GhostIconButton"];

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
        CloseRadialMenu();
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
    private void OnFingerClicked(object? sender, EventArgs e) => ToggleFingerDrawing();

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
    /// The button that stays in the tool row while the menu is the way in. It is what is left when the
    /// note cannot be tapped - there is no free finger while the finger draws, and there is no right
    /// mouse button on a tablet - so the ring is always one press away. It comes up over the middle of
    /// the note.
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
    /// </summary>
    private IReadOnlyList<RadialMenuGroup> BuildMenuGroups()
    {
        var colors = new RadialMenuEntry[PenColors.Length];
        for (var index = 0; index < PenColors.Length; index++)
        {
            colors[index] = new RadialMenuEntry(PenColors[index].Name, PenColors[index].Hex, 0);
        }

        var widths = new RadialMenuEntry[PenThickness.Count];
        for (var index = 0; index < PenThickness.Count; index++)
        {
            widths[index] = new RadialMenuEntry(ThicknessNames[index], null, (float)ThicknessDotDiameter(index));
        }

        // The tool block offers the tool that is not drawing at the moment, so what pressing it does
        // is read off the icon and off its name rather than having to be worked out.
        var drawsWithFinger = InkHost.AllowFingerDrawing;

        return
        [
            new RadialMenuGroup(
                Strings.RadialMenuColors,
                PenColors[selectedPenColorIndex].Name,
                colors,
                IconFont.Color),
            new RadialMenuGroup(
                Strings.PenThicknessTitle,
                ThicknessNames[selectedThicknessIndex],
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
                ApplyPenColor(hit.Index);
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
    /// Shows the way in that was picked and takes the other one away: the ring carries what the five
    /// tool buttons and the pen colours carry, so the two are alternatives and never both. While the
    /// menu is the way in, the tool row keeps nothing but the button that opens it - the note is what
    /// a free finger taps, and the button is what is left when there is no free finger - which leaves
    /// the note the space the buttons and the pen colours were taking. In the toolbar mode the menu is
    /// off altogether: no tap on the note, no right mouse button and no button of its own.
    /// </summary>
    private void ApplyToolUi()
    {
        var isToolBar = ReadToolUi() == InkToolUi.ToolBar;

        PenButton.IsVisible = isToolBar;
        EraserButton.IsVisible = isToolBar;
        UndoButton.IsVisible = isToolBar;
        RedoButton.IsVisible = isToolBar;
        FingerButton.IsVisible = isToolBar;
        PenColorBar.IsVisible = isToolBar;

        MenuButton.IsVisible = !isToolBar;
        InkHost.RadialMenuEnabled = !isToolBar;

        if (isToolBar)
        {
            // A ring that is up would be left there without a way of being asked for again.
            CloseRadialMenu();
        }
    }

    /// <summary>
    /// Colours new strokes with the chosen pen width and remembers it for the next card. Strokes that
    /// have already been drawn keep the width they were drawn with.
    /// </summary>
    private void ApplyThickness(int index)
    {
        selectedThicknessIndex = PenThickness.ClampIndex(index);
        var thickness = PenThickness.ValueAt(selectedThicknessIndex);
        InkHost.StrokeThickness = thickness;
        preferences.Set(PreferenceKeys.PenThickness, thickness.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Width of the dot that stands for a rung of the ladder on the ring, in device units. Scaled up
    /// from the width itself so the finest rung is still a visible dot, and capped so the widest one
    /// stays inside the segment that carries it.
    /// </summary>
    private static double ThicknessDotDiameter(int index) =>
        Math.Clamp(PenThickness.ValueAt(index) * 1.6, 5, ThicknessDotMaxSize);

    /// <summary>
    /// The width the user last drew with. A stored value that is not a number - or one that is no
    /// longer on the ladder - falls back on the width a pen starts with rather than leaving the
    /// editor without one.
    /// </summary>
    private float ReadStoredThickness()
    {
        var stored = preferences.Get(PreferenceKeys.PenThickness, string.Empty);
        return float.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness)
            ? thickness
            : PenThickness.Default;
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
        await Navigation.PopModalAsync();
    }
}
