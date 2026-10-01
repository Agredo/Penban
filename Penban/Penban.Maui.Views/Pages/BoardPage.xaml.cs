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
    /// Gap the Kanban control keeps between two columns. It is not configurable, so it is mirrored
    /// here: the width a column is given has to leave room for it, or the columns hang over the right
    /// edge of the control and the last of them is cut off.
    /// </summary>
    private const double KanbanColumnSpacing = 5;

    /// <summary>
    /// Smallest note the content-driven sizing may produce. Below this a note would no longer be
    /// big enough to write on comfortably.
    /// </summary>
    private const double MinNoteSize = 100;

    /// <summary>
    /// Paper the note keeps around its ink. Mirrors the note's padding in BoardPage.xaml, so the ink
    /// inside a note is laid out in the same box whether the note follows its content or not.
    /// </summary>
    private const double NoteInset = 7;

    /// <summary>
    /// Space a content-driven note keeps around its ink, in document units. Strokes are measured by
    /// their centre line, so half of the thickest one is added on top of this to keep it inside.
    /// </summary>
    private const float InkCropMargin = 24f;

    /// <summary>
    /// Transparent margin the cell keeps around the note, so its rotation and its shadow stay
    /// inside the cell at every note size.
    /// </summary>
    private const double NoteCellPadding = 10;

    private readonly SettingsViewModel settingsViewModel;
    private readonly FeedbackViewModel feedbackViewModel;
    private readonly IPreferences preferences;
    private readonly TransferCoordinator transferCoordinator;
    private readonly List<(ColumnViewModel Column, PropertyChangedEventHandler Handler)> titleSubscriptions = [];

    private ObservableCollection<BoardKanbanCard> kanbanCards = new();
    private BoardViewModel? viewModel;
    private bool isLoaded;
    private bool suppressCardRebuild;

    /// <summary>
    /// The column width the control was last given, so that a layout pass that leaves the width
    /// alone does not write the same three values into the control again.
    /// </summary>
    private double appliedColumnWidth = double.NaN;

    /// <summary>
    /// A note to open as soon as the board is loaded, or <c>null</c> for a board that was opened for
    /// its own sake. Set when the board was reached through a search result; cleared once it has
    /// been opened, so returning to the board does not open it again.
    /// </summary>
    private Guid? openCardId;

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

    public BoardPage(
        BoardViewModel viewModel,
        SettingsViewModel settingsViewModel,
        FeedbackViewModel feedbackViewModel,
        IPreferences preferences,
        TransferCoordinator transferCoordinator,
        Guid? openCardId = null)
    {
        InitializeComponent();
        this.settingsViewModel = settingsViewModel;
        this.feedbackViewModel = feedbackViewModel;
        this.preferences = preferences;
        this.transferCoordinator = transferCoordinator;
        this.openCardId = openCardId;
        BindingContext = this.viewModel = viewModel;
        BoardKanban.ItemsSource = kanbanCards;
        BoardKanban.DragEnd += OnKanbanDragEnd;
        BoardKanban.SizeChanged += OnBoardKanbanSizeChanged;
        viewModel.Columns.CollectionChanged += OnColumnsChanged;
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;
    }

    /// <summary>
    /// Opens the settings from the board. The drawing settings are tried out on a note, and going
    /// back to the overview for each one loses the board and the note that was being worked on.
    /// </summary>
    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new SettingsPage(settingsViewModel, feedbackViewModel));
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

        // A board opened from a search result carries the note that was searched for, and that note
        // is what the tap asked for: the board is only the way there. Dispatched rather than opened
        // here, because the board is still being pushed while this runs.
        if (openCardId is { } wanted)
        {
            openCardId = null;
            Dispatcher.Dispatch(() => OpenSearchedCard(wanted));
        }
    }

    /// <summary>
    /// Opens the note a search result pointed at. A note that is gone by now - deleted while the
    /// search page was open - leaves the board showing, which is what a tap on the board itself
    /// would have got.
    /// </summary>
    private async void OpenSearchedCard(Guid cardId)
    {
        if (viewModel is null)
        {
            return;
        }

        var card = viewModel.Columns
            .SelectMany(column => column.Cards)
            .FirstOrDefault(candidate => candidate.Id == cardId);

        if (card is not null)
        {
            await OpenCardEditorAsync(card);
        }
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
    /// <remarks>
    /// The control takes the width it is given only as long as it fits between its own bounds, and
    /// those are 250 and 450 unless they are set: on a window wide enough for two columns to be wider
    /// than that, every column stopped at 450 while the notes went on being sized for the width that
    /// had been asked for - and they were cut off at the side of the column. The bounds are therefore
    /// moved along with the width instead of being left at their defaults.
    /// </remarks>
    private void UpdateColumnWidth()
    {
        if (viewModel is null || viewModel.Columns.Count == 0 || BoardKanban.Width <= 0)
        {
            return;
        }

        var computed = ColumnWidthFor(BoardKanban.Width, viewModel.Columns.Count);
        if (computed != appliedColumnWidth)
        {
            appliedColumnWidth = computed;
            BoardKanban.MinimumColumnWidth = computed;
            BoardKanban.MaximumColumnWidth = computed;
            BoardKanban.ColumnWidth = computed;
        }

        RefreshCardSizes();
    }

    /// <summary>
    /// Width one column gets from the width the control has to offer: the width split over the
    /// columns, less the gaps between them, and never below <see cref="MinColumnWidth"/> - a board
    /// with more columns than fit scrolls sideways instead of squeezing the notes down to a size that
    /// can no longer be written on.
    /// </summary>
    private static double ColumnWidthFor(double availableWidth, int columnCount) =>
        Math.Max(MinColumnWidth, (availableWidth - ((columnCount - 1) * KanbanColumnSpacing)) / columnCount);

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
                    Layout = NoteLayoutFor(card),
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
    /// Scale the ink is drawn with on a note. This is the scale a full note uses, so a note that only
    /// takes up the height its ink needs shows that ink at the size it has everywhere else instead of
    /// shrinking it along with the note.
    /// </summary>
    private double InkScale =>
        Math.Max(1, NoteBaseSize - (2 * NoteInset)) / InkDocument.Size;

    /// <summary>
    /// Size of one note and the part of the sheet it shows. Unless the notes follow their content,
    /// that is the whole sheet at the usual note size. With it, the note keeps the width it always
    /// had - a share of the column - but only as much height as its ink needs: the sheet is cut above
    /// and below the drawing, so a note with little on it stops carrying a block of blank paper.
    /// Horizontally nothing moves, so the ink stays the size and in the place it always had.
    /// </summary>
    private NoteLayout NoteLayoutFor(CardViewModel card)
    {
        var baseSize = NoteBaseSize;
        var wholeSheet = new NoteLayout(
            baseSize,
            baseSize,
            new RectF(0f, 0f, InkDocument.Size, InkDocument.Size));

        if (!autoSizeCards
            || card.InkCanvas.Strokes.Count == 0
            || !InkDocument.TryGetBounds(card.InkCanvas.Strokes, out var bounds)
            || bounds.MaxExtent <= 0)
        {
            return wholeSheet;
        }

        var scale = InkScale;

        // Cut above and below the ink, keeping the room the strokes themselves need: they are
        // measured along their centre line, so half of the thickest one is added to the margin.
        var margin = InkCropMargin + (ThickestStroke(card) / 2f);
        var top = Math.Max(0f, bounds.MinY - margin);
        var bottom = Math.Min(InkDocument.Size, bounds.MaxY + margin);
        var cropHeight = Math.Max(bottom - top, 1f);

        // The full width of the sheet stays in view, so the ink lands where it always did.
        return new NoteLayout(
            baseSize,
            (cropHeight * scale) + (2 * NoteInset),
            new RectF(0f, top, InkDocument.Size, cropHeight));
    }

    /// <summary>Width of the thickest stroke on the card, in document units.</summary>
    private static float ThickestStroke(CardViewModel card)
    {
        var thickness = 0f;
        foreach (var stroke in card.InkCanvas.Strokes)
        {
            thickness = MathF.Max(thickness, stroke.Thickness);
        }

        return thickness;
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
            var layout = NoteLayoutFor(card);
            if (shown.TryGetValue(card.Id, out var item) && item.Layout != layout)
            {
                item.Layout = layout;
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
    /// The editor is a page of the shell's own stack, so the board keeps its place under it and the
    /// shell's bar gives the editor its title and its way back. Pushing returns as soon as the editor
    /// is up, the way a modal push does, so what the editor changed about the board is not read off
    /// here but on the way back, see <see cref="OnAppearing"/>.
    /// </summary>
    private async Task OpenCardEditorAsync(CardViewModel card)
    {
        await Navigation.PushAsync(new CardInkEditorPage(card, preferences, settingsViewModel, feedbackViewModel, viewModel?.Title ?? string.Empty));
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

        await Navigation.PushAsync(new CardInkEditorPage(viewModel.CreateNoteEditor(), preferences, settingsViewModel, feedbackViewModel, viewModel.Title));
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
        private NoteLayout layout = new(
            DefaultNoteSize,
            DefaultNoteSize,
            new RectF(0f, 0f, InkDocument.Size, InkDocument.Size));

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid CardId { get; init; }

        public CardViewModel Card { get; init; } = null!;

        public string Category { get; set; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        /// <summary>
        /// Size of the note and the part of the sheet it shows. Comes from the ink the card carries,
        /// see <see cref="NoteLayoutFor"/>.
        /// </summary>
        public NoteLayout Layout
        {
            get => layout;
            set
            {
                if (layout == value)
                {
                    return;
                }

                layout = value;
                Raise(nameof(NoteWidth));
                Raise(nameof(NoteHeight));
                Raise(nameof(NoteSource));
                Raise(nameof(CellWidth));
                Raise(nameof(CellHeight));
            }
        }

        /// <summary>Width of the note.</summary>
        public double NoteWidth => layout.Width;

        /// <summary>Height of the note.</summary>
        public double NoteHeight => layout.Height;

        /// <summary>Part of the ink document the note shows.</summary>
        public RectF NoteSource => layout.Source;

        /// <summary>
        /// The note plus the margin its rotation and its shadow need, so the corners are never cut
        /// off at the cell edge.
        /// </summary>
        public double CellWidth => NoteWidth + (2 * NoteCellPadding);

        /// <summary>See <see cref="CellWidth"/>.</summary>
        public double CellHeight => NoteHeight + (2 * NoteCellPadding);

        private void Raise(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// How one note on the board is drawn: the size of its paper and the part of the ink document it
/// shows. Unless the notes follow their content, the paper is the whole sheet at the usual note size.
/// </summary>
/// <param name="Width">Width of the paper, in the units the note's views are laid out in.</param>
/// <param name="Height">Height of the paper.</param>
/// <param name="Source">Part of the ink document the note shows, in document units.</param>
public readonly record struct NoteLayout(double Width, double Height, RectF Source);
