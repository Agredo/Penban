using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penban.Recognition.Onnx;

/// <summary>
/// The model catalog that ships next to the ONNX files: everything the runtime needs to know about a
/// PP-OCRv6 line recognizer that cannot be read off the graph itself.
/// <para>
/// An ONNX file describes its tensors but not what they mean. Nothing in the graph says that the
/// output is a CTC sequence, that label 0 is the blank, that the codec maps label 1 to a space, or
/// that the input has to be inverted. All of that is convention, and a wrong guess produces plausible
/// nonsense rather than an error - which is why it lives in a file with a hash instead of in code
/// comments.
/// </para>
/// <para>
/// The catalog covers every variant and precision; a per-model manifest is the same document without
/// <see cref="Variants"/>, plus the single model entry. Both shapes load into this type, and the model
/// is selected by <see cref="FindModel(string)"/> rather than by position.
/// </para>
/// </summary>
public sealed class Ppocrv6Catalog
{
    /// <summary>The only catalog format this reader understands.</summary>
    public const string ExpectedFormat = "penban-ctc-line-model";

    /// <summary>
    /// The unicode normal form the recognizer applies to decoded text. Only NFC is implemented; a
    /// catalog asking for another form is rejected instead of silently ignored, because the form is
    /// part of the cache key and a silent change would invalidate stored text.
    /// </summary>
    public const string SupportedUnicodeForm = "nfc";

    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("catalogVersion")]
    public int CatalogVersion { get; init; }

    [JsonPropertyName("family")]
    public string? Family { get; init; }

    [JsonPropertyName("origin")]
    public string? Origin { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    /// <summary>Unicode normal form of the decoded text, e.g. <c>NFC</c>.</summary>
    [JsonPropertyName("unicodeForm")]
    public string? UnicodeForm { get; init; }

    [JsonPropertyName("input")]
    public Ppocrv6Input? Input { get; init; }

    [JsonPropertyName("output")]
    public Ppocrv6Output? Output { get; init; }

    [JsonPropertyName("decoding")]
    public Ppocrv6Decoding? Decoding { get; init; }

    [JsonPropertyName("codec")]
    public Ppocrv6Codec? Codec { get; init; }

    /// <summary>Model variants by name (<c>tiny</c>, <c>small</c>, <c>medium</c>). Absent in a per-model manifest.</summary>
    [JsonPropertyName("variants")]
    public Dictionary<string, Ppocrv6Variant>? Variants { get; init; }

    /// <summary>
    /// Reads a catalog or per-model manifest. Throws <see cref="InvalidDataException"/> for anything
    /// that is not a catalog, and <see cref="JsonException"/> for malformed JSON - a model that cannot
    /// be described correctly must not be run at all.
    /// </summary>
    public static Ppocrv6Catalog Load(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var catalog = JsonSerializer.Deserialize(json, Ppocrv6JsonContext.Default.Ppocrv6Catalog)
            ?? throw new InvalidDataException("Der Modellkatalog ist leer.");

        catalog.Validate();

        return catalog;
    }

    /// <inheritdoc cref="Load(Stream)" />
    public static Ppocrv6Catalog Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var catalog = JsonSerializer.Deserialize(json, Ppocrv6JsonContext.Default.Ppocrv6Catalog)
            ?? throw new InvalidDataException("Der Modellkatalog ist leer.");

        catalog.Validate();

        return catalog;
    }

    /// <summary>
    /// Checks the parts the runtime depends on. Only structural questions are asked here - whether a
    /// model entry is really the file it claims to be is answered by its hash in
    /// <see cref="OnnxInkTextRecognizer.Create(byte[], Ppocrv6Catalog, string, string?, int?)"/>.
    /// </summary>
    public void Validate()
    {
        if (!string.Equals(Format, ExpectedFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Modellkatalog hat das Format '{Format}', erwartet wird '{ExpectedFormat}'.");
        }

        if (Input?.Name is not { Length: > 0 })
        {
            throw new InvalidDataException("Modellkatalog nennt keinen Eingabenamen (input.name).");
        }

        if (Input.Height <= 0)
        {
            throw new InvalidDataException("Modellkatalog nennt keine gültige Eingabehöhe (input.height).");
        }

        if (Output is null || Output.Labels is not { Length: > 0 } || Output.Confidences is not { Length: > 0 })
        {
            throw new InvalidDataException(
                "Modellkatalog nennt nicht beide Ausgaben (output.labels, output.confidences).");
        }

        if (Codec is null)
        {
            throw new InvalidDataException("Modellkatalog enthält keinen Codec.");
        }

        // Forces the label table to be built, so a broken codec fails here and not on the first line.
        _ = Codec.LabelTable;

        var form = UnicodeForm?.ToLowerInvariant() ?? SupportedUnicodeForm;
        if (!string.Equals(form, SupportedUnicodeForm, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Der Modellkatalog verlangt die Unicode-Normalform '{UnicodeForm}'; unterstützt wird nur " +
                $"'{SupportedUnicodeForm.ToUpperInvariant()}'.");
        }
    }

    /// <summary>The model entry with the given id, or <c>null</c> when the catalog does not contain it.</summary>
    public Ppocrv6ModelEntry? FindModel(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        if (Variants is null)
        {
            return null;
        }

        foreach (var variant in Variants.Values)
        {
            foreach (var model in variant.Models ?? [])
            {
                if (string.Equals(model.Id, modelId, StringComparison.Ordinal))
                {
                    return model;
                }
            }
        }

        return null;
    }

    /// <summary>The model entry for a variant name and precision, e.g. <c>("small", "fp16")</c>.</summary>
    public Ppocrv6ModelEntry? FindModel(string variant, string precision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        ArgumentException.ThrowIfNullOrWhiteSpace(precision);

        if (Variants is null || !Variants.TryGetValue(variant, out var entry))
        {
            return null;
        }

        foreach (var model in entry.Models ?? [])
        {
            if (string.Equals(model.Precision, precision, StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }
        }

        return null;
    }
}

/// <summary>How the model wants its image, as the catalog states it.</summary>
public sealed class Ppocrv6Input
{
    /// <summary>Name of the graph input, e.g. <c>image</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("channels")]
    public int Channels { get; init; }

    /// <summary>White margin in pixels the model expects on both sides, matching <c>InkLineRenderer</c>.</summary>
    [JsonPropertyName("padding")]
    public int Padding { get; init; }

    /// <summary>Whether ink is <c>1</c> and paper <c>0</c>, as <c>InkLineImage.ToModelInput</c> produces it.</summary>
    [JsonPropertyName("invert")]
    public bool Invert { get; init; }

    [JsonPropertyName("dynamicWidth")]
    public bool DynamicWidth { get; init; }
}

/// <summary>The graph outputs and what they contain.</summary>
public sealed class Ppocrv6Output
{
    /// <summary>Name of the int64 label tensor, e.g. <c>labels</c>.</summary>
    [JsonPropertyName("labels")]
    public string? Labels { get; init; }

    /// <summary>Name of the float32 confidence tensor, e.g. <c>confs</c>.</summary>
    [JsonPropertyName("confidences")]
    public string? Confidences { get; init; }

    /// <summary>Label that means "no character here", i.e. the CTC blank.</summary>
    [JsonPropertyName("blank")]
    public int Blank { get; init; }
}

/// <summary>
/// The decoding convention. The softmax, the argmax and the per-step confidence all happen inside the
/// graph, so only the CTC collapse is left for the caller - see <see cref="CtcDecoder"/>.
/// </summary>
public sealed class Ppocrv6Decoding
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 1.0;

    [JsonPropertyName("blank")]
    public int Blank { get; init; }
}

