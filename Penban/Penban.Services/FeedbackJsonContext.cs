using System.Text.Json.Serialization;

namespace Penban.Services;

/// <summary>
/// Body of a feedback request. The property names are the ones the BugBear endpoint expects, which
/// is why they are spelled out instead of being derived from a naming policy.
/// </summary>
internal sealed record FeedbackRequest(
    [property: JsonPropertyName("productApiKey")] string ProductApiKey,
    [property: JsonPropertyName("categoryId")] string CategoryId,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("submitterEmail")] string? SubmitterEmail,
    [property: JsonPropertyName("metadata")] string Metadata,
    [property: JsonPropertyName("productVersionId")] string? ProductVersionId);

/// <summary>
/// Source-generated serialization for <see cref="FeedbackRequest"/>, for the same reason as
/// <see cref="PenbanJsonContext"/>: the reflection-based serializer is trimmed away in a release
/// build of the mobile heads, which is the build a user would send feedback from.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(FeedbackRequest))]
internal sealed partial class FeedbackJsonContext : JsonSerializerContext;
