using AgredoApplication.MVVM.Services.Abstractions.Navigation;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// The first-steps page: what the pen does, what the lasso does and where the rest is. Everything on
/// it is static text, so the page has no view model and only has to know how to close itself.
/// </summary>
public partial class HelpPage : ContentPage
{
    private readonly INavigationService navigation;

    public HelpPage(INavigationService navigation)
    {
        InitializeComponent();
        this.navigation = navigation;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await PopAsync();
    }

    /// <summary>
    /// Whether this page was put up over another one rather than onto the shell's stack, which is what
    /// a settings page opened over the ink editor does with it. A modal page has only the modal stack
    /// behind it - the window's own stack refuses a pop - so closing this page has to go through the
    /// modal one while it is up.
    /// </summary>
    private bool IsModal => Navigation.ModalStack.Contains(this);

    private Task PopAsync() => IsModal ? Navigation.PopModalAsync() : navigation.NavigateBack();
}
