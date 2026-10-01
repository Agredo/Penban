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
/// makes its board card look the way it did before the note existed - unless the user picked a
/// colour for the note, which the board holds on to until the note itself is dropped.
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
    /// <para>
    /// Only the ink is written here. The paper colour is the user's choice and stays with the board
    /// even while the note is empty - this save runs on every erase as well, so clearing it here
    /// would repaint the board card behind the user's back.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var strokes = InkCanvas.Strokes.ToList();

        board.NoteStrokes = strokes;

        await boardService.SaveBoardNoteAsync(board.Id, strokes, board.NoteColorIndex);
    }

    /// <summary>
    /// Drops the note from the board. Unlike erasing the last stroke, which only takes the ink away,
    /// this takes the note together with the paper colour the user gave it, so the board card goes
    /// back to what it looked like before the note was written.
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
