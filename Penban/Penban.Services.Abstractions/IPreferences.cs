namespace Penban.Services.Abstractions;

/// <summary>
/// Abstraction over simple key/value app preferences, so platform-agnostic code
/// never references <c>Microsoft.Maui.Storage.Preferences</c> directly.
/// </summary>
public interface IPreferences
{
    string Get(string key, string defaultValue);

    void Set(string key, string value);

    void Remove(string key);
}
