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
        // dead switch anywhere else.
        PenTailEraserRow.IsVisible = OperatingSystem.IsWindows();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Picks up changes made elsewhere, e.g. the inline finger-drawing toggle in the editor.
        viewModel.Refresh();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnFeedbackClicked(object? sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new FeedbackPage(feedbackViewModel));
    }
}
