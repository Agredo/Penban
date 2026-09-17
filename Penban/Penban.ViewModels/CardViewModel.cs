// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// Represents a single card. Card content is exclusively the handwritten ink strokes held
/// by <see cref="InkCanvas"/>; there is no typed-text content.
/// </summary>
public partial class CardViewModel : ObservableObject
{
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;
    private readonly Card card;

    public CardViewModel(Card card, ICardService cardService, IDialogService dialogService)
    {
        this.card = card;
        this.cardService = cardService;
        this.dialogService = dialogService;
        InkCanvas = new InkCanvasViewModel();
        InkCanvas.LoadStrokes(card.Strokes);
        sortOrder = card.SortOrder;
    }

    public Guid Id => card.Id;

    public Guid ColumnId => card.ColumnId;

    [ObservableProperty]
    private int sortOrder;

    /// <summary>Set to true once the user has confirmed and completed deletion of this card.</summary>
    [ObservableProperty]
    private bool isDeleted;

    public void UpdatePlacement(Guid columnId, int newSortOrder)
    {
        card.ColumnId = columnId;
        SortOrder = newSortOrder;
    }

    public InkCanvasViewModel InkCanvas { get; }

    /// <summary>Persists the card's current ink strokes. Called when the card/canvas is closed - there is no explicit "Save" button.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        card.Strokes = InkCanvas.Strokes.ToList();
        card.SortOrder = SortOrder;
        await cardService.SaveCardAsync(card);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await dialogService.DisplayConfirmationAsync(Strings.Delete, Strings.DeleteCardConfirmation, Strings.Delete, Strings.Cancel);
        if (!confirmed)
        {
            return;
        }

        await cardService.DeleteCardAsync(card.Id);
        IsDeleted = true;
    }
}

