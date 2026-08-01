namespace Penban.Maui.Views.Markup;

/// <summary>
/// Tiny static event bus that lets <see cref="TranslateExtension"/> react to language changes
/// without needing DI inside a markup extension. The head app wires this up once in
/// <c>MauiProgram</c>/<c>App.xaml.cs</c> by forwarding <c>ILocalizationService.LanguageChanged</c>
/// into <see cref="Publish"/>.
/// </summary>
public static class LocalizationBroadcaster
{
    public static event Action? Changed;

    public static void Publish() => Changed?.Invoke();
}
