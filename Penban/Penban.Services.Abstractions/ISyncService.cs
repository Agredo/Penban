namespace Penban.Services.Abstractions;

/// <summary>
/// Prepares the app for a future cloud sync feature. The first version ships only
/// a local, no-op implementation (<c>LocalOnlySyncService</c>); no data is transmitted.
/// </summary>
public interface ISyncService
{
    Task PushAsync(CancellationToken ct = default);

    Task PullAsync(CancellationToken ct = default);
}
