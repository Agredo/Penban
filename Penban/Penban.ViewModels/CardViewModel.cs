// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Models;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>
/// Represents a single card. A card carries handwritten ink strokes, through <see cref="InkCanvas"/>,
/// or typed text, or both - which of the two is being written is <see cref="Mode"/>, and switching
/// between the two keeps what the other one holds.
/// </summary>
public partial class CardViewModel : ObservableObject, INoteEditorTarget, ITextNoteEditorTarget
{
    private readonly ICardService cardService;
    private readonly IDialogService dialogService;
    private readonly Card card;

    public CardViewModel(Card card, ICardService cardService, IDialogService dialogService)
    {
        this.card = card;
        this.cardService = cardService;
        this.dialogService = dialogService;
        InkCanvas = new InkCanvasViewModel();
        InkCanvas.LoadStrokes(card.Strokes);
        sortOrder = card.SortOrder;
    }

    public Guid Id => card.Id;

    public Guid ColumnId => card.ColumnId;

    /// <summary>
    /// Paper colour of this card's note. Without a choice of the user the colour is derived from the
    /// id, so a fresh note still looks hand-picked instead of uniform; picking one in the editor
    /// stores it with the card.
    /// </summary>
    public int NoteColorIndex
    {
        get => card.NoteColorIndex ?? NoteStyle.PaperIndexFor(card.Id);
        set
        {
            if (card.NoteColorIndex == value)
            {
                return;
            }

            card.NoteColorIndex = value;
            OnPropertyChanged();
        }
    }

    public bool SupportsTags => true;

    public IReadOnlyList<string> Tags => card.Tags;

    /// <summary>The tags as the one line drawn on the note.</summary>
    public string TagsText => CardTags.Join(card.Tags);

    public bool ToggleTag(string tag)
    {
        card.Tags ??= [];
        if (!CardTags.Toggle(card.Tags, tag))
        {
            return false;
        }

        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(TagsText));
        return true;
    }

    /// <summary>Tilt of this card's note in degrees.</summary>
    public double NoteTilt => NoteStyle.TiltFor(card.Id);

    /// <summary>
    /// Which of the two the note is being written in. It is kept with the card and not with the page,
    /// so a note left in the text field is opened in it again.
    /// </summary>
    public CardContentMode Mode
    {
        get => card.Mode;
        set
        {
            if (card.Mode == value)
            {
                return;
            }

            card.Mode = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Title line of the note; empty when the user wrote a body only.</summary>
    public string TextTitle
    {
        get => card.TextTitle ?? string.Empty;
        set
        {
            var text = value ?? string.Empty;
            if (string.Equals(card.TextTitle, text, StringComparison.Ordinal))
            {
                return;
            }

            card.TextTitle = text;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasText));
        }
    }

    /// <summary>Everything under the title, over as many lines as the user needs.</summary>
    public string TextBody
    {
        get => card.TextBody ?? string.Empty;
        set
        {
            var text = value ?? string.Empty;
            if (string.Equals(card.TextBody, text, StringComparison.Ordinal))
            {
                return;
            }

            card.TextBody = text;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasText));
        }
    }

    /// <summary>Whether the note carries typed text, which is what the board shows as its preview.</summary>
    public bool HasText => CardText.HasText(card);

    /// <summary>Size of the note's text in document units, see <see cref="CardText"/>.</summary>
    public float TextSize
    {
        get => CardText.SizeOf(card);
        set
        {
            var size = CardText.Clamp(value);
            if (card.TextSize == size)
            {
                return;
            }

            card.TextSize = size;
            OnPropertyChanged();
        }
    }

    /// <summary>Colour the note's text is written in, as <c>#RRGGBB</c>.</summary>
    public string TextColorHex
    {
        get => CardText.ColorHexOf(card);
        set
        {
            if (string.Equals(card.TextColorHex, value, StringComparison.Ordinal))
            {
                return;
            }

            card.TextColorHex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the note is written in bold.</summary>
    public bool TextBold
    {
        get => card.TextBold;
        set
        {
            if (card.TextBold == value)
            {
                return;
            }

            card.TextBold = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the note is written in italics.</summary>
    public bool TextItalic
    {
        get => card.TextItalic;
        set
        {
            if (card.TextItalic == value)
            {
                return;
            }

            card.TextItalic = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private int sortOrder;

    /// <summary>Set to true once the user has confirmed and completed deletion of this card.</summary>
    [ObservableProperty]
    private bool isDeleted;

    public void UpdatePlacement(Guid columnId, int newSortOrder)
    {
        card.ColumnId = columnId;
        SortOrder = newSortOrder;
    }

    public InkCanvasViewModel InkCanvas { get; }

    /// <summary>Persists the card's current ink strokes. Called when the card/canvas is closed - there is no explicit "Save" button.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        card.Strokes = InkCanvas.Strokes.ToList();
        card.SortOrder = SortOrder;
        await cardService.SaveCardAsync(card);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await dialogService.DisplayConfirmationAsync(Strings.Delete, Strings.DeleteCardConfirmation, Strings.Delete, Strings.Cancel);
        if (!confirmed)
        {
            return;
        }

        await cardService.DeleteCardAsync(card.Id);
        IsDeleted = true;
    }
}

