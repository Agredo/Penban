using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Skia;

namespace Penban.Maui.Views.Widget;

/// <summary>
/// Draws the previews of one board: three widget sizes, each in a light and a dark version.
/// <para>
/// The widget shows these images instead of drawing the card itself, which keeps a second renderer of
/// the app's card - with its fonts, its palette and its ink - out of the extension. Drawing happens
/// here, in the app, where the card already exists.
/// </para>
/// </summary>
public static class WidgetImageRenderer
{
    /// <summary>
    /// Render factor of the exported images. The widget sizes are logical points, so a home screen
    /// with three times as many pixels needs three times the pixels to look sharp - and a widget is
    /// small enough that the extra pixels cost nothing worth mentioning.
    /// </summary>
    public const float DisplayScale = 3f;

    /// <summary>The widget sizes, in the order <see cref="WidgetImages"/> names them.</summary>
    private static readonly WidgetCardKind[] Kinds =
    [
        WidgetCardKind.Square,
        WidgetCardKind.Wide,
        WidgetCardKind.Tall,
    ];

    /// <summary>
    /// Draws all six previews of a board into <paramref name="directory"/> and returns the file names
    /// the snapshot has to carry for them.
    /// </summary>
    public static WidgetImages Render(WidgetBoard board, string directory)
    {
        foreach (var palette in WidgetPalette.All)
        {
            foreach (var kind in Kinds)
            {
                Render(board, palette, kind, Path.Combine(directory, WidgetImages.FileName(board.Id, kind, palette.IsDark)));
            }
        }

        return WidgetImages.For(board.Id);
    }

    /// <summary>Draws one preview and writes it to <paramref name="file"/>.</summary>
    public static void Render(WidgetBoard board, WidgetPalette palette, WidgetCardKind kind, string file)
    {
        var size = WidgetCardDrawable.SizeOf(kind);
        var width = MathF.Round(size.Width * DisplayScale);
        var height = MathF.Round(size.Height * DisplayScale);

        // The context gets the full pixel size and a display scale of one: the drawing scales its own
        // design into whatever rect it is handed, so the image comes out at the render factor without
        // depending on whether the export context scales its canvas - one of the two would double it.
        using var context = new SkiaBitmapExportContext((int)width, (int)height, 1f);

        new WidgetCardDrawable(board, palette, kind).Draw(context.Canvas, new RectF(0f, 0f, width, height));
        context.WriteToFile(file);
    }
}