/// <summary>
/// The label table of the recognizer: which character each output label stands for.
/// <para>
/// Label <c>0</c> is the CTC blank and has no character. The single-character entries are keyed by
/// label index as a string, one-based, so the table is dense and can be indexed directly.
/// </para>
/// </summary>
public sealed class Ppocrv6Codec
{
    private string?[]? _labelTable;

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    /// <summary>Label index (as string) to character.</summary>
    [JsonPropertyName("singles")]
    public Dictionary<string, string>? Singles { get; init; }

    /// <summary>
    /// Multi-character entries. Left as a raw element because the PP-OCRv6 codecs are empty here and
    /// the shape is not pinned down; a non-empty table is rejected rather than misread.
    /// </summary>
    [JsonPropertyName("sequences")]
    public JsonElement Sequences { get; init; }

    /// <summary>
    /// Characters by label, with index <c>0</c> left <c>null</c> for the blank. Built once and cached;
    /// the input is immutable, so a concurrent second build would produce the same table.
    /// </summary>
    public string?[] LabelTable => _labelTable ??= BuildLabelTable();

    private string?[] BuildLabelTable()
    {
        if (Singles is not { Count: > 0 })
        {
            throw new InvalidDataException("Der Codec enthält keine Einzelzeichen (codec.singles).");
        }

        if (Sequences.ValueKind == JsonValueKind.Array && Sequences.GetArrayLength() > 0)
        {
            throw new NotSupportedException(
                "Der Codec enthält Mehrzeichen-Einträge (codec.sequences). Diese Variante ist nicht " +
                "implementiert, weil sie in allen PP-OCRv6-Modellen leer ist.");
        }

        var highest = 0;
        foreach (var key in Singles.Keys)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var label) || label <= 0)
            {
                throw new InvalidDataException($"Codec-Schlüssel '{key}' ist keine positive Labelnummer.");
            }

            if (label > highest)
            {
                highest = label;
            }
        }

        var table = new string?[highest + 1];
        foreach (var (key, character) in Singles)
        {
            table[int.Parse(key, CultureInfo.InvariantCulture)] = character;
        }

        return table;
    }
}

