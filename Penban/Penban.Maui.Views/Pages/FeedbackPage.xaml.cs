using Penban.ViewModels;
using AgredoApplication.MVVM.Services.Abstractions.Navigation;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// The feedback form. Everything it does lives in <see cref="FeedbackViewModel"/>; the page only
/// forwards the submit and closes itself once the feedback went through.
/// </summary>
public partial class FeedbackPage : ContentPage
{
    private readonly FeedbackViewModel viewModel;
    private readonly INavigationService navigation;

    public FeedbackPage(FeedbackViewModel viewModel, INavigationService navigation)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.navigation = navigation;
        BindingContext = viewModel;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await PopAsync();
    }

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        await viewModel.SubmitCommand.ExecuteAsync(null);
        if (viewModel.IsSubmitted)
        {
            await PopAsync();
        }
    }

    /// <summary>
    /// Whether this page was put up over another one rather than onto the shell's stack, which is what
    /// a settings page opened over the ink editor does with it. A modal page has only the modal stack
    /// behind it - the window's own stack refuses a pop - so closing this page has to go through the
    /// modal one while it is up.
    /// </summary>
    private bool IsModal => Navigation.ModalStack.Contains(this);

    private Task PopAsync() => IsModal ? Navigation.PopModalAsync() : navigation.NavigateBack();

    private async void OnBugBearLinkTapped(object? sender, TappedEventArgs e)
    {
        await Launcher.OpenAsync("https://www.bug-bear.com");
    }
}
