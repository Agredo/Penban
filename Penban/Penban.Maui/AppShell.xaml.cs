using Microsoft.Extensions.DependencyInjection;
using Penban.Maui.Views.Pages;
using Penban.Services.Abstractions;

namespace Penban.Maui;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        InitializeComponent();

        // Shell's {DataTemplate} markup extension resolves types via Activator.CreateInstance,
        // which can't satisfy BoardsPage's constructor-injected BoardsViewModel. Building the
        // ShellContent here lets us route page creation through the DI container instead.
        Items.Add(new ShellContent
        {
            Title = "Boards",
            Route = "boards",
            ContentTemplate = new DataTemplate(() => Boards = services.GetRequiredService<BoardsPage>()),
        });

        // The pages that go on top of whatever is on screen. A route is what lets a page open
        // another one without naming its type, and the factory is what gives that route a page with
        // its dependencies in place, which the shell would otherwise have to build by hand.
        Routing.RegisterRoute(AppRoutes.Settings, new ServiceRouteFactory<SettingsPage>(services));
        Routing.RegisterRoute(AppRoutes.Help, new ServiceRouteFactory<HelpPage>(services));
        Routing.RegisterRoute(AppRoutes.Privacy, new ServiceRouteFactory<PrivacyPage>(services));
        Routing.RegisterRoute(AppRoutes.About, new ServiceRouteFactory<AboutPage>(services));
        Routing.RegisterRoute(AppRoutes.Feedback, new ServiceRouteFactory<FeedbackPage>(services));
        Routing.RegisterRoute(AppRoutes.BoardDetails, new ServiceRouteFactory<BoardDetailsPage>(services));
        Routing.RegisterRoute(AppRoutes.ProjectDetails, new ServiceRouteFactory<ProjectDetailsPage>(services));
        Routing.RegisterRoute(AppRoutes.Project, new ServiceRouteFactory<ProjectPage>(services));
    }

    /// <summary>
    /// The overview this shell built, as long as it exists. What needs it is not a page and has no
    /// way of its own to get there - a tap on the home screen widget, which is opened out of the
    /// app's lifecycle (see <see cref="MauiProgram"/>) rather than from a row in the list.
    /// </summary>
    internal BoardsPage? Boards { get; private set; }
}
