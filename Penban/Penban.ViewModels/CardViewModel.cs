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

    /// <summary>
    /// Paper colour of this card's note. Without a choice of the user the colour is derived from the
    /// id, so a fresh note still looks hand-picked instead of uniform; picking one in the editor
    /// stores it with the card.
    /// </summary>
    public int NoteColorIndex
    {
        get => card.NoteColorIndex ?? NoteStyle.PaperIndexFor(card.Id);
        set
        {
            if (card.NoteColorIndex == value)
            {
                return;
            }

            card.NoteColorIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Tilt of this card's note in degrees.</summary>
    public double NoteTilt => NoteStyle.TiltFor(card.Id);

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

