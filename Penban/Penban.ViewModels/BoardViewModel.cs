// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>Represents a single board, including its ordered columns.</summary>
public partial class BoardViewModel : ObservableObject
{
    private readonly IBoardService boardService;
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;

    public BoardViewModel(Board board, IBoardService boardService, ICardService cardService, IDialogService dialogService)
    {
        Id = board.Id;
        title = board.Title;
        this.boardService = boardService;
        this.cardService = cardService;
        this.dialogService = dialogService;

        foreach (var column in board.Columns.OrderBy(c => c.SortOrder))
        {
            Columns.Add(new ColumnViewModel(column, cardService, dialogService));
        }
    }

    public Guid Id { get; }

    [ObservableProperty]
    private string title;

    /// <summary>Total number of cards across all columns; loaded by <see cref="LoadSummaryAsync"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardCountText))]
    private int cardCount;

    /// <summary>Localized "N cards" caption for the board overview.</summary>
    public string CardCountText => string.Format(Strings.CardsCountFormat, CardCount);

    /// <summary>Ink strokes of the first non-empty card, shown as the board's thumbnail in the overview.</summary>
    public ObservableCollection<InkStroke> PreviewStrokes { get; } = new();

    public ObservableCollection<ColumnViewModel> Columns { get; } = new();

    /// <summary>
    /// Loads the lightweight summary shown on the board overview card (card count + ink thumbnail)
    /// without populating the column card collections used by the board page.
    /// </summary>
    public async Task LoadSummaryAsync()
    {
        var count = 0;
        List<InkStroke>? preview = null;
        foreach (var column in Columns)
        {
            var cards = await cardService.GetCardsAsync(column.Id);
            count += cards.Count;
            preview ??= cards.OrderBy(c => c.SortOrder).Select(c => c.Strokes).FirstOrDefault(s => s.Count > 0);
        }

        CardCount = count;
        PreviewStrokes.Clear();
        if (preview is not null)
        {
            foreach (var stroke in preview)
            {
                PreviewStrokes.Add(stroke);
            }
        }
    }

    [RelayCommand]
    private async Task AddColumnAsync()
    {
        var name = await dialogService.DisplayPromptAsync(Strings.AddColumn, string.Empty, Strings.AddColumn, Strings.Cancel);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var column = await boardService.AddColumnAsync(Id, name);
        Columns.Add(new ColumnViewModel(column, cardService, dialogService));
    }

    [RelayCommand]
    private async Task DeleteColumnAsync(Guid columnId)
    {
        var confirmed = await dialogService.DisplayConfirmationAsync(Strings.Delete, Strings.DeleteColumnConfirmation, Strings.Delete, Strings.Cancel);
        if (!confirmed)
        {
            return;
        }

        await boardService.DeleteColumnAsync(Id, columnId);
        var column = Columns.FirstOrDefault(c => c.Id == columnId);
        if (column is not null)
        {
            Columns.Remove(column);
        }
    }

    [RelayCommand]
    private async Task RenameColumnAsync(Guid columnId)
    {
        var column = Columns.FirstOrDefault(c => c.Id == columnId);
        if (column is null)
        {
            return;
        }

        var name = await dialogService.DisplayPromptAsync(Strings.RenameColumn, string.Empty, Strings.Rename, Strings.Cancel, initialValue: column.Title);
        if (string.IsNullOrWhiteSpace(name) || name == column.Title)
        {
            return;
        }

        column.Title = name;
        await boardService.RenameColumnAsync(Id, columnId, name);
    }

    /// <summary>Called after a touch drag reorders the columns; writes the new SortOrder values.</summary>
    [RelayCommand]
    private Task ReorderColumnsAsync(IReadOnlyList<Guid> orderedColumnIds)
        => boardService.ReorderColumnsAsync(Id, orderedColumnIds);

    /// <summary>
    /// Persists a card move without reloading the columns. The Kanban control has already
    /// applied the move to its bound collection, and the page mirrors it in the column
    /// view models; reloading here would rebuild the ItemsSource mid-drag-pipeline and
    /// race the control's own insert step (which produced duplicate cards).
    /// </summary>
    [RelayCommand]
    private Task MoveCardAsync(CardMoveRequest request)
        => cardService.MoveCardAsync(request.CardId, request.SourceColumnId, request.TargetColumnId, request.NewIndex);
}
