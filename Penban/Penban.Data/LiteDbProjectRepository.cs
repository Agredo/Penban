using LiteDB;
using Penban.Models;
using Penban.Services.Abstractions;

namespace Penban.Data;

/// <summary>LiteDB-backed implementation of <see cref="IProjectRepository"/>.</summary>
public class LiteDbProjectRepository : IProjectRepository
{
    private readonly LiteDbContext context;

    public LiteDbProjectRepository(LiteDbContext context) => this.context = context;

    /// <summary>
    /// Asked for per call, not kept: the collection belongs to the database instance of the moment,
    /// and that one is closed when the app is left (see <see cref="LiteDbContext.Suspend"/>).
    /// </summary>
    private ILiteCollection<Project> Projects => context.Collection<Project>("projects");

    public Task<List<Project>> GetAllAsync()
        => DatabaseWork.RunAsync(() => Projects.Find(p => !p.IsDeleted).ToList());

    public Task SaveAsync(Project project) => DatabaseWork.RunAsync(() =>
    {
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Projects.Upsert(project);
    });

    public Task DeleteAsync(Guid projectId) => DatabaseWork.RunAsync(() =>
    {
        var project = Projects.FindById(projectId);
        if (project is null)
        {
            return;
        }

        project.IsDeleted = true;
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Projects.Update(project);
    });

    public Task SaveAllAsync(IReadOnlyList<Project> imported) => DatabaseWork.RunAsync(() =>
    {
        // Upsert rather than Insert: it writes what it is given without touching it, and it does not
        // abort the whole import when a file happens to name the same project twice.
        if (imported.Count > 0)
        {
            Projects.Upsert(imported);
        }
    });

    public Task ClearAsync() => DatabaseWork.RunAsync(() =>
    {
        Projects.DeleteAll();
    });
}
