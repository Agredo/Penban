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

    /// <summary>
    /// Height of one line of a note's writing, as a multiple of its size. What the board counts with
    /// when it works out how many lines of a body fit on a note - the platform's own line spacing is
    /// not something a note can be laid out from beforehand.
    /// </summary>
    private const double LineHeight = 1.4;

    /// <summary>
    /// Lines the title of a note may take. A title is a headline, so it is given a short leash: what
    /// does not fit in two lines is cut off rather than kept from the body.
    /// </summary>
    private const int TitleLines = 2;

    private readonly SettingsViewModel settingsViewModel;
    private readonly FeedbackViewModel feedbackViewModel;
    private readonly IPreferences preferences;
    private readonly TransferCoordinator transferCoordinator;
    private readonly IDialogService dialogService;
    private readonly List<(ColumnViewModel Column, PropertyChangedEventHandler Handler)> titleSubscriptions = [];
    private readonly SearchViewModel? searchViewModel;
    private readonly HashSet<string> filterTags = [];
    private readonly Dictionary<string, Button> filterTagButtons = [];
    private CancellationTokenSource? filterSearch;

    /// <summary>Notes whose text matches the typed search, or <c>null</c> while nothing is typed.</summary>
    private HashSet<Guid>? textMatches;

    private ObservableCollection<BoardKanbanCard> kanbanCards = new();
    private BoardViewModel? viewModel;
    private bool isLoaded;

    /// <summary>
    /// How many loads are filling a column right now, see <see cref="BeginCardRebuildSuppression"/>.
    /// Counted rather than a plain flag because the loads overlap: the board loads its columns while
    /// the overview may be re-reading the board behind it, and the first load to finish must not
    /// clear the suppression the other one is still working under.
    /// </summary>
    private int cardRebuildSuppressions;

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

    /// <summary>
    /// The notes the user has picked on the board, by card id. Only ever filled while
    /// <see cref="isSelecting"/>, and only with notes the board is showing.
    /// </summary>
    private readonly HashSet<Guid> pickedCards = [];

    /// <summary>
    /// Whether the board is picking notes instead of opening them, see <see cref="OnSelectCardsClicked"/>.
    /// </summary>
    private bool isSelecting;

    /// <summary>
    /// The note a drag that is in the air is carrying, or <c>null</c> while nothing is being dragged.
    /// The control reports nothing about a drag that ends outside its columns - which is exactly the
    /// one that ends on the bin - so the note is taken down while it can still be asked for.
    /// </summary>
    private Guid? draggedCardId;

    /// <summary>
    /// Whether the bin is filled, i.e. whether a note is being held over it. Its colour is the only
    /// thing that says so, and the fill has to be taken off again when the note is carried away.
    /// </summary>
    private bool isBinHot;

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
        IDialogService dialogService,
        SearchViewModel? searchViewModel = null,
        Guid? openCardId = null)
    {
        InitializeComponent();
        this.settingsViewModel = settingsViewModel;
        this.feedbackViewModel = feedbackViewModel;
        this.preferences = preferences;
        this.transferCoordinator = transferCoordinator;
        this.dialogService = dialogService;
        this.openCardId = openCardId;
        this.searchViewModel = searchViewModel;
        BuildFilterTags();
        BindingContext = this.viewModel = viewModel;
        BoardKanban.ItemsSource = kanbanCards;
        BoardKanban.DragStart += OnKanbanDragStart;
        BoardKanban.DragEnd += OnKanbanDragEnd;
        BoardKanban.SizeChanged += OnBoardKanbanSizeChanged;
    }

    /// <summary>
    /// Subscribes to the board for this visit. Called from <see cref="OnAppearing"/> rather than from
    /// the constructor because the view model outlives the page: the overview keeps its rows, and a
    /// board is opened into a new page every time, so the handlers have to follow the page.
    /// </summary>
    private void SubscribeToBoard()
    {
        if (viewModel is null)
        {
            return;
        }

        // Detach first, so that a page that comes back into view - from the ink editor, or from a
        // window that was in the background - ends up with exactly one handler either way.
        viewModel.Columns.CollectionChanged -= OnColumnsChanged;
        viewModel.Columns.CollectionChanged += OnColumnsChanged;

        foreach (var column in viewModel.Columns)
        {
            column.Cards.CollectionChanged -= OnCardsChanged;
            column.Cards.CollectionChanged += OnCardsChanged;
        }

        Application.Current!.RequestedThemeChanged -= OnRequestedThemeChanged;
        Application.Current!.RequestedThemeChanged += OnRequestedThemeChanged;

        foreach (var (column, handler) in titleSubscriptions)
        {
            column.PropertyChanged -= handler;
            column.PropertyChanged += handler;
        }
    }

    /// <summary>
    /// Undoes <see cref="SubscribeToBoard"/>. A handler left behind goes on answering the overview's
    /// refresh - reading every lane of a board that is no longer on screen, and building a board
    /// nobody is looking at for it - once more for every visit the board has ever had. The title
    /// handlers are kept in the list but let go of, so that a page that is gone does not stay alive
    /// through the columns of the board it was showing.
    /// </summary>
    private void UnsubscribeFromBoard()
    {
        if (viewModel is null)
        {
            return;
        }

        viewModel.Columns.CollectionChanged -= OnColumnsChanged;

        foreach (var column in viewModel.Columns)
        {
            column.Cards.CollectionChanged -= OnCardsChanged;
        }

        Application.Current!.RequestedThemeChanged -= OnRequestedThemeChanged;

        foreach (var (column, handler) in titleSubscriptions)
        {
            column.PropertyChanged -= handler;
        }
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

        // The bin is painted in plain colours too, and that painting is what carries the fill it is
        // wearing at this moment.
        PaintBin();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (viewModel is null)
        {
            return;
        }

        SubscribeToBoard();

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

            if (RemoveDeletedCards() | RefreshCardSizes() | IsFiltering)
            {
                RebuildKanbanCards();
            }

            // Then, and not before the board is back on screen: the home screen's widget shows a
            // picture of the overview, and that picture is drawn from the overview's rows - which
            // know of a note only once it told them. Nothing else would: this page is left for the
            // app itself rather than for the overview, and reading the rows is the app's job while it
            // is in front, since the database file is given back the moment it is not (see
            // MauiProgram). One row is read here, not the whole list.
            await viewModel.LoadSummaryAsync();

            return;
        }

        autoSizeCards = ReadAutoSizeCards();

        // The board is the one thing on this page that is not there yet: it is read lane by lane and
        // built note by note, and until the notes stand the page shows nothing but an empty board.
        // Said out loud, because a board with a lot on it takes long enough for the wait to read as a
        // tap that was missed - and taken back in the finally, so that a load that fails cannot leave
        // the spinner turning over a board that will never come.
        viewModel.IsLoading = true;
        try
        {
            await LoadColumnsAndCardsAsync();
            RebuildKanbanColumns();
            RebuildKanbanCards();
        }
        finally
        {
            viewModel.IsLoading = false;
        }

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
    /// Lets go of the board while this page is off screen - it stays off screen behind the ink
    /// editor, and it is left behind for good when the reader goes back to the overview. The page is
    /// gone from that point on, but the view model it showed is not, so what is unsubscribed here
    /// would otherwise keep reading lanes for a board that no longer exists on screen.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        UnsubscribeFromBoard();
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

        var added = e.NewItems?.OfType<ColumnViewModel>().ToList() ?? [];
        foreach (var item in added)
        {
            item.Cards.CollectionChanged -= OnCardsChanged;
            item.Cards.CollectionChanged += OnCardsChanged;
        }

        // A lane that already carries its cards is not read again: it either only changed places, or
        // it is one the board is showing while the overview re-read the board behind it. Loading is
        // what a reader saw as the lanes coming back one after the other - each lane that was read
        // filled its cards one at a time, and every one of those rebuilt the whole board.
        var pending = added.Where(item => item.Cards.Count == 0).ToList();
        BeginCardRebuildSuppression();
        try
        {
            await Task.WhenAll(pending.Select(item => item.LoadCardsCommand.ExecuteAsync(null)));
        }
        finally
        {
            EndCardRebuildSuppression();
        }

        RebuildKanbanColumns();
        RebuildKanbanCards();
    }

    private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (cardRebuildSuppressions == 0)
        {
            RebuildKanbanCards();
        }
    }

    /// <summary>
    /// Holds off the board rebuild while a column is being filled. Filling one is a <c>Clear()</c>
    /// plus one <c>Add()</c> per card, and each of those raises <c>CollectionChanged</c>; rebuilding
    /// for every card made opening a board quadratic, which is why a board with a few dozen notes
    /// took seconds to appear. Load first, build the board once.
    /// </summary>
    private void BeginCardRebuildSuppression() => cardRebuildSuppressions++;

    private void EndCardRebuildSuppression()
    {
        if (cardRebuildSuppressions > 0)
        {
            cardRebuildSuppressions--;
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

        BeginCardRebuildSuppression();
        try
        {
            await Task.WhenAll(columns.Select(column => column.LoadCardsCommand.ExecuteAsync(null)));
        }
        finally
        {
            EndCardRebuildSuppression();
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
                AllowDrag = CanDragCards,
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
                if (!MatchesFilter(card))
                {
                    continue;
                }

                nextCards.Add(new BoardKanbanCard
                {
                    CardId = card.Id,
                    Card = card,
                    Category = column.Id.ToString(),
                    Title = card.Id.ToString()[..8],
                    Layout = NoteLayoutFor(card),
                    ShowPicker = isSelecting,
                    IsPicked = pickedCards.Contains(card.Id),
                });
            }
        }

        kanbanCards = nextCards;
        BoardKanban.ItemsSource = kanbanCards;
    }

    private bool IsFiltering => filterTags.Count > 0 || textMatches is not null;

    /// <summary>
    /// Whether notes may be dragged at all. Two things take that away: a filter, because the drop
    /// index the control reports is a position among the notes it shows and not a position in the
    /// column - and the picking, where a note that can be dragged swallows the tap that picks it.
    /// </summary>
    private bool CanDragCards => !IsFiltering && !isSelecting;

    private bool MatchesFilter(CardViewModel card) =>
        (textMatches is null || textMatches.Contains(card.Id))
        && (filterTags.Count == 0 || card.Tags.Any(filterTags.Contains));

    /// <summary>One toggle per tag the editor offers; a chosen one narrows the board to the notes that carry it.</summary>
    private void BuildFilterTags()
    {
        foreach (var tag in CardTags.Palette)
        {
            var button = new Button
            {
                Text = tag,
                FontSize = 20,
                Style = (Style)Application.Current!.Resources["GhostIconButton"],
            };
            button.Clicked += (_, _) =>
            {
                if (!filterTags.Remove(tag))
                {
                    filterTags.Add(tag);
                }

                button.BackgroundColor = filterTags.Contains(tag) ? Color.FromArgb("#33808080") : Colors.Transparent;
                ApplyFilter();
            };
            filterTagButtons[tag] = button;
            FilterTagRow.Add(button);
        }
    }

    /// <summary>
    /// Slides the tags in beside the button, or tucks them away. Only one of the two panels is out at
    /// a time, so the title keeps room on a narrow screen; a filter that is set stays in force while
    /// its panel is tucked away, and the accent on its button says so.
    /// </summary>
    private void OnFilterClicked(object? sender, EventArgs e)
    {
        FilterTagScroll.IsVisible = !FilterTagScroll.IsVisible;
        SearchEntryHost.IsVisible = false;
    }

    private void OnSearchClicked(object? sender, EventArgs e)
    {
        SearchEntryHost.IsVisible = !SearchEntryHost.IsVisible;
        FilterTagScroll.IsVisible = false;
        if (SearchEntryHost.IsVisible)
        {
            FilterEntry.Focus();
        }
    }
    private async void OnFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
        filterSearch?.Cancel();
        var text = e.NewTextValue?.Trim() ?? string.Empty;
        if (text.Length == 0 || searchViewModel is null)
        {
            textMatches = null;
            ApplyFilter();
            return;
        }

        var source = filterSearch = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, source.Token);
            var matches = await searchViewModel.FindCardIdsAsync(text);
            if (source.IsCancellationRequested)
            {
                return;
            }

            textMatches = matches;
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyFilter()
    {
        var resources = Application.Current!.Resources;
        FilterButton.Style = (Style)resources[filterTags.Count > 0 ? "AccentIconButton" : "GhostIconButton"];
        SearchButton.Style = (Style)resources[textMatches is not null ? "AccentIconButton" : "GhostIconButton"];

        // The drop index of a drag is a position among the notes that are shown, which is not a
        // position in the column while some of them are left out.
        foreach (var column in BoardKanban.Columns)
        {
            column.AllowDrag = CanDragCards;
        }
        RebuildKanbanCards();
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
            if (!shown.TryGetValue(card.Id, out var item))
            {
                continue;
            }

            if (item.Layout != layout)
            {
                item.Layout = layout;
                changed = true;
            }

            // The note's writing is read off the card, and the card is what the editor wrote into
            // while this board was off screen: a note that came back with other text - or with
            // another size, colour or weight - is put in step here, where every returning visit
            // passes anyway, instead of building the whole board again for it.
            item.RefreshText();
        }

        return changed;
    }

    /// <summary>
    /// Takes the note down while it is in the air and puts the bin up in the header's place. The bin
    /// is what a note can be dropped on to be deleted; the header stays underneath it, so the board
    /// below keeps its height and nothing the drag is aiming at moves.
    /// </summary>
    private void OnKanbanDragStart(object? sender, KanbanDragStartEventArgs e)
    {
        draggedCardId = TryGetDraggedCardId(e.Data, out var cardId) ? cardId : null;

        // A drag the app was never told the end of leaves the bin filled; a drag that follows starts
        // from the plain bin rather than from the last drag's colour.
        SetBinHot(false);
        TrashDropArea.IsVisible = draggedCardId is not null;
    }

    /// <summary>Puts the header back and forgets the note that was in the air.</summary>
    private void HideBin()
    {
        SetBinHot(false);
        TrashDropArea.IsVisible = false;
        draggedCardId = null;
    }

    /// <summary>
    /// Fills the bin as soon as the note is over it, so that it is clear that letting go here is what
    /// deletes the note: the area is not much to look at from under a finger, and nothing else about
    /// the board changes until the note is actually let go.
    /// </summary>
    private void OnTrashDragOver(object? sender, DragEventArgs e) => SetBinHot(true);

    /// <summary>Drains the bin again when the note is carried away from it.</summary>
    private void OnTrashDragLeave(object? sender, DragEventArgs e) => SetBinHot(false);

    /// <summary>Fills or drains the bin. A drag reports every move it makes, so the colour of a bin
    /// that already looks the way it should is left alone.</summary>
    private void SetBinHot(bool hot)
    {
        if (isBinHot == hot)
        {
            return;
        }

        isBinHot = hot;
        PaintBin();
    }

    /// <summary>
    /// Paints the bin for the state it is in. The colours are read from the theme every time rather
    /// than declared as an AppThemeBinding, because a plain colour set from code outlives a theme
    /// flip: this is also what the page calls when the OS theme moves on while the bin is filled.
    /// </summary>
    private void PaintBin()
    {
        var resources = Application.Current!.Resources;
        var dark = Application.Current.RequestedTheme == AppTheme.Dark;
        var surface = (Color)resources[dark ? "SurfaceSecondaryDark" : "SurfaceSecondaryLight"];
        var danger = (Color)resources[dark ? "DangerDark" : "DangerLight"];
        var onFill = (Color)resources[dark ? "BackgroundDark" : "BackgroundLight"];

        TrashDropArea.Background = new SolidColorBrush(isBinHot ? danger : surface);
        DeleteDropIcon.TextColor = isBinHot ? onFill : danger;
        DeleteDropHintLabel.TextColor = isBinHot ? onFill : danger;
    }

    /// <summary>
    /// The note was let go over the bin. The control hears nothing about this drop - a release that
    /// misses every column is one it quietly takes back - so the bin claims the drop and the note is
    /// deleted rather than moved.
    /// </summary>
    private void OnTrashDrop(object? sender, DropEventArgs e)
    {
        e.Handled = true;

        if (draggedCardId is not { } cardId)
        {
            HideBin();
            return;
        }

        HideBin();

        // The drop arrives in the middle of the control's own drag pipeline, which is still holding
        // the note and is about to put it back where it came from. Deleting it here would take the
        // note out of the collection while that pipeline is working on it, so the deletion waits
        // until the pipeline has let go.
        Dispatcher.Dispatch(() => _ = DeleteCardsAsync([cardId]));
    }

    /// <summary>
    /// Puts the header back after a drag the app was never told the end of - a drag the platform
    /// called off, or one that ended while the app was in the background. Without this the bin would
    /// be left where the header belongs.
    /// </summary>
    private void OnTrashTapped(object? sender, TappedEventArgs e) => HideBin();

    /// <summary>
    /// Deletes the given notes on one question. Returns whether they are gone: a question the user
    /// turned down leaves the board as it was.
    /// </summary>
    private async Task<bool> DeleteCardsAsync(IReadOnlyList<Guid> cardIds)
    {
        if (viewModel is null || cardIds.Count == 0)
        {
            return false;
        }

        await viewModel.DeleteCardsCommand.ExecuteAsync(cardIds);

        // The command only marks the notes as deleted; taking them out of their columns is the
        // board's job, so a board that is on screen rebuilds itself once instead of once per note.
        var removed = RemoveDeletedCards();
        if (removed)
        {
            RebuildKanbanCards();
        }

        return removed;
    }

    /// <summary>
    /// Puts the board into picking mode, or takes it back out. Picking has a mode of its own because
    /// a note is a drag handle and a tap target at once: while the notes can be dragged, the tap that
    /// should pick one starts a drag instead. Inside the mode dragging is off, so the tap picks.
    /// </summary>
    private void SetSelecting(bool value)
    {
        if (!value)
        {
            pickedCards.Clear();
        }

        isSelecting = value;

        foreach (var column in BoardKanban.Columns)
        {
            column.AllowDrag = CanDragCards;
        }

        // The filter panels belong to the header the picking row covers, so they are put away with
        // it rather than left open underneath.
        FilterTagScroll.IsVisible = false;
        SearchEntryHost.IsVisible = false;

        RebuildKanbanCards();
        UpdateSelectionBar();
    }

    private void OnSelectCardsClicked(object? sender, EventArgs e) => SetSelecting(true);

    private void OnDoneSelectingClicked(object? sender, EventArgs e) => SetSelecting(false);

    /// <summary>
    /// Says how many notes are picked, and leaves the actions out until at least one is: a button
    /// that would act on nothing says nothing.
    /// </summary>
    private void UpdateSelectionBar()
    {
        SelectionBar.IsVisible = isSelecting;
        SelectionCountLabel.Text = string.Format(Strings.SelectionCount, pickedCards.Count);

        var hasPicked = pickedCards.Count > 0;
        MoveSelectedButton.IsVisible = hasPicked;
        DeleteSelectedButton.IsVisible = hasPicked;
    }

    /// <summary>
    /// Moves every picked note into one column. Which column is a question rather than a drag because
    /// the control drags one note at a time, and a drag of one of the picked notes would be a move of
    /// that note alone.
    /// </summary>
    private async void OnMoveSelectedClicked(object? sender, EventArgs e)
    {
        if (viewModel is null || pickedCards.Count == 0)
        {
            return;
        }

        var columns = viewModel.Columns.ToList();
        if (columns.Count == 0)
        {
            return;
        }

        var choice = await dialogService.DisplayActionSheetAsync(
            Strings.MoveCardsTo,
            Strings.Cancel,
            [.. columns.Select(column => column.Title)]);

        if (choice < 0 || choice >= columns.Count)
        {
            return;
        }

        BeginCardRebuildSuppression();
        try
        {
            await viewModel.MoveCardsCommand.ExecuteAsync(
                new CardSelectionMoveRequest([.. pickedCards], columns[choice].Id));
        }
        finally
        {
            EndCardRebuildSuppression();
        }

        RebuildKanbanCards();
    }

    private async void OnDeleteSelectedClicked(object? sender, EventArgs e)
    {
        if (viewModel is null || pickedCards.Count == 0)
        {
            return;
        }

        if (await DeleteCardsAsync([.. pickedCards]))
        {
            // The picking was for these notes, so it is over with them.
            SetSelecting(false);
        }
    }

    private async void OnKanbanDragEnd(object? sender, KanbanDragEndEventArgs e)
    {
        // Whatever the drag did, the note is no longer in the air - so the bin goes away with it.
        // The control fires no end event at all for a drop that missed its columns, which is why the
        // bin also puts the header back when it is tapped.
        HideBin();

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
        BeginCardRebuildSuppression();
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
            EndCardRebuildSuppression();
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
        BeginCardRebuildSuppression();
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
            EndCardRebuildSuppression();
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

        // While notes are being picked, a tap is what picks one: opening the editor instead would
        // take the board away under the very thing the picking is for.
        if (isSelecting)
        {
            if (!pickedCards.Remove(card.CardId))
            {
                pickedCards.Add(card.CardId);
            }

            card.IsPicked = pickedCards.Contains(card.CardId);
            UpdateSelectionBar();
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

        /// <summary>
        /// Side of the paper the typed text is written on, in the units the note is laid out in. The
        /// same side the ink is drawn over - the paper less the inset the note keeps around both -
        /// which is what makes the writing the same share of the note here as in the editor.
        /// </summary>
        private double TextSide => Math.Max(1, NoteWidth - (2 * NoteInset));

        /// <summary>Whether the note carries typed text, which is what its two lines stand for.</summary>
        public bool HasText => Card.HasText;

        /// <summary>Title line of the note, empty when only a body was written.</summary>
        public string TextTitle => Card.TextTitle;

        /// <summary>Whether there is a title line to draw at all.</summary>
        public bool HasTitle => Card.TextTitle.Length > 0;

        /// <summary>The body, over as many lines as the note shows of it.</summary>
        public string TextBody => Card.TextBody;

        /// <summary>Size the title is drawn at on the board, from the size kept in the card.</summary>
        public double TextTitleSize => CardText.TitleFontSizeFor(Card.TextSize, TextSide);

        /// <summary>Size the body is drawn at on the board, from the size kept in the card.</summary>
        public double TextBodySize => CardText.FontSizeFor(Card.TextSize, TextSide);

        /// <summary>
        /// Colour the writing is drawn in. The same colour the editor writes it in, so a note is not
        /// read in one colour on the board and written in another.
        /// </summary>
        public Color TextColor => Color.FromArgb(Card.TextColorHex);

        /// <summary>The title is always in its own weight, which is what sets it apart from the body.</summary>
        public FontAttributes TextTitleAttributes =>
            Card.TextItalic ? FontAttributes.Bold | FontAttributes.Italic : FontAttributes.Bold;

        /// <summary>Weight the body is drawn in, as the two switches in the editor left it.</summary>
        public FontAttributes TextBodyAttributes =>
            (Card.TextBold ? FontAttributes.Bold : FontAttributes.None)
            | (Card.TextItalic ? FontAttributes.Italic : FontAttributes.None);

        /// <summary>
        /// How many lines of the body a note has room for. The title takes its own lines off the top
        /// first - it may wrap twice - and what is left is divided by the height of one line, so a
        /// long note is cut off at its lower edge instead of running over the paper.
        /// </summary>
        public int TextBodyMaxLines
        {
            get
            {
                var titleRoom = HasTitle ? TextTitleSize * LineHeight * TitleLines : 0;
                var room = (NoteHeight - (2 * NoteInset)) - titleRoom;
                return Math.Max(1, (int)(room / (TextBodySize * LineHeight)));
            }
        }

        /// <summary>
        /// Puts the card's writing on the note again. The note is read off the card rather than
        /// remembered, so it is the note that is told - the editor writes into the card, and coming
        /// back from it is when this runs, before the board is drawn again.
        /// </summary>
        public void RefreshText()
        {
            Raise(nameof(HasText));
            Raise(nameof(TextTitle));
            Raise(nameof(HasTitle));
            Raise(nameof(TextBody));
            Raise(nameof(TextTitleSize));
            Raise(nameof(TextBodySize));
            Raise(nameof(TextColor));
            Raise(nameof(TextTitleAttributes));
            Raise(nameof(TextBodyAttributes));
            Raise(nameof(TextBodyMaxLines));
        }

        /// <summary>
        /// Whether the board is picking notes, which is when every note shows its pick box. Kept on
        /// the note rather than on the card: the note is what the pick box is drawn on.
        /// </summary>
        public bool ShowPicker
        {
            get => showPicker;
            set
            {
                if (showPicker == value)
                {
                    return;
                }

                showPicker = value;
                Raise(nameof(ShowPicker));
            }
        }

        /// <summary>Whether this note is one of the picked ones.</summary>
        public bool IsPicked
        {
            get => isPicked;
            set
            {
                if (isPicked == value)
                {
                    return;
                }

                isPicked = value;
                Raise(nameof(IsPicked));
                Raise(nameof(IsNotPicked));
            }
        }

        /// <summary>The pick box and the tick read as one and the same mark, so only one is drawn.</summary>
        public bool IsNotPicked => !isPicked;

        private void Raise(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool showPicker;
        private bool isPicked;
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
