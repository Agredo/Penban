using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Penban.ViewModels;
using Syncfusion.Maui.Kanban;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Shows a single board with Syncfusion's Kanban control. Cross-column moves are handled via the
/// control's native drag/drop pipeline and persisted through BoardViewModel.MoveCardCommand.
/// </summary>
public partial class BoardPage : ContentPage
{
    private ObservableCollection<BoardKanbanCard> kanbanCards = new();
    private BoardViewModel? viewModel;
    private bool isLoaded;
    private bool suppressCardRebuild;

    /// <summary>Carries the owning <see cref="ColumnViewModel"/> on each <see cref="KanbanColumn"/>
    /// so header-template buttons (whose BindingContext is the Syncfusion column) can reach it.</summary>
    private static readonly BindableProperty ColumnViewModelProperty =
        BindableProperty.CreateAttached("ColumnViewModel", typeof(ColumnViewModel), typeof(BoardPage), null);

    public BoardPage(BoardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        BoardKanban.ItemsSource = kanbanCards;
        BoardKanban.DragEnd += OnKanbanDragEnd;
        viewModel.Columns.CollectionChanged += OnColumnsChanged;
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        // Column backgrounds are plain brushes (AppThemeBinding isn't constructible from
        // code), so re-apply them when the OS theme flips.
        if (isLoaded)
        {
            RebuildKanbanColumns();
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (viewModel is null)
        {
            return;
        }

        await LoadColumnsAndCardsAsync();
        if (!isLoaded)
        {
            RebuildKanbanColumns();
            isLoaded = true;
        }

        RebuildKanbanCards();
    }

    private async void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<ColumnViewModel>())
            {
                item.Cards.CollectionChanged -= OnCardsChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ColumnViewModel>())
            {
                item.Cards.CollectionChanged += OnCardsChanged;
                await item.LoadCardsCommand.ExecuteAsync(null);
            }
        }

        RebuildKanbanColumns();
        RebuildKanbanCards();
    }

    private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!suppressCardRebuild)
        {
            RebuildKanbanCards();
        }
    }

    private async Task LoadColumnsAndCardsAsync()
    {
        if (viewModel is null)
        {
            return;
        }

        foreach (var column in viewModel.Columns)
        {
            column.Cards.CollectionChanged -= OnCardsChanged;
            column.Cards.CollectionChanged += OnCardsChanged;
            await column.LoadCardsCommand.ExecuteAsync(null);
        }
    }

    private void RebuildKanbanColumns()
    {
        if (viewModel is null)
        {
            return;
        }

        var resources = Application.Current!.Resources;
        var isDark = Application.Current.RequestedTheme == AppTheme.Dark;
        var columnBrush = new SolidColorBrush((Color)resources[isDark ? "SurfaceSecondaryDark" : "SurfaceSecondaryLight"]);

        var accent = (Color)resources[isDark ? "AccentDark" : "AccentLight"];
        var placeholderStyle = new KanbanPlaceholderStyle
        {
            Background = new SolidColorBrush(accent.WithAlpha(0.14f)),
            Stroke = new SolidColorBrush(accent.WithAlpha(0.6f)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
        };

        BoardKanban.Columns.Clear();
        foreach (var column in viewModel.Columns)
        {
            var kanbanColumn = new KanbanColumn
            {
                Title = column.Title,
                Categories = new List<object> { column.Id.ToString() },
                PlaceholderStyle = placeholderStyle,
                // Replaces the control's stark white default column panel.
                Background = columnBrush,
            };
            kanbanColumn.SetValue(ColumnViewModelProperty, column);
            BoardKanban.Columns.Add(kanbanColumn);
        }
    }

    private void RebuildKanbanCards()
    {
        if (viewModel is null)
        {
            return;
        }

        var nextCards = new ObservableCollection<BoardKanbanCard>();
        foreach (var column in viewModel.Columns)
        {
            foreach (var card in column.Cards.OrderBy(c => c.SortOrder))
            {
                nextCards.Add(new BoardKanbanCard
                {
                    CardId = card.Id,
                    Card = card,
                    Category = column.Id.ToString(),
                    Title = card.Id.ToString()[..8],
                });
            }
        }

        kanbanCards = nextCards;
        BoardKanban.ItemsSource = kanbanCards;
    }

    private async void OnKanbanDragEnd(object? sender, KanbanDragEndEventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        // Syncfusion has already applied the drop to the bound collection by the time this
        // event fires (card removed from source, inserted at target, Category updated).
        // Neither cancel the drop nor rebuild the ItemsSource here: replacing the collection
        // mid-pipeline is what raced the control's own insert/revert step and produced
        // duplicate cards in the target (without Cancel) or source (with Cancel) column.
        // Instead, persist the move silently and mirror it in the view model.
        if (!TryGetDraggedCardId(e.Data, out var draggedCardId)
            || !Guid.TryParse(e.TargetCategory?.ToString(), out var targetColumnId))
        {
            return;
        }

        var sourceColumn = viewModel.Columns.FirstOrDefault(c => c.Cards.Any(card => card.Id == draggedCardId));
        var targetColumn = viewModel.Columns.FirstOrDefault(c => c.Id == targetColumnId);
        if (sourceColumn is null || targetColumn is null)
        {
            return;
        }

        await viewModel.MoveCardCommand.ExecuteAsync(new CardMoveRequest(
            draggedCardId,
            sourceColumn.Id,
            targetColumnId,
            e.TargetIndex));

        // Keep the view model consistent with what the control now displays, without
        // triggering a rebuild of the ItemsSource.
        var cardViewModel = sourceColumn.Cards.First(c => c.Id == draggedCardId);
        suppressCardRebuild = true;
        try
        {
            sourceColumn.Cards.Remove(cardViewModel);
            var insertIndex = Math.Clamp(e.TargetIndex, 0, targetColumn.Cards.Count);
            targetColumn.Cards.Insert(insertIndex, cardViewModel);

            ReindexColumn(sourceColumn);
            if (targetColumn != sourceColumn)
            {
                ReindexColumn(targetColumn);
            }
        }
        finally
        {
            suppressCardRebuild = false;
        }

        if (e.Data is BoardKanbanCard boardCard)
        {
            boardCard.Category = targetColumnId.ToString();
        }
    }

    private static void ReindexColumn(ColumnViewModel column)
    {
        for (var i = 0; i < column.Cards.Count; i++)
        {
            column.Cards[i].UpdatePlacement(column.Id, i);
        }
    }

    private async void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not BoardKanbanCard card)
        {
            return;
        }

        await Navigation.PushModalAsync(new CardInkEditorPage(card.Card));
    }

    private async void OnAddCardClicked(object? sender, EventArgs e)
    {
        // The header template's BindingContext is the Syncfusion KanbanColumn; the owning
        // ColumnViewModel rides along via the attached property set in RebuildKanbanColumns.
        if ((sender as BindableObject)?.BindingContext is not KanbanColumn kanbanColumn
            || kanbanColumn.GetValue(ColumnViewModelProperty) is not ColumnViewModel columnViewModel)
        {
            return;
        }

        await columnViewModel.AddCardCommand.ExecuteAsync(null);

        // Pen-first flow: a fresh card is an empty canvas, so open the ink editor right away.
        var newCard = columnViewModel.Cards.LastOrDefault();
        if (newCard is not null)
        {
            await Navigation.PushModalAsync(new CardInkEditorPage(newCard));
        }
    }

    private static bool TryGetDraggedCardId(object? data, out Guid cardId)
    {
        switch (data)
        {
            case BoardKanbanCard boardCard:
                cardId = boardCard.CardId;
                return true;
            case CardViewModel cardViewModel:
                cardId = cardViewModel.Id;
                return true;
            default:
                cardId = Guid.Empty;
                return false;
        }
    }

    public sealed class BoardKanbanCard
    {
        public Guid CardId { get; init; }

        public CardViewModel Card { get; init; } = null!;

        public string Category { get; set; } = string.Empty;

        public string Title { get; init; } = string.Empty;
    }
}
