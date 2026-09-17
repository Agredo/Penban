using System.Collections.Specialized;
using System.Linq;
using Penban.Models;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Lightweight, read-only ink preview for board cards.
/// </summary>
public sealed class InkPreviewView : GraphicsView, IDrawable
{
    public static readonly BindableProperty StrokesSourceProperty = BindableProperty.Create(
        nameof(StrokesSource),
        typeof(IEnumerable<InkStroke>),
        typeof(InkPreviewView),
        default(IEnumerable<InkStroke>),
        propertyChanged: OnStrokesSourceChanged);

    /// <summary>A preview stroke never renders thinner than this many device-independent units,
    /// so a much-reduced note still shows its pen marks.</summary>
    private const float MinimumStrokeSize = 1f;

    /// <summary>…and never thicker than this share of the preview. Ink is fitted into the note by
    /// its bounding box, so a drawing that covers little of the sheet is scaled up a lot; without
    /// a ceiling its strokes would come out as thick marker lines on a note this small.</summary>
    private const float MaximumStrokeShare = 0.02f;

    private INotifyCollectionChanged? observedCollection;

    public InkPreviewView()
    {
        Drawable = this;
    }

    public IEnumerable<InkStroke>? StrokesSource
    {
        get => (IEnumerable<InkStroke>?)GetValue(StrokesSourceProperty);
        set => SetValue(StrokesSourceProperty, value);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (StrokesSource is null)
        {
            return;
        }

        var strokes = StrokesSource.Where(s => s.Points.Count > 0).ToList();
        if (strokes.Count == 0)
        {
            return;
        }

        var (minX, minY, maxX, maxY) = GetInkBounds(strokes);
        var sourceWidth = Math.Max(maxX - minX, 1f);
        var sourceHeight = Math.Max(maxY - minY, 1f);
        const float padding = 6f;
        var targetWidth = Math.Max(dirtyRect.Width - (padding * 2f), 1f);
        var targetHeight = Math.Max(dirtyRect.Height - (padding * 2f), 1f);
        var scale = Math.Min(targetWidth / sourceWidth, targetHeight / sourceHeight);
        var offsetX = dirtyRect.Left + padding + ((targetWidth - (sourceWidth * scale)) / 2f);
        var offsetY = dirtyRect.Top + padding + ((targetHeight - (sourceHeight * scale)) / 2f);

        // Thicknesses below are in ink units; the canvas is scaled, so they are divided by the
        // scale to land on the wanted size on screen.
        var minimumInkStroke = MinimumStrokeSize / scale;
        var maximumInkStroke = Math.Max(
            Math.Min(dirtyRect.Width, dirtyRect.Height) * MaximumStrokeShare / scale,
            minimumInkStroke);

        canvas.SaveState();
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale, scale);
        canvas.Translate(-minX, -minY);

        foreach (var stroke in strokes)
        {
            var thickness = Math.Clamp(stroke.Thickness, minimumInkStroke, maximumInkStroke);

            canvas.StrokeColor = Color.FromArgb(stroke.Color);
            canvas.StrokeSize = thickness;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            if (stroke.Points.Count == 1)
            {
                var p = stroke.Points[0];
                canvas.FillColor = Color.FromArgb(stroke.Color);
                canvas.FillCircle(p.X, p.Y, thickness * 0.5f);
                continue;
            }

            for (var i = 1; i < stroke.Points.Count; i++)
            {
                var a = stroke.Points[i - 1];
                var b = stroke.Points[i];
                canvas.DrawLine(a.X, a.Y, b.X, b.Y);
            }
        }

        canvas.RestoreState();
    }

    private static void OnStrokesSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (InkPreviewView)bindable;
        view.UnsubscribeFromObservedCollection();
        view.SubscribeToObservedCollection(newValue as INotifyCollectionChanged);
        view.Invalidate();
    }

    private void SubscribeToObservedCollection(INotifyCollectionChanged? collection)
    {
        observedCollection = collection;
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged += OnObservedCollectionChanged;
        }
    }

    private void UnsubscribeFromObservedCollection()
    {
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged -= OnObservedCollectionChanged;
            observedCollection = null;
        }
    }

    private void OnObservedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Invalidate();

    private static (float minX, float minY, float maxX, float maxY) GetInkBounds(IEnumerable<InkStroke> strokes)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        foreach (var stroke in strokes)
        {
            foreach (var point in stroke.Points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        if (minX == float.MaxValue)
        {
            return (0f, 0f, 1f, 1f);
        }

        return (minX, minY, maxX, maxY);
    }
}
