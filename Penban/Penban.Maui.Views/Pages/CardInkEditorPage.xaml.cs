using Microsoft.Maui.Controls.Shapes;
using Penban.Maui.Views.Controls;
using Penban.Maui.Views.Ink;
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
    /// Ink is dark on paper in both themes, so the ring that marks the picked pen colour cannot
    /// come from the theme's accent - it would vanish on a dark swatch.
    /// </summary>
    private const string InkColor = "#1E2230";

    /// <summary>Pen colours offered in the editor, dark enough to read on every paper colour.</summary>
    private static readonly (string Hex, string Name)[] PenColors =
    [
        ("#000000", Strings.PenColorBlack),
        ("#1B4FD8", Strings.PenColorBlue),
        ("#C62828", Strings.PenColorRed),
        ("#1B7F3B", Strings.PenColorGreen),
    ];

    private readonly CardViewModel cardViewModel;
    private readonly IPreferences preferences;
    private readonly List<StickyNoteBorder> swatches = [];
    private readonly List<Border> penSwatches = [];
    private int selectedPenColorIndex;
    private int saveVersion;
    private bool isClosing;

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

        NoteSurface.NoteColorIndex = cardViewModel.NoteColorIndex;
        NoteArea.SizeChanged += OnNoteAreaSizeChanged;
        BuildColorPicker();
        BuildPenColorPicker();
        UpdateToolButtons();

#if IOS
        // Apple Pencil double-tap switches to the eraser. Only iOS exposes that gesture; Android's
        // S Pen button has no equivalent.
        InkHost.Behaviors.Add(new PencilTapBehavior(preferences));
#endif

#if WINDOWS
        // The eraser end of a Surface Pen erases while it is held against the surface, and writes
        // again as soon as it is lifted.
        InkHost.Behaviors.Add(new PenTailEraserBehavior(preferences));
#endif
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

    /// <summary>Keeps the tool buttons showing which mode is active.</summary>
    private void UpdateToolButtons()
    {
        var resources = Application.Current!.Resources;
        PenButton.Style = (Style)resources[InkHost.IsEraserMode ? "GhostButton" : "PrimaryButton"];
        EraserButton.Style = (Style)resources[InkHost.IsEraserMode ? "PrimaryButton" : "GhostButton"];
        FingerButton.Style = (Style)resources[InkHost.AllowFingerDrawing ? "PrimaryButton" : "GhostButton"];
    }

    private void OnStrokeCompleted(object? sender, EventArgs e)
    {
        cardViewModel.InkCanvas.Clear();
        foreach (var stroke in InkHost.GetStrokes())
        {
            cardViewModel.InkCanvas.AddStroke(stroke);
        }

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

    private void OnUndoClicked(object? sender, EventArgs e) => InkHost.Undo();

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        await cardViewModel.DeleteCommand.ExecuteAsync(null);
        if (cardViewModel.IsDeleted)
        {
            await Navigation.PopModalAsync();
        }
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        isClosing = true;
        await SaveAsync();
        await Navigation.PopModalAsync();
    }
}
