using System.Windows.Input;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// One board as a list draws it, in the two layouts a page can ask for: the fan of notes beside what is
/// written on the board where there is room, one under the other where there is not. The same card is
/// drawn wherever boards are listed, so a board looks the same on every page and there is only one
/// place to change it.
/// </summary>
public partial class BoardCard : ContentView
{
    /// <summary>
    /// Page width from which a board card keeps its fan beside its text. Every page that lists boards
    /// switches at the same width, so the cards of two pages do not differ on the same screen.
    /// </summary>
    public const double WidePageWidth = 700;

    public static readonly BindableProperty NarrowProperty = BindableProperty.Create(
        nameof(Narrow),
        typeof(bool),
        typeof(BoardCard),
        false,
        propertyChanged: OnNarrowChanged);

    /// <summary>The other half of <see cref="Narrow"/>, so a layout can be bound to one flag each.</summary>
    public static readonly BindableProperty WideProperty = BindableProperty.Create(
        nameof(Wide),
        typeof(bool),
        typeof(BoardCard),
        true);

    /// <summary>
    /// Whether the buttons that act on a board are shown. A page that only shows a board as a way in
    /// leaves them out, which is why they are a flag rather than three commands that have to be filled
    /// in on every page.
    /// </summary>
    public static readonly BindableProperty ShowActionsProperty = BindableProperty.Create(
        nameof(ShowActions),
        typeof(bool),
        typeof(BoardCard),
        true);

    public static readonly BindableProperty ShareCommandProperty = BindableProperty.Create(
        nameof(ShareCommand),
        typeof(ICommand),
        typeof(BoardCard));

    public static readonly BindableProperty EditCommandProperty = BindableProperty.Create(
        nameof(EditCommand),
        typeof(ICommand),
        typeof(BoardCard));

    public static readonly BindableProperty DeleteCommandProperty = BindableProperty.Create(
        nameof(DeleteCommand),
        typeof(ICommand),
        typeof(BoardCard));

    public BoardCard()
    {
        InitializeComponent();
    }

    /// <summary>Whether the card is stacked rather than laid out side by side.</summary>
    public bool Narrow
    {
        get => (bool)GetValue(NarrowProperty);
        set => SetValue(NarrowProperty, value);
    }

    /// <summary>Whether the card is laid out side by side.</summary>
    public bool Wide
    {
        get => (bool)GetValue(WideProperty);
        set => SetValue(WideProperty, value);
    }

    /// <summary>Whether the buttons that act on a board are shown.</summary>
    public bool ShowActions
    {
        get => (bool)GetValue(ShowActionsProperty);
        set => SetValue(ShowActionsProperty, value);
    }

    /// <summary>Sends the board away as a file. The board is the command's parameter.</summary>
    public ICommand? ShareCommand
    {
        get => (ICommand?)GetValue(ShareCommandProperty);
        set => SetValue(ShareCommandProperty, value);
    }

    /// <summary>Opens the details page on the board. The board is the command's parameter.</summary>
    public ICommand? EditCommand
    {
        get => (ICommand?)GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    /// <summary>Throws the board away. The board is the command's parameter.</summary>
    public ICommand? DeleteCommand
    {
        get => (ICommand?)GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    private static void OnNarrowChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var card = (BoardCard)bindable;
        card.Wide = !(bool)newValue;
    }
}
