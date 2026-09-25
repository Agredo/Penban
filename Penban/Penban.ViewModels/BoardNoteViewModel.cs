// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// The board's own note, written from the board header. It is not a Kanban card - it belongs to the
/// board itself and needs no column - but it holds the same ink and is edited by the same page, so
/// the board can say something about itself the way a single card says something about its column.
/// A board without a note keeps the paper colour and the tilt derived from its id, which is what
/// makes its board card look the way it did before the note existed.
/// </summary>
public partial class BoardNoteViewModel : ObservableObject, INoteEditorTarget
{
    private readonly Board board;
    private readonly IBoardService boardService;
    private readonly IDialogService dialogService;

    public BoardNoteViewModel(Board board, IBoardService boardService, IDialogService dialogService)
    {
        this.board = board;
        this.boardService = boardService;
        this.dialogService = dialogService;
        InkCanvas = new InkCanvasViewModel();
        InkCanvas.LoadStrokes(board.NoteStrokes);
    }

    public Guid Id => board.Id;

    /// <summary>
    /// Paper colour of the board's note. Without a choice of the user it is derived from the id, the
    /// same rule a card follows; picking one in the editor stores it with the board.
    /// </summary>
    public int NoteColorIndex
    {
        get => board.NoteColorIndex ?? NoteStyle.PaperIndexFor(board.Id);
        set
        {
            if (board.NoteColorIndex == value)
            {
                return;
            }

            board.NoteColorIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Set once the note has been cleared. The note belongs to the board rather than to the editor,
    /// so there is nothing left to write afterwards - the board simply has no note any more.
    /// </summary>
    [ObservableProperty]
    private bool isDeleted;

    public InkCanvasViewModel InkCanvas { get; }

    /// <summary>
    /// Writes the note to the board. The board is updated first as well as through the service,
    /// because the service reads its own copy from the database: the instance the board page and the
    /// overview row hold is this one, and it has to follow the note without a reload.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var strokes = InkCanvas.Strokes.ToList();

        board.NoteStrokes = strokes;
        board.NoteColorIndex = strokes.Count == 0 ? null : board.NoteColorIndex;

        await boardService.SaveBoardNoteAsync(board.Id, strokes, board.NoteColorIndex);
    }

    /// <summary>
    /// Drops the note from the board. Erasing the last stroke and pressing delete come down to the
    /// same thing - a board without a note - so both end up here and both put the board card back to
    /// what it looked like before the note was written.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await dialogService.DisplayConfirmationAsync(Strings.Delete, Strings.DeleteBoardNoteConfirmation, Strings.Delete, Strings.Cancel);
        if (!confirmed)
        {
            return;
        }

        board.NoteStrokes = [];
        board.NoteColorIndex = null;
        InkCanvas.Clear();
        await boardService.SaveBoardNoteAsync(board.Id, [], null);

        IsDeleted = true;
    }
}