/// <summary>One model variant (<c>tiny</c>, <c>small</c>, <c>medium</c>) with the files exported for it.</summary>
public sealed class Ppocrv6Variant
{
    [JsonPropertyName("variant")]
    public string? Variant { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    /// <summary>Number of output labels, i.e. the blank plus the codec entries.</summary>
    [JsonPropertyName("numClasses")]
    public int NumClasses { get; init; }

    [JsonPropertyName("models")]
    public List<Ppocrv6ModelEntry>? Models { get; init; }
}

/// <summary>
/// One ONNX file. The benchmark data that also lives in the catalog (<c>texts</c>,
/// <c>meanConfidence</c>, <c>outputs</c>) is deliberately not deserialised: it documents how the file
/// was measured, and the runtime has no use for it.
/// </summary>
public sealed class Ppocrv6ModelEntry
{
    /// <summary>Stable id, e.g. <c>kraken-ppocrv6-small-fp16</c>. Becomes <see cref="RecognizerProfile.ModelId"/>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>File name of the ONNX model, e.g. <c>ppocrv6-small-fp16.onnx</c>.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("variant")]
    public string? Variant { get; init; }

    [JsonPropertyName("precision")]
    public string? Precision { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    /// <summary>SHA-256 of the ONNX file, lower-case hex. Verified before the model is loaded.</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }
}

/// <summary>
/// Source-generated serialisation. Not a micro-optimisation: the head project is a trimmed, AOT-built
/// iOS app, where reflection-based <c>System.Text.Json</c> either needs a trimmer root or fails at
/// runtime - and a failure there means no text recognition on the device it was built for.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(Ppocrv6Catalog))]
internal sealed partial class Ppocrv6JsonContext : JsonSerializerContext;
