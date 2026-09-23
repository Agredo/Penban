namespace Penban.Models;

/// <summary>
/// The pen widths the app offers, in document units - the same unit <see cref="InkDocument"/> uses,
/// so a width means the same thing on a phone and on a tablet.
/// <para>
/// The ladder is deliberately short: it is meant to be walked through with three fingers sliding
/// over the note, so every rung has to be felt rather than read off a number.
/// </para>
/// </summary>
public static class PenThickness
{
    /// <summary>Width a pen starts with when nothing has been chosen yet.</summary>
    public const float Default = 4f;

    /// <summary>The widths on offer, from a hairline to a marker.</summary>
    public static IReadOnlyList<float> Presets { get; } = [2.5f, 4f, 7f, 11f, 16f];

    /// <summary>Number of widths on the ladder.</summary>
    public static int Count => Presets.Count;

    /// <summary>Keeps a stored width inside the range the picker can show.</summary>
    public static float Clamp(float thickness) =>
        Math.Clamp(thickness, Presets[0], Presets[Count - 1]);

    /// <summary>Keeps an index inside the ladder.</summary>
    public static int ClampIndex(int index) => Math.Clamp(index, 0, Count - 1);

    /// <summary>Width at <paramref name="index"/>, clamped to the ladder.</summary>
    public static float ValueAt(int index) => Presets[ClampIndex(index)];

    /// <summary>Index of the width closest to <paramref name="thickness"/>.</summary>
    public static int NearestIndex(float thickness)
    {
        var nearest = 0;
        var shortest = float.MaxValue;

        for (var index = 0; index < Count; index++)
        {
            var distance = Math.Abs(Presets[index] - thickness);
            if (distance < shortest)
            {
                shortest = distance;
                nearest = index;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Moves <paramref name="steps"/> rungs up or down the ladder, stopping at either end: running
    /// out of ladder says more than jumping to the opposite extreme.
    /// </summary>
    public static int StepIndex(int index, int steps) => ClampIndex(index + steps);
}
