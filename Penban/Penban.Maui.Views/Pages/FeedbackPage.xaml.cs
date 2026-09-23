using Penban.ViewModels;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// The feedback form. Everything it does lives in <see cref="FeedbackViewModel"/>; the page only
/// forwards the submit and closes itself once the feedback went through.
/// </summary>
public partial class FeedbackPage : ContentPage
{
    private readonly FeedbackViewModel viewModel;

    public FeedbackPage(FeedbackViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        await viewModel.SubmitCommand.ExecuteAsync(null);
        if (viewModel.IsSubmitted)
        {
            await Navigation.PopAsync();
        }
    }

    private async void OnBugBearLinkTapped(object? sender, TappedEventArgs e)
    {
        await Launcher.OpenAsync("https://www.bug-bear.com");
    }
}
