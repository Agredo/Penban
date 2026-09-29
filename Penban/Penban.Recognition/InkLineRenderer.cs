using System.Collections.Frozen;
using Penban.Models;

namespace Penban.Recognition;

/// <summary>
/// Draws one text line into the grayscale image a line-image recognizer reads.
/// <para>
/// A recognizer trained on scans does not read the ink, it reads a picture of the ink, and it only
/// reads that picture correctly at the size and density it was trained on. So the line is not scaled
/// to fill the model's input height - it is scaled so that its ink covers a fixed <em>fraction</em> of
/// that height, which is the fill ratio the convention names. Phase 0 measured a real spread here:
/// the same model on the same ink scored 0.0707 CER at <c>r81</c> and clearly worse at <c>r50</c>.
/// The ratio is therefore part of the recognizer's identity, not a tuning knob.
/// </para>
/// <para>
/// Everything is drawn at <see cref="Supersample"/> times the final size and then filtered down. The
/// strokes are stamped as hard-edged pixels - there is no anti-aliasing in the rasterizer, because a
/// stroke is a shape, not a gradient - so the smoothing has to happen in the downscale, where it
/// averages the shape's coverage instead of blurring its edges.
/// </para>
/// </summary>
public static class InkLineRenderer
{
    /// <summary>Height of the image handed to the model.</summary>
    public const int ModelHeight = 96;

    /// <summary>
    /// White margin added on both sides. The model's receptive field extends past the ink, so a line
    /// drawn edge-to-edge would have its first and last character read against a hard boundary the
    /// training data never contained.
    /// </summary>
    public const int HorizontalPadding = 16;

    /// <summary>Drawing scale relative to the final image.</summary>
    public const int Supersample = 3;

    private const int LanczosSupport = 3;

    /// <summary>
    /// Ink height as a fraction of the input height, per render convention. The names are the ones
    /// Phase 0 measured under, and they are persisted in <see cref="RecognizerProfile"/>, so they are
    /// data rather than something to rename.
    /// </summary>
    private static readonly FrozenDictionary<string, float> FillRatios =
        new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["r50"] = 0.50f,
            ["r81"] = 0.806f,
            ["r100"] = 1.00f,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Looks up the ink fill ratio of a render convention.</summary>
    public static bool TryGetFillRatio(string renderConvention, out float fillRatio)
    {
        ArgumentNullException.ThrowIfNull(renderConvention);
        return FillRatios.TryGetValue(renderConvention, out fillRatio);
    }

    /// <summary>Renders a line under the named convention.</summary>
    public static InkLineImage Render(InkLine line, string renderConvention)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (!TryGetFillRatio(renderConvention, out var fillRatio))
        {
            throw new ArgumentException(
                $"Unknown render convention '{renderConvention}'.", nameof(renderConvention));
        }

