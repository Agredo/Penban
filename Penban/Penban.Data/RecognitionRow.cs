using Penban.Recognition;

namespace Penban.Data;

/// <summary>
/// The stored shape of a <see cref="StoredRecognition"/>, kept apart from it for two reasons.
/// <para>
/// LiteDB maps a type by convention - it wants a settable <c>Id</c> and a parameterless constructor -
/// and <see cref="StoredRecognition"/> is a positional record with neither. Changing the record to
/// suit the database would leak the storage choice into the recognition library, which deliberately
/// knows nothing about how its results are kept.
/// </para>
/// <para>
/// The profile is written as its three parts rather than as <see cref="RecognizerProfile.ToString"/>.
/// The joined form would have to be taken apart again on every read, and a model id that ever
/// contained a slash would silently split into the wrong fields.
/// </para>
/// </summary>
internal sealed class RecognitionRow
{
    /// <summary>The card the text belongs to. One row per card, so this is the row's id as well.</summary>
    public Guid Id { get; set; }

    public string InkHash { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    public string RenderConvention { get; set; } = string.Empty;

    public string UnicodeForm { get; set; } = string.Empty;

    public string RawText { get; set; } = string.Empty;

    public string NormalizedText { get; set; } = string.Empty;

    public float MeanConfidence { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public RecognizerProfile Profile => new(ModelId, RenderConvention, UnicodeForm);

    public static RecognitionRow From(StoredRecognition recognition) => new()
    {
        Id = recognition.CardId,
        InkHash = recognition.InkHash,
        ModelId = recognition.Profile.ModelId,
        RenderConvention = recognition.Profile.RenderConvention,
        UnicodeForm = recognition.Profile.UnicodeForm,
        RawText = recognition.RawText,
        NormalizedText = recognition.NormalizedText,
        MeanConfidence = recognition.MeanConfidence,
        UpdatedAtUtc = recognition.UpdatedAtUtc,
    };

    public StoredRecognition ToStored() => new(
        Id,
        InkHash,
        Profile,
        RawText,
        NormalizedText,
        MeanConfidence,
        // LiteDB gives a stored date back as Kind=Local. The instant is right, but the record calls
        // this member UpdatedAtUtc and its callers compare it with DateTime.UtcNow, so the kind is
        // restored here. Sub-millisecond ticks do not survive the round trip either, which is why
        // this value is only ever ordered by and shown, never compared for equality.
        UpdatedAtUtc.ToUniversalTime());

    /// <summary>
    /// Whether this row still describes the ink and the conventions the caller is asking about. Both
    /// parts matter: the same ink read under a different render convention is different text.
    /// </summary>
    public bool Matches(string inkHash, RecognizerProfile profile) =>
        string.Equals(InkHash, inkHash, StringComparison.Ordinal) && Profile == profile;
}
