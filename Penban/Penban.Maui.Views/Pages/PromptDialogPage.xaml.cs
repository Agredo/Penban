using System.Threading.Tasks;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Custom modal prompt page used in place of <c>Page.DisplayPromptAsync</c>.
/// See the remarks in <c>PromptDialogPage.xaml</c> for why the native dialog had to be avoided.
/// </summary>
public partial class PromptDialogPage : ContentPage
{
    private readonly TaskCompletionSource<string?> completionSource = new();

    public PromptDialogPage(string title, string message, string accept, string cancel, string? placeholder, string? initialValue)
    {
        InitializeComponent();

        TitleLabel.Text = title;
        if (!string.IsNullOrEmpty(message))
        {
            MessageLabel.Text = message;
            MessageLabel.IsVisible = true;
        }

        InputEntry.Placeholder = placeholder ?? string.Empty;
        InputEntry.Text = initialValue ?? string.Empty;
        AcceptButton.Text = accept;
        CancelButton.Text = cancel;
    }

    public Task<string?> ResultTask => completionSource.Task;

    protected override void OnAppearing()
    {
        base.OnAppearing();
    }

    private async void OnAcceptClicked(object? sender, EventArgs e)
    {
        completionSource.TrySetResult(InputEntry.Text);
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        completionSource.TrySetResult(null);
        await Navigation.PopModalAsync();
    }
}
