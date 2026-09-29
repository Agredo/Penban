namespace Penban.Recognition;

/// <summary>
/// The text found on one card.
/// <para>
/// <see cref="Profile"/> travels with the result because the text is only meaningful relative to how
/// it was produced. A different render convention, a different codec or a different model can all
/// change the output for identical ink, so the profile - not just the model - is part of the cache
/// key. Without it, changing the renderer would leave every stored text looking valid while being
/// silently wrong.
/// </para>
/// </summary>
public sealed class InkRecognitionResult
{
    public InkRecognitionResult(IReadOnlyList<InkLineResult> lines, RecognizerProfile profile)
    {
        Lines = lines;
        Profile = profile;
    }

    /// <summary>Lines in reading order, top to bottom.</summary>
    public IReadOnlyList<InkLineResult> Lines { get; }

    public RecognizerProfile Profile { get; }

    /// <summary>
    /// All lines joined with a space, skipping lines that produced no text. Convenient for a quick
    /// preview or a single-column search index; the per-line results remain the source of truth.
    /// </summary>
    public string ToPlainText()
    {
        var parts = new List<string>(Lines.Count);
        foreach (var line in Lines)
        {
            if (!string.IsNullOrWhiteSpace(line.Text))
            {
                parts.Add(line.Text.Trim());
            }
        }

        return string.Join(' ', parts);
    }
}
