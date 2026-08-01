using Microsoft.Maui.Controls;
using Penban.Maui.Views.Pages;
using Penban.Services.Abstractions;

namespace Penban.Maui.Services;

/// <summary>MAUI-backed implementation of <see cref="IDialogService"/>, wrapping <see cref="Page"/> alert/prompt APIs.</summary>
public class MauiDialogService : IDialogService
{
    private static Page CurrentPage
        => Shell.Current?.CurrentPage
           ?? Application.Current?.Windows.FirstOrDefault()?.Page
           ?? throw new InvalidOperationException("No active page is available to display a dialog.");

    public Task DisplayAlertAsync(string title, string message, string cancel)
        => CurrentPage.DisplayAlertAsync(title, message, cancel);

    public Task<bool> DisplayConfirmationAsync(string title, string message, string accept, string cancel)
        => CurrentPage.DisplayAlertAsync(title, message, accept, cancel);

    public async Task<string?> DisplayPromptAsync(string title, string message, string accept, string cancel, string? placeholder = null, string? initialValue = null)
    {
        // Uses a custom modal page instead of Page.DisplayPromptAsync's native ContentDialog;
        // see PromptDialogPage.xaml remarks for background.
        var page = new PromptDialogPage(title, message, accept, cancel, placeholder, initialValue);
        await CurrentPage.Navigation.PushModalAsync(page);
        return await page.ResultTask;
    }
}
