using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using Penban.ViewModels;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// One board's details - its name and, behind the extended mode, its labels, its time frame and the
/// project it belongs to. The same page creates a board and edits one; which of the two it is
/// follows from the parameters the view model was handed.
/// </summary>
public partial class BoardDetailsPage : ContentPage, Microsoft.Maui.Controls.IQueryAttributable
{
    private readonly BoardDetailsViewModel viewModel;
    private readonly INavigationService navigation;

    public BoardDetailsPage(BoardDetailsViewModel viewModel, INavigationService navigation)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.navigation = navigation;
        BindingContext = viewModel;
    }

    /// <summary>
    /// The route's parameters, handed on to the view model. The shell only knows the page, and the
    /// view model is the half of it that has to stay free of the framework, so it cannot implement
    /// the shell's own interface itself.
    /// </summary>
    void Microsoft.Maui.Controls.IQueryAttributable.ApplyQueryAttributes(IDictionary<string, object> query)
    {
        viewModel.ApplyQueryAttributes(query);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await navigation.NavigateBack();
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        await viewModel.SaveAsync();
        await navigation.NavigateBack();
    }
}