        return Render(line.Strokes, line.Bounds, fillRatio);
    }

    /// <summary>Renders a line whose ink covers <paramref name="fillRatio"/> of the input height.</summary>
    public static InkLineImage Render(IReadOnlyList<InkStroke> strokes, InkBounds bounds, float fillRatio)
    {
        ArgumentNullException.ThrowIfNull(strokes);

        var inkHeight = MathF.Max(bounds.MaxY - bounds.MinY, 1f);
        var inkWidth = MathF.Max(bounds.MaxX - bounds.MinX, 1f);

        // Scale the ink to the fraction of the input height it should cover, then centre it there.
        var documentHeight = inkHeight / fillRatio;
        var scale = ModelHeight / documentHeight;
        var top = ((bounds.MinY + bounds.MaxY) / 2f) - (documentHeight / 2f);

        var contentWidth = Math.Max(1, (int)MathF.Round(inkWidth * scale));
        var highWidth = Math.Max(1, contentWidth * Supersample);
        var highHeight = ModelHeight * Supersample;

        var mask = new byte[highWidth * highHeight];
        var up = scale * Supersample;
        var offsetX = -bounds.MinX * up;
        var offsetY = -top * up;

        foreach (var stroke in strokes)
        {
            var thickness = MathF.Max(1f, MathF.Round(stroke.Thickness * up));
            InkRasterizer.StampStroke(mask, highWidth, highHeight, stroke, up, offsetX, offsetY, thickness);
        }

        for (var i = 0; i < mask.Length; i++)
        {
            mask[i] = mask[i] == InkRasterizer.Ink ? (byte)0 : (byte)255;
        }

        var content = Resample(mask, highWidth, highHeight, contentWidth, ModelHeight);

        var width = contentWidth + (2 * HorizontalPadding);
        var gray = new byte[width * ModelHeight];
        Array.Fill(gray, (byte)255);
        for (var y = 0; y < ModelHeight; y++)
        {
            Array.Copy(content, y * contentWidth, gray, (y * width) + HorizontalPadding, contentWidth);
        }

        return new InkLineImage(gray, width, ModelHeight, contentWidth);
    }

    /// <summary>
    /// Separable Lanczos-3 resample, horizontal then vertical.
    /// <para>
    /// The two passes each round to a byte rather than carrying intermediate precision, because that
    /// is what the reference implementation does and this image is compared against its output. The
    /// difference is at most one level on a handful of pixels, but keeping the passes identical means
    /// any future mismatch is a real bug instead of a rounding artefact to be argued away.
    /// </para>
    /// </summary>
    private static byte[] Resample(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (sourceWidth == targetWidth && sourceHeight == targetHeight)
        {
            return (byte[])source.Clone();
        }

        var horizontal = new byte[targetWidth * sourceHeight];
        for (var x = 0; x < targetWidth; x++)
        {
            var (start, weights) = BuildWeights(sourceWidth, targetWidth, x);
            for (var y = 0; y < sourceHeight; y++)
            {
                var row = y * sourceWidth;
                var sum = 0.0;
                for (var i = 0; i < weights.Length; i++)
                {
                    sum += weights[i] * source[row + start + i];
                }

                horizontal[(y * targetWidth) + x] = ToByte(sum);
            }
        }

        var result = new byte[targetWidth * targetHeight];
        for (var y = 0; y < targetHeight; y++)
        {
            var (start, weights) = BuildWeights(sourceHeight, targetHeight, y);
            for (var x = 0; x < targetWidth; x++)
            {
                var sum = 0.0;
                for (var i = 0; i < weights.Length; i++)
                {
                    sum += weights[i] * horizontal[((start + i) * targetWidth) + x];
                }

                result[(y * targetWidth) + x] = ToByte(sum);
            }
        }

        return result;
    }

    /// <summary>
    /// The input window and normalised weights for one output pixel. The window widens as the
    /// downscale factor grows, so a large reduction averages over proportionally more input - which
    /// is the whole point of resampling rather than picking the nearest pixel.
    /// </summary>
    private static (int Start, double[] Weights) BuildWeights(int sourceSize, int targetSize, int outputIndex)
    {
        var scale = (double)sourceSize / targetSize;
        var support = LanczosSupport * scale;
        var center = (outputIndex + 0.5) * scale;

        var start = (int)(center - support + 0.5);
        if (start < 0)
        {
            start = 0;
        }

        var end = (int)(center + support + 0.5);
        if (end > sourceSize)
        {
            end = sourceSize;
        }

        if (end <= start)
        {
            start = Math.Clamp((int)center, 0, sourceSize - 1);
            end = start + 1;
        }

        var weights = new double[end - start];
        var sum = 0.0;
        for (var i = start; i < end; i++)
        {
            var weight = Lanczos((i - center + 0.5) / scale);
            weights[i - start] = weight;
            sum += weight;
        }

        if (sum != 0.0)
        {
            for (var i = 0; i < weights.Length; i++)
            {
                weights[i] /= sum;
            }
        }

        return (start, weights);
    }

    private static double Lanczos(double x)
    {
        var distance = Math.Abs(x);
        if (distance >= LanczosSupport)
        {
            return 0.0;
        }

        if (distance == 0.0)
        {
            return 1.0;
        }

        return Sinc(distance) * Sinc(distance / LanczosSupport);
    }

    private static double Sinc(double x)
    {
        var scaled = Math.PI * x;
        return Math.Sin(scaled) / scaled;
    }

    private static byte ToByte(double value)
    {
        var rounded = (int)(value + 0.5);
        return (byte)Math.Clamp(rounded, 0, 255);
    }
}
