using Penban.ViewModels;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Central place for the app's switches. Each switch writes through to the preferences store the
/// moment it is flipped, so leaving the page needs no confirmation.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
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
}
