namespace Penban.Models;

/// <summary>
/// A single sampled point of an <see cref="InkStroke"/>, including pressure and timing
/// so different ink renderers can be compared for latency and writing feel.
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

    public long TimestampMs { get; set; }
}
