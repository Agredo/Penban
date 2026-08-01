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
            Columns.Add(new ColumnViewModel(column, cardService));
        }
    }

    public Guid Id { get; }

    [ObservableProperty]
    private string title;

    public ObservableCollection<ColumnViewModel> Columns { get; } = new();

    [RelayCommand]
    private async Task AddColumnAsync()
    {
        var name = await dialogService.DisplayPromptAsync(Strings.AddColumn, string.Empty, Strings.AddColumn, Strings.Delete);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var column = await boardService.AddColumnAsync(Id, name);
        Columns.Add(new ColumnViewModel(column, cardService));
    }

    [RelayCommand]
    private async Task DeleteColumnAsync(Guid columnId)
    {
        await boardService.DeleteColumnAsync(Id, columnId);
        var column = Columns.FirstOrDefault(c => c.Id == columnId);
        if (column is not null)
        {
            Columns.Remove(column);
        }
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
