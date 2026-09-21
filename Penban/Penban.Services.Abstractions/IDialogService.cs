namespace Penban.Services.Abstractions;

/// <summary>
/// Abstraction over simple alert/confirmation/prompt dialogs, so ViewModels never reference
/// <c>Microsoft.Maui.Controls.Page.DisplayAlert</c>/<c>DisplayPromptAsync</c> directly.
/// </summary>
public interface IDialogService
{
    Task DisplayAlertAsync(string title, string message, string cancel);

    Task<bool> DisplayConfirmationAsync(string title, string message, string accept, string cancel);

    Task<string?> DisplayPromptAsync(string title, string message, string accept, string cancel, string? placeholder = null, string? initialValue = null);

    /// <summary>
    /// Shows a menu of choices. Returns the index of the chosen option, or <c>-1</c> when the user
    /// dismissed the menu without picking one.
    /// </summary>
    Task<int> DisplayActionSheetAsync(string title, string cancel, params string[] options);
}
