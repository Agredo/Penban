using Penban.ViewModels;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Dedicated ink editor for one card, opened from the board to keep Kanban card templates lightweight.
/// </summary>
public partial class CardInkEditorPage : ContentPage
{
    private readonly CardViewModel cardViewModel;

    public CardInkEditorPage(CardViewModel cardViewModel)
    {
        InitializeComponent();
        this.cardViewModel = cardViewModel;
        InkHost.LoadStrokes(cardViewModel.InkCanvas.Strokes);
        InkHost.StrokeCompleted += OnStrokeCompleted;
    }

    private void OnStrokeCompleted(object? sender, EventArgs e)
    {
        cardViewModel.InkCanvas.Clear();
        foreach (var stroke in InkHost.GetStrokes())
        {
            cardViewModel.InkCanvas.AddStroke(stroke);
        }

        _ = cardViewModel.SaveCommand.ExecuteAsync(null);
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
        await cardViewModel.SaveCommand.ExecuteAsync(null);
        await Navigation.PopModalAsync();
    }
}
