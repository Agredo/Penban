using Penban.ViewModels;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Searches the text that was read from the notes. Opening a result is the overview's job - it is
/// the page that knows how to show a board - so it is handed in as the one thing this page cannot
/// do itself.
/// </summary>
public partial class SearchPage : ContentPage
{
    private readonly Func<Guid, Task> openBoard;

    public SearchPage(SearchViewModel viewModel, Func<Guid, Task> openBoard)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(openBoard);

        InitializeComponent();
        this.openBoard = openBoard;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Coming back to the search page is how the user checks whether the reading has got further,
        // so the count is taken again here rather than only when the page is built.
        if (BindingContext is SearchViewModel viewModel)
        {
            viewModel.RefreshPendingCount();
        }

        // The keyboard comes up with the page: searching is the only thing to do here, and a field
        // that has to be tapped first costs a tap for nothing. Dispatched because a field cannot take
        // the focus before it has been laid out.
        Dispatcher.Dispatch(() => SearchInput.Focus());
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void OnSearchPressed(object? sender, EventArgs e)
    {
        if (BindingContext is SearchViewModel viewModel)
        {
            viewModel.SearchCommand.Execute(null);
        }
    }

    private async void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element { BindingContext: SearchResultViewModel result })
        {
            return;
        }

        // The search is a way of looking something up, not a place to stay: the board it found opens
        // behind it, so the way back leads to the overview rather than to the search again.
        await Navigation.PopAsync();
        await openBoard(result.BoardId);
    }
}
