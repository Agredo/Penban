using Microsoft.Maui.Storage;
using Penban.Services.Abstractions;

namespace Penban.Maui.Services;

/// <summary>MAUI-backed implementation of <see cref="ISecureStorage"/>, wrapping <see cref="SecureStorage.Default"/>.</summary>
public class MauiSecureStorage : Penban.Services.Abstractions.ISecureStorage
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}
