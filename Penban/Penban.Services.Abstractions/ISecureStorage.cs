namespace Penban.Services.Abstractions;

/// <summary>
/// Abstraction over secure, encrypted key/value storage, so platform-agnostic code
/// never references <c>Microsoft.Maui.Storage.SecureStorage</c> directly.
/// </summary>
public interface ISecureStorage
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    void Remove(string key);
}
