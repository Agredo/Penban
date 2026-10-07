using AgredoApplication.MVVM.Services.Abstractions.Navigation;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// What the app does with the user's data, in plain words: everything stays on the device except the
/// feedback that is sent on purpose. Static text, so no view model.
/// </summary>
public partial class PrivacyPage : ContentPage
{
    private readonly INavigationService navigation;

    public PrivacyPage(INavigationService navigation)
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
