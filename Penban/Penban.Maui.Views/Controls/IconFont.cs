using SkiaSharp;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Glyph constants for the Fluent System Icons font (registered as "FluentIcons" in the head app).
/// Codepoints match FluentSystemIcons-Regular.ttf from microsoft/fluentui-system-icons.
/// <para>
/// The same font is also what draws the radial menu, which is painted on a canvas rather than built
/// from labels and so cannot reach it through the font family the head app registers. The file is
/// opened from the app package instead and turned into a <see cref="SKTypeface"/> here, once for the
/// whole app - see <see cref="GetTypefaceAsync"/>.
/// </para>
/// </summary>
public static class IconFont
{
    public const string Add = "\uf10a";
    public const string Back = "\uf15c";
    public const string Dismiss = "\uf36a";
    public const string Delete = "\uf34d";
    public const string DragHandle = "\ue9f9";
    public const string Board = "\uf1e3";
    public const string Checkmark = "\uf295";
    public const string Edit = "\uf3de";
    public const string Notebook = "\uf570";
    public const string Settings = "\uf588";

    /// <summary>Pen colours, as offered by the radial menu's colour block.</summary>
    public const string Color = "\uf2f6";

    /// <summary>Pen widths, as offered by the radial menu's width block.</summary>
    public const string LineThickness = "\uf0062";

    /// <summary>Back a step: the radial menu's "undo".</summary>
    public const string Undo = "\uf19a";

    /// <summary>Forward a step: the radial menu's "redo".</summary>
    public const string Redo = "\uf16f";

    /// <summary>The pen draws and a finger does not.</summary>
    public const string Pen = "\ue8d8";

    /// <summary>The pen rubs out instead of drawing.</summary>
    public const string Eraser = "\ue5e5";

    /// <summary>A finger draws as well as the pen.</summary>
    public const string Finger = "\ue6d8";

    /// <summary>The radial menu itself: the rings of the menu as a picture of them.</summary>
    public const string RadialMenu = "\uf07a5";

    /// <summary>Name of the font file inside the app package, next to its registration in the head app.</summary>
    private const string TypefaceFile = "FluentSystemIcons-Regular.ttf";

    private static readonly SemaphoreSlim TypefaceGate = new(1, 1);
    private static SKTypeface? typeface;

    /// <summary>
    /// The icon font once <see cref="GetTypefaceAsync"/> has brought it in, and <c>null</c> until
    /// then or if it could not be opened at all. Painting happens on a canvas callback that cannot
    /// wait, so whoever paints reads this and falls back to something else while it is still empty.
    /// </summary>
    public static SKTypeface? Typeface => typeface;

    /// <summary>
    /// The icon font as something Skia can draw with, loaded on first use and kept for the rest of
    /// the session. Returns <c>null</c> only if the font cannot be opened at all, which is what a
    /// caller falls back on instead of drawing nothing.
    /// </summary>
    public static async Task<SKTypeface?> GetTypefaceAsync()
    {
        if (typeface is not null)
        {
            return typeface;
        }

        await TypefaceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (typeface is not null)
            {
                return typeface;
            }

            using var file = await FileSystem.OpenAppPackageFileAsync(TypefaceFile).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer).ConfigureAwait(false);
            buffer.Position = 0;

            // Skia keeps its own copy of the data, so the stream can go once it has been read.
            typeface = SKTypeface.FromStream(buffer, 0);
            return typeface;
        }
        catch (Exception)
        {
            // A missing font is a menu without icons, not a menu that cannot be opened - so the
            // answer is "no typeface" rather than a crash on the drawing thread.
            return null;
        }
        finally
        {
            TypefaceGate.Release();
        }
    }
}
