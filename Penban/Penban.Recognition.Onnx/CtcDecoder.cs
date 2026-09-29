using System.Text;

namespace Penban.Recognition.Onnx;

/// <summary>One decoded line: the text and how confident the model was about it.</summary>
/// <param name="Text">
/// The characters the codec emitted, in order. May be empty when the model saw nothing it could read.
/// </param>
/// <param name="MeanConfidence">
/// Mean confidence over the emitted time steps, <c>0</c> when nothing was emitted. Blank steps are
/// left out rather than counted as zero: a blank step is the model saying "no character here", and
/// averaging those in would make a short word on a wide line look less reliable than the same word on
/// a narrow one.
/// </param>
public readonly record struct CtcDecodeResult(string Text, float MeanConfidence);

/// <summary>
/// Collapses the model's per-time-step labels into a string.
/// <para>
/// The graph already does the softmax, the argmax and the confidence, so what is left is the CTC
/// collapse: a character is emitted only where the label changes, and the blank label is dropped. The
/// "only where the label changes" rule is what turns <c>a a a _ a</c> into <c>aa</c> rather than
/// <c>aaa</c> - a doubled letter survives because the blank separates the two runs, while the same
/// letter held across several time steps does not.
/// </para>
/// <para>
/// The confidences are read at the emitted step, i.e. the first step of each run, because that is the
/// step the graph scored as the character.
/// </para>
/// </summary>
public static class CtcDecoder
{
    /// <summary>Emitted for a label the codec does not know, so a wrong table shows up as text instead of an exception.</summary>
    public const string UnknownCharacter = "\uFFFD";

    /// <summary>
    /// Decodes one sequence. <paramref name="labelTable"/> is indexed by label and has <c>null</c> at
    /// the blank index; see <see cref="Ppocrv6Codec.LabelTable"/>.
    /// </summary>
    public static CtcDecodeResult Decode(
        ReadOnlySpan<long> labels,
        ReadOnlySpan<float> confidences,
        IReadOnlyList<string?> labelTable)
    {
        ArgumentNullException.ThrowIfNull(labelTable);

        if (confidences.Length < labels.Length)
        {
            throw new ArgumentException(
                $"Es gibt {labels.Length} Labels, aber nur {confidences.Length} Konfidenzen.",
                nameof(confidences));
        }

        var text = new StringBuilder(labels.Length);
        var sum = 0f;
        var emitted = 0;

        // -1 rather than 0, so a sequence that starts on the blank does not treat the initial state as
        // a repeated label and swallow a real character.
        var previous = -1L;

        for (var step = 0; step < labels.Length; step++)
        {
            var label = labels[step];

            if (label == previous)
            {
                continue;
            }

            previous = label;

            // The CTC blank: a real step, but not a character.
            if (label == 0)
            {
                continue;
            }

            // A label outside the table means the codec does not match the model. Emitting the
            // replacement character keeps the text readable and the mistake visible, where throwing
            // would lose the whole card over one label.
            text.Append(label > 0 && label < labelTable.Count
                ? labelTable[(int)label] ?? UnknownCharacter
                : UnknownCharacter);

            sum += confidences[step];
            emitted++;
        }

        return new CtcDecodeResult(text.ToString(), emitted == 0 ? 0f : sum / emitted);
    }
}
