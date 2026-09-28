using Penban.Models;

namespace Penban.Recognition;

/// <summary>
/// Groups the strokes of one card into text lines, top to bottom.
/// <para>
/// This is a direct port of the segmentation that was measured in the Phase 0 benchmark, so the line
/// boxes match the ones the model was evaluated on. The approach is deliberately geometric rather
/// than model-driven: the recognizer has no layout detection of its own (it is a line model and sees
/// exactly one line at a time), so finding the lines is entirely this class's job. In the benchmark
/// it was the weakest part of the pipeline - free-standing drawings and multi-column sketches are
/// segmented into several "lines" - which is why it is kept in one small, testable place.
/// </para>
/// <para>
/// The algorithm:
/// <list type="number">
/// <item>rasterise the ink at document resolution and count the ink pixels per row;</item>
/// <item>cut the rows into maximal runs that carry ink ("bands");</item>
/// <item>move short bands (i-dots, periods, accents) into the nearest tall band;</item>
/// <item>split a band that is far taller than one text line at its deepest interior valley;</item>
/// <item>assign every stroke to the band it overlaps most, and drop bands with fewer than two
/// strokes.</item>
/// </list>
/// </para>
/// </summary>
public static class InkLineSegmenter
{
    /// <summary>Document space is square; the row profile is built at one pixel per document unit.</summary>
    private const int ProfileSize = (int)InkDocument.Size;

    /// <summary>A band this short is a dot or an accent, never a line of its own.</summary>
    private const float DotMaxHeight = 14f;

    /// <summary>A band taller than this many line heights may hold more than one line.</summary>
    private const float SplitRatio = 1.9f;

    /// <summary>A text line is about this many median stroke heights tall.</summary>
    private const float LineHeightFactor = 2.2f;

    /// <summary>Fraction of the band height the deepest valley has to fall below to count as a gap.</summary>
    private const float ValleyThreshold = 0.35f;

    /// <summary>How far into a band a split is allowed to look, as a fraction of the band height.</summary>
    private const float SplitInset = 0.20f;

    /// <summary>A line needs at least this many strokes; a lone stroke is treated as a drawing.</summary>
    private const int MinStrokesPerLine = 2;

    /// <summary>
    /// Returns the text lines of <paramref name="strokes"/>, ordered top to bottom. Bands that hold
    /// fewer than two strokes are left out, which is what keeps single doodle strokes from becoming
    /// lines.
    /// </summary>
    public static IReadOnlyList<InkLine> Segment(IReadOnlyList<InkStroke> strokes)
    {
        if (strokes.Count == 0)
        {
            return [];
        }

        var profile = BuildRowProfile(strokes);

        var bodies = new List<(int Top, int Bottom)>();
        var dots = new List<(int Top, int Bottom)>();

        void AddBand(int top, int bottom)
        {
            if (bottom - top + 1 > DotMaxHeight)
            {
                bodies.Add((top, bottom));
            }
            else
            {
                dots.Add((top, bottom));
            }
        }

        // Raw bands are the maximal runs of consecutive rows that carry ink.
        var bandStart = -1;
        for (var y = 0; y < ProfileSize; y++)
        {
            if (profile[y] > 0)
            {
                if (bandStart < 0)
                {
                    bandStart = y;
                }
            }
            else if (bandStart >= 0)
            {
                AddBand(bandStart, y - 1);
                bandStart = -1;
            }
        }

        if (bandStart >= 0)
        {
            AddBand(bandStart, ProfileSize - 1);
        }

        if (bodies.Count == 0 && dots.Count == 0)
        {
            return [];
        }

        // Estimate the height of a single text line from the strokes, not from the bands: one stroke
        // that bridges two lines merges their bands, which inflates a band-based estimate and stops
        // the split it was supposed to trigger.
        var strokeHeights = new List<float>(strokes.Count);
        foreach (var stroke in strokes)
        {
            if (TryGetVerticalExtent(stroke, out var minY, out var maxY))
            {
                strokeHeights.Add(maxY - minY);
            }
        }

        if (strokeHeights.Count == 0)
        {
            return [];
        }

        strokeHeights.Sort();
        var lineHeight = MathF.Max(LineHeightFactor * Median(strokeHeights), DotMaxHeight);

        if (bodies.Count > 0)
        {
            var tallestBody = 0;
            foreach (var (top, bottom) in bodies)
            {
                tallestBody = Math.Max(tallestBody, bottom - top + 1);
            }

            lineHeight = MathF.Min(lineHeight, tallestBody);
        }

        foreach (var (top, bottom) in dots)
        {
            if (bodies.Count == 0)
            {
                bodies.Add((top, bottom));
                continue;
            }

            var centre = (top + bottom) / 2f;
            var nearest = 0;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < bodies.Count; i++)
            {
                var distance = MathF.Min(
                    MathF.Abs(centre - bodies[i].Top),
                    MathF.Abs(centre - bodies[i].Bottom));

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = i;
                }
            }

