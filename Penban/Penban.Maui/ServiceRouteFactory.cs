using Microsoft.Extensions.DependencyInjection;

namespace Penban.Maui;

/// <summary>
/// Builds the page of a registered route out of the container. The shell's own way of turning a route
/// into a page goes through <c>Activator.CreateInstance</c>, which cannot satisfy a page that takes
/// its view model as a constructor argument - and every page of this app does.
/// </summary>
/// <typeparam name="TPage">
/// The page the route stands for. It has to be registered in the container, since that is where the
/// page and everything it asks for are built.
/// </typeparam>
internal sealed class ServiceRouteFactory<TPage>(IServiceProvider services) : RouteFactory
    where TPage : Page
{
    // The shell asks for the page through the overload that carries a provider, and falls back to the
    // parameterless one where it has none. Both have to be implemented, and both resolve from the
    // container - which is the whole point of the type.
    public override Element GetOrCreate() => services.GetRequiredService<TPage>();

    public override Element GetOrCreate(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<TPage>();
}
