namespace Penban.Recognition;

/// <summary>
/// A rendered text line as the grayscale image a line-image recognizer reads, before it becomes a
/// tensor.
/// <para>
/// The pixels are kept in the convention they were drawn in - <c>255</c> is paper, <c>0</c> is ink,
/// as in a scan - and inverted only on the way into the model. Rendering into an inverted image
/// directly would look like a trivial saving, but it makes the image useless for the one thing it is
/// most needed for: showing the user what the recognizer saw when it produced a wrong line. A preview
/// that is white-on-black has to be un-inverted again, and every place that does it is a chance to
/// get it wrong once.
/// </para>
/// </summary>
public sealed class InkLineImage
{
    private readonly byte[] _gray;

    internal InkLineImage(byte[] gray, int width, int height, int contentWidth)
    {
        _gray = gray;
        Width = width;
        Height = height;
        ContentWidth = contentWidth;
    }

    /// <summary>Row-major grayscale, <see cref="Width"/> * <see cref="Height"/>, 255 paper / 0 ink.</summary>
    public ReadOnlySpan<byte> Gray => _gray;

    /// <summary>Total width, including the white margin the model expects on both sides.</summary>
    public int Width { get; }

    public int Height { get; }

    /// <summary>Width of the drawn ink, i.e. <see cref="Width"/> without the margin.</summary>
    public int ContentWidth { get; }

    /// <summary>
    /// The model input: three identical channels, inverted so ink is <c>1</c> and paper <c>0</c>,
    /// laid out channel-major as <c>[channel][y][x]</c>.
    /// </summary>
    public float[] ToModelInput()
    {
        const int channels = 3;
        var plane = Width * Height;
        var input = new float[channels * plane];

        for (var i = 0; i < plane; i++)
        {
            var value = 1f - (_gray[i] / 255f);
            input[i] = value;
            input[plane + i] = value;
            input[(2 * plane) + i] = value;
        }

        return input;
    }
}
