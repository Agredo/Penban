namespace Penban.Services.Abstractions;

/// <summary>
/// What a project carries beside the boards under it. Bundled into one type so the create dialog and
/// the project's details page write the same fields, and so a field added later is added in one place
/// rather than threaded through two long parameter lists.
/// </summary>
public sealed record ProjectDetails
{
    public required string Title { get; init; }

    /// <summary>Free-text labels. Normalised by the service, so the caller may hand in raw text.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public DateTimeOffset? StartDate { get; init; }

    public DateTimeOffset? EndDate { get; init; }
}
