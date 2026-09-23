namespace Penban.Services.Abstractions;

/// <summary>
/// The categories the Penban project on BugBear sorts feedback into. The API addresses them by id,
/// so the order here has no meaning beyond the order they are offered in.
/// </summary>
public enum FeedbackCategory
{
    Ui,
    Ux,
    FeatureWish,
    General,
}

/// <summary>
/// Sends user feedback to the BugBear feedback API.
/// </summary>
public interface IFeedbackService
{
    /// <summary>
    /// Submits one piece of feedback. Returns <c>false</c> when the API rejected the request;
    /// a missing network connection throws instead.
    /// </summary>
    Task<bool> SubmitFeedbackAsync(FeedbackCategory category, string description, string? submitterEmail = null);
}
