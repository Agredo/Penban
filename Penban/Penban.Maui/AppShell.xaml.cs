using AgredoApplication.MVVM.Services.Abstractions.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Penban.Maui.Views.Controls;
using Penban.Maui.Views.Markup;
using Penban.Maui.Views.Pages;
using Penban.Services.Abstractions;
using Penban.Util;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui;

public partial class AppShell : Shell
{
    /// <summary>
    /// The areas of the flyout, in the order they stand in it, with the glyph that stands beside each
    /// one. The dashboard leads, because it is what the app opens on unless the settings say otherwise
    /// and because it is the one of the three that says where to go next. A title is the key of a
    /// string rather than the string itself, so the menu is written in the same language as the pages
    /// it leads to.
    /// </summary>
    private static readonly (string Route, string TitleKey, string Glyph)[] Areas =
    [
        (AppRoutes.Dashboard, nameof(Strings.DashboardTitle), IconFont.Home),
        (AppRoutes.Projects, nameof(Strings.ProjectsTitle), IconFont.Grid),
        (AppRoutes.Boards, nameof(Strings.BoardsTitle), IconFont.Board),
    ];

    /// <summary>
    /// What the menu offers under the areas. These are not areas: an area is a place the app stays and
    /// the menu keeps it picked, while one of these opens on top of whatever is on screen and the way
    /// back leads there again. What the entry carries is the route of its page, which is what the
    /// shell was told about when the routes were registered below.
    /// </summary>
    private static readonly (string Route, string TitleKey, string Glyph)[] MenuPages =
    [
        (AppRoutes.Settings, nameof(Strings.Settings), IconFont.Settings),
        (AppRoutes.Help, nameof(Strings.HelpTitle), IconFont.Help),
        (AppRoutes.Feedback, nameof(Strings.FeedbackTitle), IconFont.Feedback),
        (AppRoutes.Privacy, nameof(Strings.PrivacyTitle), IconFont.Privacy),
        (AppRoutes.About, nameof(Strings.AboutTitle), IconFont.Info),
    ];

    private readonly IServiceProvider services;
    private readonly INavigationService navigation;

    public AppShell(IServiceProvider services, IPreferences preferences)
    {
        InitializeComponent();
        this.services = services;
        navigation = services.GetRequiredService<INavigationService>();

        var items = new Dictionary<string, FlyoutItem>();

        // The areas lead the menu and the pages follow it, whatever order the two were made in: the
        // pages are made below, so the areas are put in front of them as they are made.
        for (var index = 0; index < Areas.Length; index++)
        {
            var (route, titleKey, glyph) = Areas[index];

            // The route goes on the content alone. Shell builds a route per entry, section and
            // content, and two of them carrying the same name is what makes an absolute route such as
            // //boards ambiguous - so the entry is titled, and the content is what a route reaches.
            var content = new ShellContent
            {
                Route = route,
                Content = CreatePage(route),
            };
            content.SetBinding(ShellContent.TitleProperty, TranslateExtension.Create(titleKey));

            var item = new FlyoutItem
            {
                Icon = new FontImageSource { FontFamily = IconFont.FontFamily, Glyph = glyph, Size = 22 },
            };
            item.SetBinding(FlyoutItem.TitleProperty, TranslateExtension.Create(titleKey));

            item.Items.Add(content);
            items[route] = item;
            Items.Insert(index, item);
        }

        foreach (var (route, titleKey, glyph) in MenuPages)
        {
            // A page of the menu is a menu item and not an entry of its own: picking it runs what it
            // is told to run and leaves the menu where it was, which is what opening a page on top of
            // the one on screen is.
            var entry = new MenuItem
            {
                CommandParameter = route,
                IconImageSource = new FontImageSource { FontFamily = IconFont.FontFamily, Glyph = glyph, Size = 22 },
            };
            entry.SetBinding(MenuItem.TextProperty, TranslateExtension.Create(titleKey));
            entry.Clicked += OnMenuEntryClicked;

            Items.Add(entry);
        }

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

        // Where the app opens. An area is picked by its flyout entry rather than by its content: the
        // shell's current item is an entry of the flyout, and the entry is what a later switch of the
        // start page and the highlighted row of the menu both read.
        CurrentItem = items[StartRoute(preferences)];
    }

    /// <summary>
    /// Opens the page one entry of the menu stands for. The menu is a pane beside the page rather than
    /// a page of its own, so it is closed here: left open, it would stand in front of what was just
    /// asked for.
    /// </summary>
    private async void OnMenuEntryClicked(object? sender, EventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: string route })
        {
            return;
        }

        FlyoutIsPresented = false;
        await navigation.ShellNavigationTo(route);
    }

    /// <summary>
    /// The page behind one area.
    /// <para>
    /// The board overview is built here and handed to the shell as its content rather than left to a
    /// template, because a tap on the home screen widget arrives out of the app's lifecycle and needs
    /// that page even when the boards were never opened (<see cref="Boards"/>, and see
    /// <c>MauiProgram</c>). The other two are built here as well: a page is only read once the shell
    /// puts it on screen, so building all three costs one page more than building the one the app
    /// opens on, and it keeps the three from being built three different ways.
    /// </para>
    /// </summary>
    private Page CreatePage(string route) => route switch
    {
        AppRoutes.Dashboard => services.GetRequiredService<DashboardPage>(),
        AppRoutes.Projects => services.GetRequiredService<ProjectsPage>(),
        AppRoutes.Boards => Boards = services.GetRequiredService<BoardsPage>(),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "No area of the flyout carries this route."),
    };

    /// <summary>
    /// The area the app opens on. Anything that is not one of the three - a store that has never held
    /// the setting, or one that holds a route that no longer exists - means the dashboard, so a start
    /// page that cannot be honoured falls back rather than leaving the app with nowhere to land.
    /// </summary>
    private static string StartRoute(IPreferences preferences)
    {
        var stored = preferences.Get(PreferenceKeys.StartPage, AppRoutes.Dashboard);
        return Array.Exists(Areas, area => area.Route == stored) ? stored : AppRoutes.Dashboard;
    }

    /// <summary>
    /// The overview this shell built. What needs it is not a page and has no way of its own to get
    /// there - a tap on the home screen widget, which is opened out of the app's lifecycle (see
    /// <see cref="MauiProgram"/>) rather than from a row in the list.
    /// </summary>
    internal BoardsPage? Boards { get; private set; }
}
