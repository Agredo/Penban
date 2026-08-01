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
    private readonly BoardColumn column;

    public ColumnViewModel(BoardColumn column, ICardService cardService)
    {
        this.column = column;
        this.cardService = cardService;
        title = column.Title;
        sortOrder = column.SortOrder;
    }

    public Guid Id => column.Id;

    public Guid BoardId => column.BoardId;

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
            Cards.Add(new CardViewModel(card, cardService));
        }
    }

    [RelayCommand]
    private async Task AddCardAsync()
    {
        var card = await cardService.CreateCardAsync(Id);
        Cards.Add(new CardViewModel(card, cardService));
    }

    /// <summary>Called after a touch drag reorders the cards; writes the new SortOrder values.</summary>
    [RelayCommand]
    private Task ReorderCardsAsync(IReadOnlyList<Guid> orderedCardIds)
        => cardService.ReorderCardsAsync(Id, orderedCardIds);
}
