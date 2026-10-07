using System.Collections.Specialized;
using Penban.Models;

namespace Penban.Maui.Views.Controls;

/// <summary>
/// Lightweight, read-only ink preview for board cards. The drawing itself lives in
/// <see cref="InkRenderer"/>, which the widget snapshots use as well, so a note looks the same
/// wherever it is rendered.
/// </summary>
public sealed class InkPreviewView : GraphicsView, IDrawable
{
    public static readonly BindableProperty StrokesSourceProperty = BindableProperty.Create(
        nameof(StrokesSource),
        typeof(IEnumerable<InkStroke>),
        typeof(InkPreviewView),
        default(IEnumerable<InkStroke>),
        propertyChanged: OnStrokesSourceChanged);

    public static readonly BindableProperty SourceRectProperty = BindableProperty.Create(
        nameof(SourceRect),
        typeof(RectF?),
        typeof(InkPreviewView),
        default(RectF?),
        propertyChanged: (bindable, _, _) => ((InkPreviewView)bindable).Invalidate());

    private INotifyCollectionChanged? observedCollection;

    public InkPreviewView()
    {
        Drawable = this;

#if ANDROID
        // Nothing here answers a touch, and on Android the drawing surface would swallow every one of
        // them: a GraphicsView takes the touch down itself and hands it back as consumed, so the note
        // the drawing sits on never hears the tap that opens it, and a note that is held down never
        // starts the drag. Transparent to input, the touch goes on to the note underneath. The other
        // platforms hand the touch on by themselves, so they are left as they were.
        InputTransparent = true;
#endif
    }

    public IEnumerable<InkStroke>? StrokesSource
    {
        get => (IEnumerable<InkStroke>?)GetValue(StrokesSourceProperty);
        set => SetValue(StrokesSourceProperty, value);
    }

    /// <summary>
    /// Part of the ink document to show, in document units. The whole document by default; a note that
    /// follows its content hands in the part its ink uses, so the drawing fills the note.
    /// </summary>
    public RectF? SourceRect
    {
        get => (RectF?)GetValue(SourceRectProperty);
        set => SetValue(SourceRectProperty, value);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect) =>
        InkRenderer.Draw(canvas, StrokesSource, dirtyRect, SourceRect);

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
