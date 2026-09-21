using System.Collections.Specialized;
using System.Linq;
using Penban.Models;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Lightweight, read-only ink preview for board cards.
/// <para>
/// The strokes are in <see cref="InkDocument"/> space, so the preview only has to scale the whole
/// document onto itself - the note is square and so is the document, so one factor is enough. That is
/// what makes a card look the same as the note it was written on: the drawing keeps its position and
/// its size relative to the sheet instead of being enlarged and centred in it.
/// </para>
/// <para>
/// This is a reduced rendering on purpose - it uses one width per stroke rather than following the
/// pressure along it, and it ignores pen tilt. The note editor shows the real thing.
/// </para>
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

        var targetWidth = Math.Max(dirtyRect.Width, 1f);
        var targetHeight = Math.Max(dirtyRect.Height, 1f);
        var scale = Math.Min(targetWidth / InkDocument.Size, targetHeight / InkDocument.Size);

        // Thicknesses are in document units; the canvas is scaled, so they are divided by the scale to
        // land on the wanted size on screen. Only a lower bound is needed now - nothing is scaled up
        // any more, so a stroke can only come out too thin to see, never too thick.
        var minimumInkStroke = MinimumStrokeSize / scale;

        canvas.SaveState();
        canvas.Scale(scale, scale);

        foreach (var stroke in strokes)
        {
            var thickness = Math.Max(stroke.Thickness, minimumInkStroke);

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
}
