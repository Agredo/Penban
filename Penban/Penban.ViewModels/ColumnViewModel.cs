// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Services.Abstractions;

namespace Penban.ViewModels;

/// <summary>Represents a single column and its ordered cards.</summary>
public partial class ColumnViewModel : ObservableObject
{
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;
    private BoardColumn column;

    public ColumnViewModel(BoardColumn column, ICardService cardService, IDialogService dialogService)
    {
        this.column = column;
        this.cardService = cardService;
        this.dialogService = dialogService;
        title = column.Title;
        sortOrder = column.SortOrder;
    }

    public Guid Id => column.Id;

    public Guid BoardId => column.BoardId;

    /// <summary>
    /// Points this column at the row as it was just read from the database, keeping the instance and
    /// with it the cards that have already been loaded into it. The board page reads its lanes from
    /// these very view models, so a lane that is still on the board must not be built again just
    /// because the overview re-read the board - see <see cref="BoardViewModel.RefreshAsync"/>.
    /// </summary>
    public void Update(BoardColumn updated)
    {
        column = updated;
        Title = updated.Title;
        SortOrder = updated.SortOrder;
    }

    [ObservableProperty]
    private string title;

    partial void OnTitleChanged(string value) => column.Title = value;

    [ObservableProperty]
    private int sortOrder;

    public ObservableCollection<CardViewModel> Cards { get; } = new();

    [RelayCommand]
    private async Task LoadCardsAsync()
    {
        var cards = await cardService.GetCardsAsync(Id);
        Cards.Clear();
        foreach (var card in cards.OrderBy(c => c.SortOrder))
        {
            Cards.Add(new CardViewModel(card, cardService, dialogService));
        }

        // Opening a board is when its notes are handed to the search: a note written before the text
        // recognition existed has no stored text yet, and waiting for it to be edited would leave the
        // search empty for everything already written. Cards that were read before are recognised as
        // unchanged by their hash and cost nothing further.
        cardService.QueueRecognition(cards);
    }

    [RelayCommand]
    private async Task AddCardAsync()
    {
        var card = await cardService.CreateCardAsync(Id);
        Cards.Add(new CardViewModel(card, cardService, dialogService));
    }

    /// <summary>Called after a touch drag reorders the cards; writes the new SortOrder values.</summary>
    [RelayCommand]
    private Task ReorderCardsAsync(IReadOnlyList<Guid> orderedCardIds)
        => cardService.ReorderCardsAsync(Id, orderedCardIds);
}
