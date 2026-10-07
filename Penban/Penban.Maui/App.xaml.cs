using Penban.Services.Abstractions;
using Penban.Maui.Views.Markup;
using IPreferences = Penban.Services.Abstractions.IPreferences;

namespace Penban.Maui;

public partial class App : Application
{
    private readonly IServiceProvider services;
    private readonly IPreferences preferences;

    public App(ILocalizationService localizationService, IServiceProvider services, IPreferences preferences)
    {
        InitializeComponent();
        this.services = services;
        this.preferences = preferences;

        // Markup extensions can't use DI, so live language switching is broadcast through
        // this static event bus instead (see Penban.Maui.Views.Markup.TranslateExtension).
        localizationService.LanguageChanged += (_, _) => LocalizationBroadcaster.Publish();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell(services, preferences));

        // Custom title bar (Windows/Mac Catalyst) with the Penban mark.
        window.TitleBar = new TitleBar
        {
            Icon = "penban_logo.png",
            Title = "Penban",
        };

        return window;
    }
}
