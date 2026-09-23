// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.ViewModels;

/// <summary>One entry of the category picker: what the user reads and what the API is told.</summary>
public sealed record FeedbackCategoryItem(string DisplayName, FeedbackCategory Category);

/// <summary>
/// The feedback form. The four categories are fixed by the Penban project on BugBear, so they are
/// listed here rather than loaded.
/// </summary>
public partial class FeedbackViewModel : ObservableObject
{
    private readonly IFeedbackService feedbackService;
    private readonly IDialogService dialogService;

    public IReadOnlyList<FeedbackCategoryItem> Categories { get; } =
    [
        new(Strings.FeedbackCategoryUi, FeedbackCategory.Ui),
        new(Strings.FeedbackCategoryUx, FeedbackCategory.Ux),
        new(Strings.FeedbackCategoryFeatureWish, FeedbackCategory.FeatureWish),
        new(Strings.FeedbackCategoryGeneral, FeedbackCategory.General),
    ];

    [ObservableProperty]
    private FeedbackCategoryItem selectedCategory;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool isBusy;

    /// <summary>Set once the API accepted the feedback; the page closes itself on that.</summary>
    [ObservableProperty]
    private bool isSubmitted;

    public bool IsNotBusy => !IsBusy;

    public FeedbackViewModel(IFeedbackService feedbackService, IDialogService dialogService)
    {
        this.feedbackService = feedbackService;
        this.dialogService = dialogService;
        selectedCategory = Categories[0];
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        // An empty description would only create an empty ticket, so it is refused here instead of
        // being disabled up front: a greyed-out button would not say why.
        var description = Description.Trim();
        if (description.Length == 0)
        {
            await dialogService.DisplayAlertAsync(Strings.FeedbackTitle, Strings.FeedbackMissingDescription, Strings.Done);
            return;
        }

        IsBusy = true;
        try
        {
            var email = Email.Trim();
            var accepted = await feedbackService.SubmitFeedbackAsync(
                SelectedCategory.Category,
                description,
                email.Length == 0 ? null : email);

            if (accepted)
            {
                await dialogService.DisplayAlertAsync(Strings.FeedbackThanksTitle, Strings.FeedbackThanksMessage, Strings.Done);
                IsSubmitted = true;
            }
            else
            {
                await dialogService.DisplayAlertAsync(Strings.FeedbackErrorTitle, Strings.FeedbackErrorMessage, Strings.Done);
            }
        }
        catch
        {
            // The service throws when the request never reached the API, which in practice means
            // there is no connection.
            await dialogService.DisplayAlertAsync(Strings.FeedbackErrorTitle, Strings.FeedbackConnectionMessage, Strings.Done);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
