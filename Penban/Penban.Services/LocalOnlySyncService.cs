// Framework-agnostic: no Microsoft.Maui.* usings allowed in this file.
using Penban.Services.Abstractions;

namespace Penban.Services;

/// <summary>
/// No-op placeholder for <see cref="ISyncService"/>. The first version of the app ships
/// without any real cloud sync; this implementation exists purely so the rest of the app
/// (and DI wiring) already depends on the final interface shape.
/// </summary>
public class LocalOnlySyncService : ISyncService
{
    public Task PushAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task PullAsync(CancellationToken ct = default) => Task.CompletedTask;
}
