using Penban.Util;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Who made the app, which version is running and which third-party components it is built on. Static
/// apart from the version, which comes from <see cref="AppVersion"/>.
/// </summary>
public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Set here rather than in the constructor: the page is built once per navigation, but the
        // language can have changed since, and a formatted string is not covered by the translate
        // markup extension.
        VersionLabel.Text = string.Format(Strings.AboutVersionFormat, AppVersion.Display, AppVersion.Build);
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await PopAsync();
    }

    private async void OnBugBearLinkTapped(object? sender, TappedEventArgs e)
    {
        await Launcher.OpenAsync("https://www.bug-bear.com");
    }

    /// <summary>
    /// Whether this page was put up over another one rather than onto the shell's stack, which is what
    /// a settings page opened over the ink editor does with it. A modal page has only the modal stack
    /// behind it - the window's own stack refuses a pop - so closing this page has to go through the
    /// modal one while it is up.
    /// </summary>
    private bool IsModal => Navigation.ModalStack.Contains(this);

    private Task PopAsync() => IsModal ? Navigation.PopModalAsync() : Navigation.PopAsync();
}
