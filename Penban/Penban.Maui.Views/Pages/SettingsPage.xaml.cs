using Penban.ViewModels;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Central place for the app's switches. Each switch writes through to the preferences store the
/// moment it is flipped, so leaving the page needs no confirmation.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel viewModel;
    private readonly FeedbackViewModel feedbackViewModel;

    public SettingsPage(SettingsViewModel viewModel, FeedbackViewModel feedbackViewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.feedbackViewModel = feedbackViewModel;
        BindingContext = viewModel;

        // Only Windows reports which end of the pen is against the surface, so the row would be a
        // dead switch anywhere else. The same goes for the button on the pen itself.
        PenTailEraserRow.IsVisible = OperatingSystem.IsWindows();
        PenButtonLassoRow.IsVisible = OperatingSystem.IsWindows();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Picks up changes made elsewhere, e.g. the inline finger-drawing toggle in the editor.
        viewModel.Refresh();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (IsModal)
        {
            await Navigation.PopModalAsync();
            return;
        }

        await Navigation.PopAsync();
    }

    private async void OnFeedbackClicked(object? sender, TappedEventArgs e)
    {
        var page = new FeedbackPage(feedbackViewModel);

        if (IsModal)
        {
            await Navigation.PushModalAsync(page);
            return;
        }

        await Navigation.PushAsync(page);
    }

    /// <summary>
    /// Whether this page was put up over another one rather than onto the shell's stack, which is what
    /// the ink editor does with it. A modal page has only the modal stack behind it - the window's own
    /// stack refuses a push and a pop - so both ways off this page have to go through the modal one
    /// while it is up.
    /// </summary>
    private bool IsModal => Navigation.ModalStack.Contains(this);
}
