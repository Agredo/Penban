using Microsoft.Extensions.DependencyInjection;
using Penban.Maui.Views.Pages;

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
    }

    /// <summary>
    /// The overview this shell built, as long as it exists. What needs it is not a page and has no
    /// way of its own to get there - a tap on the home screen widget, which is opened out of the
    /// app's lifecycle (see <see cref="MauiProgram"/>) rather than from a row in the list.
    /// </summary>
    internal BoardsPage? Boards { get; private set; }
}
