namespace Penban.Services.Abstractions;

/// <summary>
/// Every route of the app in one place, so that no page type has to be named outside of
/// <c>AppShell</c>.
/// <para>
/// A <b>detail page</b> is registered on the shell's routing and is pushed onto whatever is on
/// screen, which is why its route is relative (<c>settings</c>). An <b>area</b> is an entry of the
/// shell's flyout and is addressed absolutely (<c>//boards</c>); those arrive with the flyout.
/// </para>
/// </summary>
public static class AppRoutes
{
    /// <summary>
    /// The areas of the app, each an entry of the shell's flyout. Which of them the app opens with is
    /// the user's choice (<see cref="PreferenceKeys.StartPage"/>); the flyout reaches all of them
    /// either way.
    /// </summary>
    public const string Dashboard = "dashboard";

    public const string Projects = "projects";
    public const string Boards = "boards";

    public const string Settings = "settings";
    public const string Help = "help";
    public const string Privacy = "privacy";
    public const string About = "about";
    public const string Feedback = "feedback";

    /// <summary>
    /// One board's details, both when it is created and when it is edited. Takes
    /// <see cref="QueryParameters.BoardId"/> to edit an existing board and
    /// <see cref="QueryParameters.ProjectId"/> to put a new one into a project.
    /// </summary>
    public const string BoardDetails = "boardDetails";
}
