using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Penban.Maui.Views.Services;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;
using Penban.ViewModels;
using Syncfusion.Maui.Kanban;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Shows a single board with Syncfusion's Kanban control. Cross-column moves are handled via the
/// control's native drag/drop pipeline and persisted through BoardViewModel.MoveCardCommand.
/// </summary>
public partial class BoardPage : ContentPage
{
    /// <summary>
    /// Lower bound for the computed column width. A board with many columns scrolls horizontally
    /// instead of squeezing the notes down to a size that can no longer be written on.
    /// </summary>
    private const double MinColumnWidth = 180;

    /// <summary>
    /// Side length a note starts out with, before the board has been measured. The note size follows
    /// the column width, so this is only what the very first layout pass works with.
    /// </summary>
    private const double DefaultNoteSize = 150;

    /// <summary>
    /// Share of the column width a note takes up. The rest of the column stays empty, so a note
    /// keeps a visible margin to the column edges instead of touching them.
    /// </summary>
    private const double NoteColumnFill = 0.8;

    /// <summary>
    /// Smallest note the content-driven sizing may produce. Below this a note would no longer be
    /// big enough to write on comfortably.
    /// </summary>
    private const double MinNoteSize = 100;

    /// <summary>
    /// Transparent margin the cell keeps around the note, so its rotation and its shadow stay
    /// inside the cell at every note size.
    /// </summary>
    private const double NoteCellPadding = 10;

    private readonly IPreferences preferences;
    private readonly TransferCoordinator transferCoordinator;
    private readonly List<(ColumnViewModel Column, PropertyChangedEventHandler Handler)> titleSubscriptions = [];

    private ObservableCollection<BoardKanbanCard> kanbanCards = new();
    private BoardViewModel? viewModel;
    private bool isLoaded;
    private bool suppressCardRebuild;

    /// <summary>
    /// Whether the notes are sized to their content. Read from the settings when the board appears
    /// so a change there is picked up without restarting the app; the field is what the card
    /// rebuild compares against.
    /// </summary>
    private bool autoSizeCards;

    /// <summary>Carries the owning <see cref="ColumnViewModel"/> on each <see cref="KanbanColumn"/>
    /// so header-template buttons (whose BindingContext is the Syncfusion column) can reach it.</summary>
    private static readonly BindableProperty ColumnViewModelProperty =
        BindableProperty.CreateAttached("ColumnViewModel", typeof(ColumnViewModel), typeof(BoardPage), null);

    public BoardPage(BoardViewModel viewModel, IPreferences preferences, TransferCoordinator transferCoordinator)
    {
        InitializeComponent();
        this.preferences = preferences;
        this.transferCoordinator = transferCoordinator;
        BindingContext = this.viewModel = viewModel;
        BoardKanban.ItemsSource = kanbanCards;
        BoardKanban.DragEnd += OnKanbanDragEnd;
        BoardKanban.SizeChanged += OnBoardKanbanSizeChanged;
        viewModel.Columns.CollectionChanged += OnColumnsChanged;
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;
    }

    /// <summary>
    /// Export the board or its cards, or import a file. The columns are handed over so that a card
    /// export can be appended to whichever one the user picks.
    /// </summary>
    private async void OnShareClicked(object? sender, EventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        if (await transferCoordinator.ShowBoardMenuAsync(
                viewModel.Id,
                viewModel.Columns.Select(column => new ColumnChoice(column.Id, column.Title)).ToList()))
        {
            // An import landed cards in this board, so the columns are rebuilt from the database.
            await LoadColumnsAndCardsAsync();
        }
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

        // Whether the board carries a note is the one thing the header shows about it, and it
        // changes in the editor - so it is re-read before either branch below.
        UpdateBoardNoteButton();

        if (isLoaded)
        {
            // Coming back from the ink editor. The card the editor worked on is the very instance
            // this board already holds, and both its note colour and its ink preview follow it
            // live, so reloading every column and rebuilding the whole board here only made
            // returning from the editor slow - on a full board, several seconds of nothing
            // happening. Only a deletion still has to be applied, because it happens on the card
            // and so cannot reach the column it came from.
            //
            // Content-driven note sizes are the other exception: the ink was drawn in the editor,
            // so the note that has to change size is the one that was just worked on. The setting
            // is re-read first so that it also takes effect without restarting the app.
            autoSizeCards = ReadAutoSizeCards();

            if (RemoveDeletedCards() | RefreshCardSizes())
            {
                RebuildKanbanCards();
            }

            return;
        }

        autoSizeCards = ReadAutoSizeCards();
        await LoadColumnsAndCardsAsync();
        RebuildKanbanColumns();
        RebuildKanbanCards();
        isLoaded = true;
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

        var columns = viewModel.Columns.ToList();
        foreach (var column in columns)
        {
            column.Cards.CollectionChanged -= OnCardsChanged;
            column.Cards.CollectionChanged += OnCardsChanged;
        }

        // Filling a column is a Clear() plus one Add() per card, and each of those raises
        // CollectionChanged - which used to rebuild the whole board. That made opening a board with
        // a few dozen cards quadratic, and it is why a big board took seconds to appear. Load
        // first, build the board once.
        suppressCardRebuild = true;
        try
        {
            await Task.WhenAll(columns.Select(column => column.LoadCardsCommand.ExecuteAsync(null)));
        }
        finally
        {
            suppressCardRebuild = false;
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

        UnsubscribeColumnTitles();
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

            // KanbanColumn.Title is a plain property with no binding behind it, so renaming a
            // column in the view model would otherwise only reach the board the next time the
            // whole column set is rebuilt.
            PropertyChangedEventHandler onColumnChanged = (_, e) =>
            {
                if (e.PropertyName == nameof(ColumnViewModel.Title))
                {
                    kanbanColumn.Title = column.Title;
                }
            };

            column.PropertyChanged += onColumnChanged;
            titleSubscriptions.Add((column, onColumnChanged));

            BoardKanban.Columns.Add(kanbanColumn);
        }

        UpdateColumnWidth();
    }

