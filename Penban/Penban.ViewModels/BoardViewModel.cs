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
    /// </summary>
    public async Task LoadSummaryAsync()
    {
        // The board's own note leads the stack of the board card, so it takes one of the few places
        // the overview fans out and the written cards fill the rest.
        var hasNote = HasNote;
        var cardLimit = hasNote ? PreviewNoteLimit - 1 : PreviewNoteLimit;

        var count = 0;
        var lastEdited = LastEditedUtc;
        var previews = new List<Card>();

        ColumnSummary.Clear();
        foreach (var column in Columns)
        {
            var cards = await cardService.GetCardsAsync(column.Id);
            count += cards.Count;
            ColumnSummary.Add(new BoardColumnSummary(column.Title, cards.Count, NoteColorIndex));

            foreach (var card in cards)
            {
                lastEdited = card.UpdatedAtUtc > lastEdited ? card.UpdatedAtUtc : lastEdited;

                // Only notes that were actually written on say something about the board.
                if (card.Strokes.Count > 0 && previews.Count < cardLimit)
                {
                    previews.Add(card);
                }
            }
        }

        LastEditedUtc = lastEdited;

        if (previews.Count == 0 && !hasNote)
        {
            // A board nobody has written on still gets a note to look at: blank, but in the paper
            // colour and tilt of the board itself, since both are derived from the id.
            previews.Add(new Card { Id = Id });
        }

        PreviewNotes.Clear();

        var total = previews.Count + (hasNote ? 1 : 0);
        for (var index = 0; index < previews.Count; index++)
        {
            PreviewNotes.Add(new BoardNotePreview(previews[index], index, total));
        }

        if (hasNote)
        {
            // Added last, because the last place of the fan is the one drawn on top: the note that
            // stands for the whole board belongs in front of the cards underneath it.
            PreviewNotes.Add(new BoardNotePreview(Id, board.NoteStrokes, board.NoteColorIndex, total - 1, total));
        }

        CardCount = count;

        // Everything above derives from the stored data, not from CardCount, so it has to be
        // announced separately.
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasColumns));
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
