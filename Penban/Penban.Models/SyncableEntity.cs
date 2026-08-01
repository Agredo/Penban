namespace Penban.Models;

/// <summary>
/// Base class for entities that participate in the (future) cloud sync mechanism.
/// Every field on this class exists so a later sync implementation can be added
/// without breaking changes to derived entities.
/// </summary>
public abstract class SyncableEntity
{
    /// <summary>Globally unique identifier, assignable client-side without collisions.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp of the last modification, used for conflict resolution during sync.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Soft-delete flag. Entities are never hard-deleted so sync can propagate deletions.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Opaque revision/ETag tag reserved for future sync conflict detection.</summary>
    public string? SyncVersionTag { get; set; }
}