    private void OnBoardKanbanSizeChanged(object? sender, EventArgs e) => UpdateColumnWidth();

    /// <summary>
    /// Spreads the columns over the available width instead of leaving a gap at the right edge,
    /// while never going below <see cref="MinColumnWidth"/>. The note size follows the column width,
    /// so a new column width is also a new note size.
    /// </summary>
    private void UpdateColumnWidth()
    {
        if (viewModel is null || viewModel.Columns.Count == 0 || BoardKanban.Width <= 0)
        {
            return;
        }

        var computed = BoardKanban.Width / viewModel.Columns.Count;
        BoardKanban.ColumnWidth = Math.Max(MinColumnWidth, computed);
        RefreshCardSizes();
    }

    /// <summary>
    /// Drops the title subscriptions from the previous column set. <see cref="RebuildKanbanColumns"/>
    /// runs on every theme change and column change, and the event source outlives the columns, so
    /// without this each rebuild would leave another handler behind.
    /// </summary>
    private void UnsubscribeColumnTitles()
    {
        foreach (var (column, handler) in titleSubscriptions)
        {
            column.PropertyChanged -= handler;
        }

        titleSubscriptions.Clear();
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
                    NoteSize = NoteSizeFor(card),
                });
            }
        }

        kanbanCards = nextCards;
        BoardKanban.ItemsSource = kanbanCards;
    }

    private bool ReadAutoSizeCards() =>
        bool.TryParse(preferences.Get(PreferenceKeys.AutoSizeCards, "false"), out var enabled) && enabled;

    /// <summary>
    /// Side length a note grows to: a fixed share of its column width. The rest of the column is the
    /// margin the note keeps, so it is clearly wider than before without touching the column edges.
    /// </summary>
    private double NoteBaseSize =>
        Math.Max(MinNoteSize, BoardKanban.ColumnWidth * NoteColumnFill);

    /// <summary>
    /// Side length for one note: a share of the column width, unless the notes are meant to follow
    /// their content, in which case a note shrinks until the ink on it fills the note.
    /// </summary>
    private double NoteSizeFor(CardViewModel card)
    {
        var baseSize = NoteBaseSize;
        if (!autoSizeCards
            || card.InkCanvas.Strokes.Count == 0
            || !InkDocument.TryGetBounds(card.InkCanvas.Strokes, out var bounds)
            || bounds.MaxExtent <= 0)
        {
            return baseSize;
        }

        // The strokes live on a fixed square sheet; the note is the same sheet at a different size,
        // so the share of the sheet the ink uses is the share of the note it may fill.
        var share = Math.Min(1, bounds.MaxExtent / InkDocument.Size);
        return Math.Clamp(baseSize * share, MinNoteSize, baseSize);
    }

    /// <summary>
    /// Brings the note size of every card in line with its ink and reports whether any of them
    /// changed. Cards are only compared, never re-created, so this is cheap enough to run every
    /// time the board comes back into view.
    /// </summary>
    private bool RefreshCardSizes()
    {
        if (viewModel is null)
        {
            return false;
        }

        var changed = false;
        var shown = new Dictionary<Guid, BoardKanbanCard>();
        foreach (var item in kanbanCards)
        {
            shown[item.CardId] = item;
        }

        foreach (var card in viewModel.Columns.SelectMany(column => column.Cards))
        {
            var size = NoteSizeFor(card);
            if (shown.TryGetValue(card.Id, out var item) && Math.Abs(item.NoteSize - size) >= 0.5)
            {
                item.NoteSize = size;
                changed = true;
            }
        }

        return changed;
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

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    /// <summary>
    /// Removes the cards that were deleted in the ink editor from their columns. Deletion is the
    /// one edit the editor cannot apply where it happens - the delete command knows the card but not
    /// the column holding it. Returns whether anything went, so the caller can rebuild the board
    /// once instead of once per removed card.
    /// </summary>
    private bool RemoveDeletedCards()
    {
        if (viewModel is null)
        {
            return false;
        }

        var removed = false;
        suppressCardRebuild = true;
        try
        {
            foreach (var column in viewModel.Columns)
            {
                for (var i = column.Cards.Count - 1; i >= 0; i--)
                {
                    if (column.Cards[i].IsDeleted)
                    {
                        column.Cards.RemoveAt(i);
                        removed = true;
                    }
                }
            }
        }
        finally
        {
            suppressCardRebuild = false;
        }

        return removed;
    }

    /// <summary>
    /// Opens the ink editor for a card and applies whatever it changed about the board itself.
    /// </summary>
    private async Task OpenCardEditorAsync(CardViewModel card)
    {
        await Navigation.PushModalAsync(new CardInkEditorPage(card, preferences));

        if (RemoveDeletedCards())
        {
            RebuildKanbanCards();
        }
    }

    /// <summary>
    /// Opens the ink editor for the board's own note - the note that stands for the whole board and
    /// shows up on the overview and on the home screen. There is no separate "create" step: a board
    /// that has no note gets one by writing on it, and a note that is erased again is dropped.
    /// </summary>
    private async void OnBoardNoteClicked(object? sender, EventArgs e)
    {
        if (viewModel is null)
        {
            return;
        }

        await Navigation.PushModalAsync(new CardInkEditorPage(viewModel.CreateNoteEditor(), preferences));
        UpdateBoardNoteButton();
    }

    /// <summary>
    /// Lights the note button while the board carries a note, so the header says whether there is
    /// one without the note having to be opened.
    /// </summary>
    private void UpdateBoardNoteButton()
    {
        var resources = Application.Current!.Resources;
        var hasNote = viewModel?.HasNote ?? false;
        BoardNoteButton.Style = (Style)resources[hasNote ? "AccentIconButton" : "GhostIconButton"];
    }

    private async void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not BoardKanbanCard card)
        {
            return;
        }

        await OpenCardEditorAsync(card.Card);
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
            await OpenCardEditorAsync(newCard);
        }
    }

    private async void OnRenameColumnTapped(object? sender, EventArgs e)
    {
        if (viewModel is null
            || (sender as BindableObject)?.BindingContext is not KanbanColumn kanbanColumn
            || kanbanColumn.GetValue(ColumnViewModelProperty) is not ColumnViewModel columnViewModel)
        {
            return;
        }

        await viewModel.RenameColumnCommand.ExecuteAsync(columnViewModel.Id);
    }

    private async void OnDeleteColumnClicked(object? sender, EventArgs e)
    {
        if (viewModel is null
            || (sender as BindableObject)?.BindingContext is not KanbanColumn kanbanColumn
            || kanbanColumn.GetValue(ColumnViewModelProperty) is not ColumnViewModel columnViewModel)
        {
            return;
        }

        await viewModel.DeleteColumnCommand.ExecuteAsync(columnViewModel.Id);
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

    public sealed class BoardKanbanCard : INotifyPropertyChanged
    {
        private double noteSize = DefaultNoteSize;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid CardId { get; init; }

        public CardViewModel Card { get; init; } = null!;

        public string Category { get; set; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        /// <summary>Side length of the note, and of the transparent cell that holds it.</summary>
        public double NoteSize
        {
            get => noteSize;
            set
            {
                if (Math.Abs(noteSize - value) < 0.5)
                {
                    return;
                }

                noteSize = value;
                Raise(nameof(NoteSize));
                Raise(nameof(CellSize));
            }
        }

        /// <summary>
        /// The note plus the margin its rotation and its shadow need, so the corners are never cut
        /// off at the cell edge.
        /// </summary>
        public double CellSize => NoteSize + (2 * NoteCellPadding);

        private void Raise(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
