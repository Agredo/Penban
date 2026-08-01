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
            ContentTemplate = new DataTemplate(() => services.GetRequiredService<BoardsPage>()),
        });

        Routing.RegisterRoute("board", typeof(BoardHostPage));
    }
}
