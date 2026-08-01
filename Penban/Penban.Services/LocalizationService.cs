// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using System.Globalization;
using Penban.Services.Abstractions;
using Penban.Util;

namespace Penban.Services;

/// <summary>
/// Default, platform-agnostic implementation of <see cref="ILocalizationService"/>. Persists
/// the selected language via <see cref="IPreferences"/> (not via Microsoft.Maui.Storage
/// directly), so this class stays unit-testable without a MAUI runtime.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private const string LanguagePreferenceKey = "InkLanguage";

    private readonly IPreferences preferences;

    public LocalizationService(IPreferences preferences)
    {
        this.preferences = preferences;
        AvailableLanguages = LocalizationHelper.DiscoverAvailableLanguages();

        var savedLanguage = preferences.Get(LanguagePreferenceKey, string.Empty);
        CurrentLanguage = TryParseCulture(savedLanguage) ?? CultureInfo.CurrentUICulture;
        ApplyCulture(CurrentLanguage);
    }

    public CultureInfo CurrentLanguage { get; private set; }

    public IReadOnlyList<CultureInfo> AvailableLanguages { get; }

    public event EventHandler? LanguageChanged;

    public Task SetLanguageAsync(CultureInfo culture)
    {
        CurrentLanguage = culture;
        ApplyCulture(culture);
        preferences.Set(LanguagePreferenceKey, culture.Name);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private static void ApplyCulture(CultureInfo culture)
    {
        CultureInfo.CurrentUICulture = culture;
        Strings.Culture = culture;
    }

    private static CultureInfo? TryParseCulture(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
