namespace Penban.Services.Abstractions;

/// <summary>
/// The names under which a route hands something to the page it opens. They live beside
/// <see cref="AppRoutes"/> rather than on the view model that reads them, because the page that
/// navigates and the view model that is being navigated to do not know each other.
/// </summary>
public static class QueryParameters
{
    /// <summary>The board a page is about.</summary>
    public const string BoardId = "boardId";

    /// <summary>The project a page is about, or the one a board is being put into.</summary>
    public const string ProjectId = "projectId";
}
