using System.Globalization;

namespace Penban.Models;

/// <summary>
/// How wide a pen writes, in document units - the same unit <see cref="InkDocument"/> uses, so a width
/// means the same thing on a phone and on a tablet.
/// <para>
/// The width is a continuous value rather than a ladder of presets: it is set on a slider in the pen
/// flyout and by sliding a finger along the ring's width block, and both of those walk through the
/// range in small steps instead of jumping between a handful of widths.
/// </para>
/// </summary>
public static class PenThickness
{
    /// <summary>
    /// Thinnest pen. Not zero: with pressure-sensitive width a stroke is drawn down to
    /// <c>MinPressureWidthShare</c> (0.4) of its width at the lightest touch, so a pen thinner than
    /// this would come out as a broken line wherever the pen was lifted a little.
    /// </summary>
    public const float Minimum = 1.5f;

    /// <summary>Widest pen: a marker.</summary>
    public const float Maximum = 16f;

    /// <summary>Width a pen starts with when nothing has been chosen yet.</summary>
    public const float Default = 4f;

    /// <summary>Step the flyout's slider walks in, in document units.</summary>
    public const float SliderStep = 0.1f;

    /// <summary>
    /// Step the ring's width block walks in: coarser than the slider, because it is picked by sliding
    /// a finger across a wedge rather than by dragging a handle.
    /// </summary>
    public const float RadialStep = 0.5f;

    /// <summary>Keeps a stored width inside the range a pen can be set to.</summary>
    public static float Clamp(float thickness) => Math.Clamp(thickness, Minimum, Maximum);

    /// <summary>Rounds a width onto the nearest step, and keeps it inside the range.</summary>
    public static float Snap(float thickness, float step) =>
        Clamp(Minimum + (MathF.Round((thickness - Minimum) / step) * step));

    /// <summary>Number of steps a block of the given step offers, ends included.</summary>
    public static int Steps(float step) => (int)MathF.Round((Maximum - Minimum) / step) + 1;

    /// <summary>The width at <paramref name="index"/> of a block of the given step.</summary>
    public static float ValueAt(int index, float step) => Clamp(Minimum + (index * step));

    /// <summary>Index of the width closest to <paramref name="thickness"/> in a block of the given step.</summary>
    public static int NearestIndex(float thickness, float step) =>
        (int)MathF.Round((Clamp(thickness) - Minimum) / step);

    /// <summary>
    /// The width as it is read out to the user, in the current culture's decimal mark. The unit is not
    /// part of it - that is the resource string it is put into.
    /// </summary>
    public static string Format(float thickness) =>
        thickness.ToString("0.0", CultureInfo.CurrentCulture);
}
