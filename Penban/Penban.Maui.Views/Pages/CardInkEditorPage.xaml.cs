using Penban.Maui.Views.Controls;
using Penban.ViewModels;

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

    private readonly CardViewModel cardViewModel;
    private readonly List<StickyNoteBorder> swatches = [];
    private int saveVersion;
    private bool isClosing;

    public CardInkEditorPage(CardViewModel cardViewModel)
    {
        InitializeComponent();
        this.cardViewModel = cardViewModel;
        NoteSurface.NoteColorIndex = cardViewModel.NoteColorIndex;
        NoteArea.SizeChanged += OnNoteAreaSizeChanged;
        BuildColorPicker();
        InkHost.LoadStrokes(cardViewModel.InkCanvas.Strokes);
        InkHost.StrokeCompleted += OnStrokeCompleted;
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

    private void OnPenClicked(object? sender, EventArgs e)
    {
        InkHost.IsEraserMode = false;
        PenButton.Style = (Style)Application.Current!.Resources["PrimaryButton"];
        EraserButton.Style = (Style)Application.Current!.Resources["GhostButton"];
    }

    private void OnEraserClicked(object? sender, EventArgs e)
    {
        InkHost.IsEraserMode = true;
        PenButton.Style = (Style)Application.Current!.Resources["GhostButton"];
        EraserButton.Style = (Style)Application.Current!.Resources["PrimaryButton"];
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
