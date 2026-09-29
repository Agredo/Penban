namespace Penban.Recognition;

/// <summary>
/// The conventions under which a recognizer produced its text, as one comparable value.
/// <para>
/// This exists because text is only valid relative to how it was made. The Phase 0 benchmark measured
/// a real spread across render conventions: the same model on the same ink scored 0.0707 CER at r81
/// and clearly worse at r50, so the fill ratio is a contract, not a tuning knob. Changing it would
/// leave every cached text looking valid while being wrong - hence the profile is part of the cache
/// key and is persisted with the text.
/// </para>
/// </summary>
public readonly record struct RecognizerProfile(
    string ModelId,
    string RenderConvention,
    string UnicodeForm)
{
    /// <summary>
    /// The convention the shipping model was measured under: kraken PP-OCRv6 small, fp16 weights,
    /// lines rendered at an 81 % fill of the 96 px input height, text normalised to NFC.
    /// </summary>
    public static RecognizerProfile Ppocrv6SmallFp16 { get; } =
        new("kraken-ppocrv6-small-fp16", "r81", "nfc");

    /// <summary>
    /// Stable, file-system- and database-safe form, used in the cache key. Fixed rather than derived
    /// from <c>ToString()</c> so a rename of the record's members cannot silently invalidate or, worse,
    /// collide with stored rows.
    /// </summary>
    public override string ToString() =>
        $"{ModelId}/{RenderConvention}/{UnicodeForm}";
}
