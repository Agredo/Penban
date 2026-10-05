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
    private Board board;
    private readonly IBoardService boardService;
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;

    public BoardViewModel(Board board, IBoardService boardService, ICardService cardService, IDialogService dialogService)
    {
        this.board = board;
        Id = board.Id;
        title = board.Title;
        LastEditedUtc = board.UpdatedAtUtc;
        SortOrder = board.SortOrder;
        this.boardService = boardService;
        this.cardService = cardService;
        this.dialogService = dialogService;

        foreach (var column in board.Columns.OrderBy(c => c.SortOrder))
        {
            Columns.Add(new ColumnViewModel(column, cardService, dialogService));
        }
    }

    public Guid Id { get; }

    /// <summary>
    /// Where this board sits on the overview, read from the board and written only by
    /// <c>BoardsViewModel.MoveBoardAsync</c> after a drag. The overview orders its rows by it, so the
    /// board that was dragged to another place is still there when the list is read again.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Paper colour of this board's note: the colour the user picked, or - as long as there is none -
    /// the one derived from the id, which stays the same for the lifetime of the board. The lanes of
    /// the board card and the notes of its stack both read this, so a picked colour cannot end up on
    /// one of them and not on the other.
    /// </summary>
    public int NoteColorIndex => board.NoteColorIndex ?? NoteStyle.PaperIndexFor(Id);

    /// <summary>Tilt of this board's note in degrees.</summary>
    public double NoteTilt => NoteStyle.TiltFor(Id);

    /// <summary>
    /// Whether the board carries a note of its own. This is the board's single note - the one shown
    /// on the board card and on the home screen - not one of the cards on it.
    /// </summary>
    public bool HasNote => board.NoteStrokes.Count > 0;

    /// <summary>
    /// A fresh editing session for the board's own note. It works on the very board this view model
    /// was built from, so the note it writes is the one the overview row reads when it comes back.
    /// </summary>
    public BoardNoteViewModel CreateNoteEditor() => new(board, boardService, dialogService);

    [ObservableProperty]
    private string title;

    /// <summary>Number of notes the overview fans out; more than a handful stops reading as a stack.</summary>
    private const int PreviewNoteLimit = 3;

    /// <summary>
    /// How many places' worth of cards the overview may read through to fill the fan, see
    /// <see cref="PickPreviewsAsync"/>. Enough to look past the blank notes a board tends to start
    /// with, few enough that a board of nothing but blank notes still lists quickly.
    /// </summary>
    private const int PreviewProbeFactor = 4;

    /// <summary>Total number of cards across all columns; loaded by <see cref="LoadSummaryAsync"/>.</summary>
    [ObservableProperty]
    private int cardCount;

    /// <summary>
    /// Whether the board is still on its way to the screen: its lanes are being read and its notes
    /// built. Turned on by the board page for the visit that builds the board, and off again once the
    /// notes stand.
    /// <para>
    /// A board is only ever built once: a board that is already up is not read and built again when
    /// the page comes back from the editor, so this is only ever on for the way into a board - which
    /// is the one moment a board with a lot on it takes long enough for the wait to look like a tap
    /// that was missed.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool isLoading;

    /// <summary>Most recent write to this board or any of its cards.</summary>
    public DateTimeOffset LastEditedUtc { get; private set; }

    /// <summary>
    /// Caption under the board title: when it was last touched and how much sits on it. A board
    /// nobody has written on yet only says that it is empty - a timestamp would be noise there.
    /// </summary>
    public string SummaryText => CardCount == 0
        ? Strings.NoCardsYet
        : string.Format(Strings.BoardSummaryFormat, RelativeTime.Describe(LastEditedUtc, DateTimeOffset.UtcNow), CardCountText);

    /// <summary>
    /// The same caption for the home screen widget, with a fixed date and time instead of "10 min ago".
    /// <para>
    /// The widget shows a picture of this list, and that picture is only drawn again when one of these
    /// lines changes. A wording that moves with the clock would make every board look different every
    /// minute and have all of them redrawn for it - during the visit to the overview, which is the
    /// moment someone is waiting for the list. A date stands still until the board is really written to.
    /// </para>
    /// </summary>
    public string WidgetSummaryText => CardCount == 0
        ? Strings.NoCardsYet
        : string.Format(Strings.BoardSummaryFormat, LastEditedUtc.ToLocalTime().ToString(Strings.TimeStampFormat), CardCountText);

    private string CardCountText => CardCount == 1
        ? Strings.OneCard
        : string.Format(Strings.CardsCountFormat, CardCount);

    /// <summary>Fills-in of one lane per column, drawn as the load bar of the overview.</summary>
    public ObservableCollection<BoardColumnSummary> ColumnSummary { get; } = new();

    /// <summary>Whether there are lanes to draw in the load bar.</summary>
    public bool HasColumns => ColumnSummary.Count > 0;

    /// <summary>Notes fanned out as this board's thumbnail; up to three cards, in board order.</summary>
    public ObservableCollection<BoardNotePreview> PreviewNotes { get; } = new();

    public ObservableCollection<ColumnViewModel> Columns { get; } = new();

    /// <summary>
    /// Brings this row up to date with the board as it was just read from the database, without
    /// giving up the view model itself. The overview holds on to its rows so that the list keeps the
    /// place the reader scrolled to (see <c>BoardsViewModel.LoadBoardsAsync</c>), so what a row shows
    /// has to be rebuilt here instead of by building a new row: a board that was opened and closed
    /// again may have gained or lost lanes, cards and a note of its own since the last visit, and the
    /// board page gets its columns from this very view model.
    /// </summary>
    public async Task RefreshAsync(Board board)
    {
        this.board = board;
        Title = board.Title;
        LastEditedUtc = board.UpdatedAtUtc;
        SortOrder = board.SortOrder;

        // The paper colour, the tilt and whether the board carries a note of its own have no field of
        // their own to report from - they are read off the board this row holds - and the date beside
        // them is written without saying so. The row stays on screen while the boards are re-read
        // (see BoardsViewModel.MergeRows), so all four are announced here.
        OnPropertyChanged(nameof(NoteColorIndex));
        OnPropertyChanged(nameof(NoteTilt));
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(LastEditedUtc));

        var columns = board.Columns
            .OrderBy(c => c.SortOrder)
            .Select(ToColumnViewModel)
            .ToList();
        Replace(Columns, columns);

        await LoadSummaryAsync();
    }

    /// <summary>
    /// The view model for one lane of the board as it was just read, reusing the instance that is
    /// already there for a lane that is still on the board.
    /// <para>
    /// A fresh instance would come with an empty card collection, and the board page - which is
    /// showing these very lanes - answers that by reading every note of the lane again. On a board
    /// that is open while the overview is refreshed (which is the case for the board that was just
    /// opened, see <c>BoardsPage.OpenBoardAsync</c>) that is a second, needless pass over all of its
    /// notes, one lane at a time, on the way to the board the reader asked for.
    /// </para>
    /// </summary>
    private ColumnViewModel ToColumnViewModel(BoardColumn column)
    {
        if (Columns.FirstOrDefault(existing => existing.Id == column.Id) is { } kept)
        {
            kept.Update(column);
            return kept;
        }

        return new ColumnViewModel(column, cardService, dialogService);
    }

    /// <summary>
    /// Loads the lightweight summary shown on the board overview (last edit, card count per lane,
    /// ink of the first few notes) without populating the column card collections used by the board
    /// page.
    /// <para>
    /// The counts and the date come out of an ink-free read of the board (<see cref="CardHeader"/>),
    /// which is what keeps this cheap on a board whose notes have been written on: only the handful
    /// of cards that are actually drawn are read whole, see <see cref="PickPreviewsAsync"/>.
    /// </para>
    /// </summary>
    public async Task LoadSummaryAsync()
    {
        // The board's own note leads the stack of the board card, so it takes one of the few places
        // the overview fans out and the written cards fill the rest.
        var hasNote = HasNote;
        var cardLimit = hasNote ? PreviewNoteLimit - 1 : PreviewNoteLimit;

        var count = 0;
        var lastEdited = LastEditedUtc;
        var candidates = new List<Guid>();
        var summaries = new List<BoardColumnSummary>();

        foreach (var column in Columns)
        {
            var headers = await cardService.GetCardHeadersAsync(column.Id);
            count += headers.Count;
            summaries.Add(new BoardColumnSummary(column.Title, headers.Count, NoteColorIndex));

            foreach (var header in headers)
            {
                lastEdited = header.UpdatedAtUtc > lastEdited ? header.UpdatedAtUtc : lastEdited;

                // Board order - columns in their order, cards in the order the board shows them. It
                // is the order the fan fills up in, and the order PickPreviewsAsync walks in.
                candidates.Add(header.Id);
            }
        }

        var previews = await PickPreviewsAsync(candidates, cardLimit);

        // The date starts at the board and moves to the note written last of all, so the row may end
        // up on another day than the one RefreshAsync put there.
        if (LastEditedUtc != lastEdited)
        {
            LastEditedUtc = lastEdited;
            OnPropertyChanged(nameof(LastEditedUtc));
        }

        if (previews.Count == 0 && !hasNote)
        {
            // A board nobody has written on still gets a note to look at: blank, but in the paper
            // colour and tilt of the board itself, since both are derived from the id. A colour the
            // user picked for the board's note counts as the board's own, so the empty note is drawn
            // in it rather than in a derived one the board no longer shows anywhere else.
            previews.Add(new Card { Id = Id, NoteColorIndex = board.NoteColorIndex });
        }

        var notes = new List<BoardNotePreview>();
        var total = previews.Count + (hasNote ? 1 : 0);
        for (var index = 0; index < previews.Count; index++)
        {
            notes.Add(new BoardNotePreview(previews[index], index, total));
        }

        if (hasNote)
        {
            // Added last, because the last place of the fan is the one drawn on top: the note that
            // stands for the whole board belongs in front of the cards underneath it.
            notes.Add(new BoardNotePreview(Id, board.NoteStrokes, board.NoteColorIndex, total - 1, total));
        }

        // Applied together and without clearing, see Replace.
        Replace(ColumnSummary, summaries);
        Replace(PreviewNotes, notes);

        CardCount = count;

        // Everything above derives from the stored data, not from CardCount, so it has to be
        // announced separately.
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasColumns));
    }

    /// <summary>
    /// Reads the board's cards in order and returns the first <paramref name="limit"/> that were
    /// written on - the notes the overview actually draws.
    /// <para>
    /// Whether a card carries ink can only be seen by reading the card, and the ink of a card is what
    /// makes reading it expensive. So they are read in runs of <paramref name="limit"/> at a time,
    /// stopping at the first run that fills the fan, and never more than
    /// <see cref="PreviewProbeFactor"/> runs into the board: blank notes are looked past, but not
    /// indefinitely - a board of nothing but blank notes would otherwise cost what it cost before.
    /// </para>
    /// </summary>
    private async Task<List<Card>> PickPreviewsAsync(IReadOnlyList<Guid> candidates, int limit)
    {
        var previews = new List<Card>();

        // How far into the board to look for written notes, at most.
        var searched = Math.Min(candidates.Count, limit * PreviewProbeFactor);

        for (var start = 0; start < searched && previews.Count < limit; start += limit)
        {
            var size = Math.Min(limit, searched - start);
            var ids = new List<Guid>(size);
            for (var offset = 0; offset < size; offset++)
            {
                ids.Add(candidates[start + offset]);
            }

            foreach (var card in await cardService.GetCardsByIdsAsync(ids))
            {
                // Only notes that were actually written on say something about the board.
                if (card.Strokes.Count > 0)
                {
                    previews.Add(card);
                    if (previews.Count == limit)
                    {
                        break;
                    }
                }
            }
        }

        return previews;
    }

    /// <summary>
    /// Puts <paramref name="items"/> into a collection that a layout is drawing, without ever
    /// clearing it.
    /// <para>
    /// Clearing raises a reset, and a reset makes BindableLayout take the children of the layout it
    /// feeds down all at once. On Windows the FlexLayout behind the load bar does not survive that:
    /// the app dies in the native children collection with no exception raised anywhere. The row is
    /// re-read on every visit to the overview, so the second visit would be enough to lose it.
    /// </para>
    /// <para>
    /// Replacing and removing one at a time raises an event per place instead, which the same
    /// controller handles without tearing the layout down.
    /// </para>
    /// <para>
    /// A place that already holds this very item is left alone. Writing it would raise a replace
    /// event for a change that did not happen, and the board page answers those by reading the
    /// lane's cards again - so re-reading a board that did not change would cost a full pass over
    /// its notes.
    /// </para>
    /// </summary>
    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (index >= target.Count)
            {
                target.Add(items[index]);
            }
            else if (!ReferenceEquals(target[index], items[index]))
            {
                target[index] = items[index];
            }
        }

        while (target.Count > items.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    [RelayCommand]
    private async Task AddColumnAsync()
    {
        var name = await dialogService.DisplayPromptAsync(Strings.AddColumn, string.Empty, Strings.Add, Strings.Cancel);
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

    /// <summary>
    /// Deletes the notes that are picked on the board, all of them on one question rather than one
    /// question per note. The cards are only marked as deleted here; taking them out of their columns
    /// is the board page's job, so a board that is on screen rebuilds itself once instead of once per
    /// note (see <c>BoardPage.RemoveDeletedCards</c>).
    /// </summary>
    [RelayCommand]
    private async Task DeleteCardsAsync(IReadOnlyList<Guid> cardIds)
    {
        if (cardIds is null || cardIds.Count == 0)
        {
            return;
        }

        var confirmed = await dialogService.DisplayConfirmationAsync(Strings.Delete, Strings.DeleteCardsConfirmation, Strings.Delete, Strings.Cancel);
        if (!confirmed)
        {
            return;
        }

        foreach (var cardId in cardIds)
        {
            await cardService.DeleteCardAsync(cardId);
        }

        foreach (var card in Columns.SelectMany(column => column.Cards).Where(card => cardIds.Contains(card.Id)))
        {
            card.IsDeleted = true;
        }
    }

    /// <summary>
    /// Moves the picked notes into another column, in the order they were given. Appending them one
    /// after the other is what lets the database do the renumbering: every move puts its note at the
    /// end of the target column, and the notes behind it keep the order they were picked in.
    /// </summary>
    [RelayCommand]
    private async Task MoveCardsAsync(CardSelectionMoveRequest request)
    {
        var target = Columns.FirstOrDefault(column => column.Id == request.TargetColumnId);
        if (target is null || request.CardIds.Count == 0)
        {
            return;
        }

        foreach (var cardId in request.CardIds)
        {
            var source = Columns.FirstOrDefault(column => column.Cards.Any(card => card.Id == cardId));
            if (source is null || source == target)
            {
                continue;
            }

            await cardService.MoveCardAsync(cardId, source.Id, target.Id, target.Cards.Count);

            var card = source.Cards.First(card => card.Id == cardId);
            source.Cards.Remove(card);
            card.UpdatePlacement(target.Id, target.Cards.Count);
            target.Cards.Add(card);
        }

        // The lane the notes were taken out of is left with a gap in its numbering.
        foreach (var column in Columns)
        {
            for (var index = 0; index < column.Cards.Count; index++)
            {
                column.Cards[index].UpdatePlacement(column.Id, index);
            }
        }
    }
}
