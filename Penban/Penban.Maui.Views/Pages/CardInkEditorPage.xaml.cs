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

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        await cardViewModel.SaveCommand.ExecuteAsync(null);
        await Navigation.PopModalAsync();
    }
}
