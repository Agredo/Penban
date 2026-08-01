namespace Penban.Services.Abstractions;

/// <summary>
/// Abstraction over Shell-based navigation, so ViewModels never reference
/// <c>Microsoft.Maui.Controls.Shell</c> directly.
/// </summary>
public interface INavigation
{
    Task GoToAsync(string route);

    Task GoToAsync(string route, IDictionary<string, object> parameters);

    Task GoBackAsync();
}
