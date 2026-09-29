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
    private readonly Board board;
    private readonly IBoardService boardService;
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;

    public BoardViewModel(Board board, IBoardService boardService, ICardService cardService, IDialogService dialogService)
    {
        this.board = board;
        Id = board.Id;
        title = board.Title;
        LastEditedUtc = board.UpdatedAtUtc;
        this.boardService = boardService;
        this.cardService = cardService;
        this.dialogService = dialogService;

        foreach (var column in board.Columns.OrderBy(c => c.SortOrder))
        {
            Columns.Add(new ColumnViewModel(column, cardService, dialogService));
        }
    }

    public Guid Id { get; }

    /// <summary>Paper colour of this board's note; stable for the lifetime of the board.</summary>
    public int NoteColorIndex => NoteStyle.PaperIndexFor(Id);

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

        LastEditedUtc = lastEdited;

        if (previews.Count == 0 && !hasNote)
        {
            // A board nobody has written on still gets a note to look at: blank, but in the paper
            // colour and tilt of the board itself, since both are derived from the id.
            previews.Add(new Card { Id = Id });
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
    /// </summary>
    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (index < target.Count)
            {
                target[index] = items[index];
            }
            else
            {
                target.Add(items[index]);
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
}
