using Penban.Services.Abstractions;
using Penban.Util;
using Penban.ViewModels;
using AgredoApplication.MVVM.Services.Abstractions.Navigation;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Central place for the app's switches. Each switch writes through to the preferences store the
/// moment it is flipped, so leaving the page needs no confirmation.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel viewModel;
    private readonly FeedbackViewModel feedbackViewModel;
    private readonly INavigationService navigation;

    public SettingsPage(SettingsViewModel viewModel, FeedbackViewModel feedbackViewModel, INavigationService navigation)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.feedbackViewModel = feedbackViewModel;
        this.navigation = navigation;
        BindingContext = viewModel;

        // Only Windows reports which end of the pen is against the surface, so the row would be a
        // dead switch anywhere else. The same goes for the button on the pen itself, and for the
        // Apple Pencil's own double-tap, which is iOS only.
        PenTailEraserRow.IsVisible = OperatingSystem.IsWindows();
        PenButtonLassoRow.IsVisible = OperatingSystem.IsWindows();
        PencilTapRow.IsVisible = OperatingSystem.IsIOS();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Picks up changes made elsewhere, e.g. the inline finger-drawing toggle in the editor.
        viewModel.Refresh();

        // A formatted string is not covered by the translate markup extension, so it is written here,
        // where the language and the version are both known to be current.
        VersionLabel.Text = string.Format(Strings.AboutVersionFormat, AppVersion.Display, AppVersion.Build);
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (IsModal)
        {
            await Navigation.PopModalAsync();
            return;
        }

        // The shell's own way back, so that its idea of where we are stays in step with the stack.
        await navigation.NavigateBack();
    }

    private async void OnHelpClicked(object? sender, TappedEventArgs e)
    {
        await OpenAsync(AppRoutes.Help, () => new HelpPage(navigation));
    }

    private async void OnPrivacyClicked(object? sender, TappedEventArgs e)
    {
        await OpenAsync(AppRoutes.Privacy, () => new PrivacyPage(navigation));
    }

    private async void OnAboutClicked(object? sender, TappedEventArgs e)
    {
        await OpenAsync(AppRoutes.About, () => new AboutPage(navigation));
    }

    private async void OnFeedbackClicked(object? sender, TappedEventArgs e)
    {
        await OpenAsync(AppRoutes.Feedback, () => new FeedbackPage(feedbackViewModel, navigation));
    }

    /// <summary>
    /// Opens a page on top of this one. Normally that is a registered route, which the shell puts on
    /// the stack it knows about. A page that is up modally has only the modal stack behind it - the
    /// shell's routing would push the next page underneath it - so while this page is modal the page
    /// is built here instead and pushed onto the modal stack.
    /// </summary>
    /// <param name="route">The route of the page to open; the one that is used while not modal.</param>
    /// <param name="modalPage">
    /// Builds the page for the modal case. Only called while this page is modal, so the common case
    /// pays for nothing.
    /// </param>
    private async Task OpenAsync(string route, Func<ContentPage> modalPage)
    {
        if (IsModal)
        {
            await Navigation.PushModalAsync(modalPage());
            return;
        }

        await navigation.ShellNavigationTo(route);
    }

    /// <summary>
    /// Whether this page was put up over another one rather than onto the shell's stack, which is what
    /// the ink editor does with it. A modal page has only the modal stack behind it - the window's own
    /// stack refuses a push and a pop - so both ways off this page have to go through the modal one
    /// while it is up.
    /// </summary>
    private bool IsModal => Navigation.ModalStack.Contains(this);
}
