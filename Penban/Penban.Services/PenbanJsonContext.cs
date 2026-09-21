using System.Text.Json.Serialization;
using Penban.Services.Abstractions;

namespace Penban.Services;

/// <summary>
/// Source-generated serialization for <see cref="PenbanFile"/>. The reflection-based serializer
/// would be trimmed away in a release build of the mobile heads, which is exactly the build a user
/// would take a backup with, so the contract is generated instead of discovered at runtime.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(PenbanFile))]
internal sealed partial class PenbanJsonContext : JsonSerializerContext;
