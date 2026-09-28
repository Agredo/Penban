using Penban.Models;

namespace Penban.Recognition;

/// <summary>
/// The recognised text of one line, together with what is known about how reliable it is and where it
/// came from in the document.
/// <para>
/// <see cref="Text"/> is the raw model output, exactly as the codec emitted it. It is deliberately
/// not normalised: the raw form is what gets shown when the user asks why a card matched, and it is
/// what a later model or codec change has to be compared against. The searchable form is derived from
/// it by <see cref="TextNormalizer"/> and stored alongside.
/// </para>
/// </summary>
public sealed class InkLineResult
{
    public InkLineResult(string text, float meanConfidence, InkBounds bounds)
    {
        Text = text;
        MeanConfidence = meanConfidence;
        Bounds = bounds;
    }

    /// <summary>
    /// Raw decoded text, NFC, never <c>null</c> but possibly empty when the model emitted only blanks.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Mean confidence over the emitted (non-blank) time steps, <c>0</c> when nothing was emitted.
    /// <para>
    /// This is stored rather than used as a gate. A threshold looks attractive - the benchmark's
    /// handful of empty lines were drawings - but it cannot tell a doodle from a badly written word,
    /// so a gate would silently drop real text. Keeping the number lets the UI show or filter by it
    /// later, once there is evidence for where a threshold belongs.
    /// </para>
    /// </summary>
    public float MeanConfidence { get; }

    /// <summary>Bounding box of the line's strokes in document space.</summary>
    public InkBounds Bounds { get; }
}
