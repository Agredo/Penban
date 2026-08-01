using Microsoft.Maui.Controls;
using Penban.Services.Abstractions;

namespace Penban.Maui.Services;

/// <summary>MAUI-backed implementation of <see cref="INavigation"/>, wrapping <see cref="Shell.Current"/>.</summary>
public class MauiNavigation : Penban.Services.Abstractions.INavigation
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task GoToAsync(string route, IDictionary<string, object> parameters)
        => Shell.Current.GoToAsync(route, parameters);

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