            var merged = bodies[nearest];
            bodies[nearest] = (Math.Min(merged.Top, top), Math.Max(merged.Bottom, bottom));
        }

        var bands = new List<(int Top, int Bottom)>();
        foreach (var body in bodies)
        {
            SplitBand(profile, body.Top, body.Bottom, lineHeight, bands);
        }

        bands.Sort();

        var buckets = new List<InkStroke>[bands.Count];
        for (var i = 0; i < buckets.Length; i++)
        {
            buckets[i] = [];
        }

        foreach (var stroke in strokes)
        {
            if (!TryGetVerticalExtent(stroke, out var minY, out var maxY))
            {
                continue;
            }

            var best = 0;
            var bestOverlap = float.NegativeInfinity;
            for (var i = 0; i < bands.Count; i++)
            {
                var overlap = MathF.Min(maxY, bands[i].Bottom) - MathF.Max(minY, bands[i].Top);

                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    best = i;
                }
            }

            buckets[best].Add(stroke);
        }

        var lines = new List<InkLine>();
        foreach (var bucket in buckets)
        {
            if (bucket.Count < MinStrokesPerLine)
            {
                continue;
            }

            if (!InkDocument.TryGetBounds(bucket, out var bounds))
            {
                continue;
            }

            lines.Add(new InkLine(bucket, bounds));
        }

        return lines;
    }

    /// <summary>
    /// Splits a tall band at its deepest interior valley, recursing while the halves are still taller
    /// than a text line. A valley that is not deep enough is not a gap between two lines - it is the
    /// waist of a single line, which is why the cut has to fall well below the band's mean ink.
    /// </summary>
    private static void SplitBand(
        int[] profile,
        int top,
        int bottom,
        float lineHeight,
        List<(int Top, int Bottom)> into)
    {
        var height = bottom - top + 1;
        if (height <= SplitRatio * lineHeight)
        {
            into.Add((top, bottom));
            return;
        }

        var low = top + (int)(SplitInset * height);
        var high = bottom - (int)(SplitInset * height);
        if (high <= low)
        {
            into.Add((top, bottom));
            return;
        }

        var cut = low;
        for (var y = low + 1; y < high; y++)
        {
            if (profile[y] < profile[cut])
            {
                cut = y;
            }
        }

        var sum = 0L;
        for (var y = top; y <= bottom; y++)
        {
            sum += profile[y];
        }

        var mean = (float)sum / height;
        if (mean <= 0f)
        {
            mean = 1f;
        }

        if (profile[cut] > ValleyThreshold * mean)
        {
            into.Add((top, bottom));
            return;
        }

        SplitBand(profile, top, cut, lineHeight, into);
        SplitBand(profile, cut + 1, bottom, lineHeight, into);
    }

    /// <summary>
    /// Counts the ink pixels of every row of the document. Overlapping strokes are counted once,
    /// because the counts decide whether a row carries ink at all and how deep a valley is; a stroke
    /// drawn twice over the same pixels must not look like two rows of ink.
    /// </summary>
    private static int[] BuildRowProfile(IReadOnlyList<InkStroke> strokes)
    {
        var pixels = new byte[ProfileSize * ProfileSize];
        foreach (var stroke in strokes)
        {
            InkRasterizer.StampStroke(pixels, ProfileSize, ProfileSize, stroke, 1f, 0f, 0f);
        }

        var profile = new int[ProfileSize];
        for (var y = 0; y < ProfileSize; y++)
        {
            var count = 0;
            var row = y * ProfileSize;
            for (var x = 0; x < ProfileSize; x++)
            {
                count += pixels[row + x];
            }

            profile[y] = count;
        }

        return profile;
    }

    /// <summary>
    /// Median of an already sorted list. For an even count this is the mean of the two middle values,
    /// matching the reference implementation.
    /// </summary>
    private static float Median(List<float> sorted)
    {
        var count = sorted.Count;
        if (count == 0)
        {
            return 0f;
        }

        var middle = count / 2;
        return count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2f;
    }

    /// <summary>Vertical extent of a single stroke, the measure a line height is estimated from.</summary>
    private static bool TryGetVerticalExtent(InkStroke stroke, out float minY, out float maxY)
    {
        minY = float.MaxValue;
        maxY = float.MinValue;

        foreach (var point in stroke.Points)
        {
            if (point.Y < minY)
            {
                minY = point.Y;
            }

            if (point.Y > maxY)
            {
                maxY = point.Y;
            }
        }

        return minY <= maxY;
    }
}
