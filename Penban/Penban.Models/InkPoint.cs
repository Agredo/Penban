namespace Penban.Models;

/// <summary>
/// A single sampled point of an <see cref="InkStroke"/>, including pressure, pen tilt and timing
/// so different ink renderers can be compared for latency and writing feel.
/// <para>
/// Coordinates are relative to the fixed square document described by <see cref="InkDocument"/>,
/// never to the pixels of the surface the stroke was captured on.
/// </para>
/// <para>
/// Members are properties rather than fields because the persistence layer (LiteDB)
/// maps public properties by default and would otherwise drop the coordinate data.
/// </para>
/// </summary>
public struct InkPoint
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Pressure { get; set; }

    /// <summary>
    /// How far the pen is tilted away from the surface, in radians: <c>0</c> when the pen stands
    /// upright, up to <c>pi / 2</c> when it lies flat. Only styluses that report tilt fill this in;
    /// it stays <c>0</c> for fingers, mice and pens without a tilt sensor, so strokes captured before
    /// tilt support existed read as "upright" without needing a conversion.
    /// </summary>
    public float Tilt { get; set; }

    /// <summary>
    /// Direction the pen leans towards, in radians, measured the way each platform reports it. Only
    /// meaningful together with a non-zero <see cref="Tilt"/>; <c>0</c> means "unknown" and is treated
    /// as pointing right.
    /// </summary>
    public float Azimuth { get; set; }

    public long TimestampMs { get; set; }
}
