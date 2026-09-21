using System.Threading.Tasks;

namespace Penban.Maui.Views.Pages;

/// <summary>
/// Custom modal menu used in place of <c>Page.DisplayActionSheet</c>. See the remarks in
/// <c>ActionSheetPage.xaml</c> for why the native dialog had to be avoided.
/// </summary>
public partial class ActionSheetPage : ContentPage
{
    private readonly TaskCompletionSource<int> completionSource = new();

    public ActionSheetPage(string title, string cancel, IReadOnlyList<string> options)
    {
        InitializeComponent();

        TitleLabel.Text = title;
        TitleLabel.IsVisible = !string.IsNullOrEmpty(title);
        CancelButton.Text = cancel;

        BindableLayout.SetItemsSource(
            OptionsStack,
            options.Select((text, index) => new Option(index, text)).ToList());
    }

    /// <summary>Index of the chosen option, or -1 when the menu was dismissed.</summary>
    public Task<int> ResultTask => completionSource.Task;

    /// <summary>
    /// Closes the menu as "dismissed" whenever the page goes away for any reason other than the two
    /// handlers below - a hardware back button, for instance. Without this the caller would wait for
    /// an answer that can never arrive.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        completionSource.TrySetResult(-1);
    }

    private async void OnOptionClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Option option)
        {
            completionSource.TrySetResult(option.Index);
        }

        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        completionSource.TrySetResult(-1);
        await Navigation.PopModalAsync();
    }

    /// <summary>
    /// Carries the position of an option, so that a handler never has to compare the labels it was
    /// handed - two menu entries that happen to read the same would be indistinguishable.
    /// </summary>
    public sealed class Option(int index, string text)
    {
        public int Index { get; } = index;

        public string Text { get; } = text;
    }
}
