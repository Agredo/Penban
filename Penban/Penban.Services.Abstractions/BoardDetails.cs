namespace Penban.Services.Abstractions;

/// <summary>
/// What a board carries beside its columns - everything the extended mode of the create page and the
/// board's details page edit. Bundled into one type for the same reason as
/// <see cref="ProjectDetails"/>: both entry points write the same fields.
/// </summary>
public sealed record BoardDetails
{
    public required string Title { get; init; }

    /// <summary>The project the board belongs to, or <c>null</c> for a board that belongs to none.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Free-text labels. Normalised by the service, so the caller may hand in raw text.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public DateTimeOffset? StartDate { get; init; }

    public DateTimeOffset? EndDate { get; init; }
}
