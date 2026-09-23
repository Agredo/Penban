using Penban.Maui.Views.Controls;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Penban.Maui.Views.Ink;

/// <summary>One choice on a ring of the radial menu.</summary>
/// <param name="Name">
/// Shown in the middle of the menu while the segment is under the finger. A ring that is read off a
/// colour or off the size of a dot still has to be nameable for the choice to be remembered.
/// </param>
/// <param name="FillHex">
/// Colour the wedge is painted in, or <c>null</c> to leave it in the paper colour - which is what a
/// ring that shows its choices as dots rather than as colours wants.
/// </param>
/// <param name="DotDiameter">Diameter of a dot drawn in the middle of the wedge, in device units. <c>0</c> for none.</param>
public readonly record struct RadialMenuEntry(string Name, string? FillHex, float DotDiameter);

/// <summary>
/// A block of choices that shares the inner ring with the other blocks. Each group gets an arc of its
/// own and is centred on its own direction - the first one at the top, the rest clockwise around it -
/// so several kinds of choice can be offered at once instead of one after the other. The group's own
/// choices do not sit on that arc: they fan out onto the outer ring when the block is opened, which
/// is what keeps a block readable no matter how many choices it has.
/// </summary>
/// <param name="Title">Names the block, in the middle while it is in focus.</param>
/// <param name="Current">
/// The name of the entry that is set right now. The menu shows it while the block is in focus so it
/// can be read before it is worked - and so a block whose choices are fanned out still says what is
/// chosen at the moment. Empty for a block that carries an action rather than a set of choices.
/// </param>
/// <param name="Entries">The choices of the block, in the order they are laid out on the outer ring.</param>
/// <param name="Icon">
/// Glyph from <see cref="IconFont"/> drawn on the block's wedge, which is what the ring is read by.
/// The name only comes up in the middle while the finger is over the block, so the ring itself stays
/// a ring of pictures.
/// </param>
/// <param name="IsAction">
/// Whether the block does something on its own instead of holding choices. An action has no fan, so
/// it is taken the moment it is chosen - and it is kept on the ring even with nothing inside it,
/// which is what the empty <see cref="Entries"/> of an action means.
/// </param>
public readonly record struct RadialMenuGroup(
    string Title,
    string Current,
    IReadOnlyList<RadialMenuEntry> Entries,
    string Icon = "",
    bool IsAction = false);

/// <summary>What was chosen in the radial menu.</summary>
public enum RadialMenuHitKind
{
    /// <summary>Nothing was hit: the menu is not open, or the point is in a gap or in an empty part of the ring.</summary>
    None,

    /// <summary>A block on the inner ring, which opens its choices onto the outer ring. See <see cref="RadialMenuHit.Group"/>.</summary>
    Group,

    /// <summary>One of the choices fanned out on the outer ring; see <see cref="RadialMenuHit.Group"/> and <see cref="RadialMenuHit.Index"/>.</summary>
    Entry,

    /// <summary>
    /// The disc in the middle. It holds no choice of its own: a press there closes the ring that is
    /// out, which is what whoever owns the menu makes of it.
    /// </summary>
    Center,

    /// <summary>Beyond the ring, which is how the menu is left without choosing anything.</summary>
    Outside,
}

/// <summary>Where a released finger landed in the radial menu.</summary>
public readonly record struct RadialMenuHit(RadialMenuHitKind Kind, int Group, int Index);

/// <summary>
/// The radial menu a finger asks for by tapping the note: a ring of blocks drawn around the point it
/// was asked for at, with the pen colours and the pen widths among them, so every choice the editor
/// offers is one press away instead of hidden behind the others.
/// <para>
/// The blocks are the first level and the only one that is always there. Opening a block fans its
/// choices out around the ring outside it, which is what keeps a block readable no matter how many
/// choices it holds: ten pen colours would be slivers if they had to share half a ring with the pen
/// widths, but a block with the ring to itself can hold ten as easily as two. The inner ring stays
/// where it is while a block is open, so it always says where one is and what else could be opened.
/// </para>
/// <para>
/// The menu stays up while it is worked - a choice is applied and the block closes, nothing more -
/// and only a press outside it puts the whole of it away. A press in the middle puts away the ring
/// that is out: the fan of choices first, and the ring of blocks once there is nothing else left to
/// close. The middle is otherwise empty while nothing is under the finger and names what is under it
/// while something is, so a choice can be read before it is taken.
/// </para>
/// <para>
/// Segments are drawn as paths on a <see cref="SKCanvasView"/> rather than built from MAUI borders:
/// a circle segment is not something a rectangle can be bent into. The view holds no meaning of its
/// own - it is told what to show and reports what was released, and the page decides what a colour
/// index or a width index is worth.
/// </para>
/// <para>
/// It is opened by a finger and worked by the same finger, but nothing about it needs that finger:
/// once it is up, a block is also a button. A click with the mouse or a tap with the pen takes
/// whatever is under it straight away, without having to come in from the middle first - which is
/// the difference between a menu that has to be learned and one that is simply pressed.
/// </para>
/// </summary>
public sealed class RadialMenuView : SKCanvasView
{
    /// <summary>Radius of the ring as a share of the shorter side of the space it is drawn in.</summary>
    private const float RadiusShare = 0.3f;

    /// <summary>Bounds on that radius, so the ring is neither a speck on a tablet nor crowded on a phone.</summary>
    private const float MinimumRadius = 96f;
    private const float MaximumRadius = 168f;

    /// <summary>Inner edge of the outer ring, as a share of its outer radius.</summary>
    private const float DetailInnerShare = 0.70f;

    /// <summary>Outer and inner edge of the inner ring, as shares of the outer radius.</summary>
    private const float GroupOuterShare = 0.64f;
    private const float GroupInnerShare = 0.40f;

    /// <summary>Radius of the disc in the middle, as a share of the outer radius.</summary>
    private const float DiscShare = 0.34f;

    /// <summary>Angle left free between two groups, so the arcs read as separate blocks.</summary>
    private const float GroupGap = 4f;

    /// <summary>Keeps the whole ring clear of the edge it was opened against.</summary>
    private const float EdgeMargin = 6f;

    /// <summary>How far the segment under the finger is lifted out of the ring.</summary>
    private const float HighlightLift = 5f;

    /// <summary>Colour of the paper the menu is drawn on - the note stays light in both themes.</summary>
    private static readonly SKColor Paper = new(0xF7, 0xF5, 0xF0);

    /// <summary>Ink the menu is written in, dark enough to read on that paper.</summary>
    private static readonly SKColor Ink = new(0x1E, 0x22, 0x30);

    /// <summary>Line between two wedges that are not told apart by their colour.</summary>
    private static readonly SKColor Separator = new(0xD8, 0xD2, 0xC6);

    /// <summary>Wedge under the finger, for a ring whose wedges are not filled with a colour.</summary>
    private static readonly SKColor HighlightFill = new(0xE8, 0xE2, 0xD5);

    private readonly List<RadialMenuGroup> groups = [];
    private float centerX;
    private float centerY;
    private float radius;

    /// <summary>Which block has its choices fanned out on the outer ring, or <c>-1</c> while none has.</summary>
    private int detailGroup = -1;

    /// <summary>Block under the pointer on the inner ring, or <c>-1</c>.</summary>
    private int highlightedGroup = -1;

    /// <summary>Choice under the pointer on the outer ring, or <c>-1</c>. Only meaningful while a block is open.</summary>
    private int highlightedIndex = -1;

    /// <summary>Arc one group gets, in degrees.</summary>
    private float GroupSweep => groups.Count > 0 ? 360f / groups.Count : 0f;

    /// <summary>
    /// Where a group's arc starts, counted from straight up and clockwise, already clear of the gap
    /// that separates it from its neighbour. The first group is centred on straight up - a menu that
    /// opens under the finger should have its first block where the hand expects it - and the rest
    /// follow clockwise. Drawing and hit testing both start here, so what is under the finger and
    /// what is under the eye cannot drift apart.
    /// </summary>
    private float GroupStart(int group) => (group * GroupSweep) - (GroupSweep / 2f) + (GroupGap / 2f);

    /// <summary>
    /// Canvas pixels per device-independent unit, and the size of that canvas in those pixels. The
    /// menu is asked for in the units the page lays out in and drawn in the units the canvas counts
    /// in, and on a screen with a scale factor those are not the same number: drawing one as the
    /// other puts the ring part of the way to the finger and makes it that much smaller. Both are
    /// read off every paint, so a window that moves to a screen with another scale factor is
    /// followed without anyone having to say so.
    /// </summary>
    private float pixelScale = 1f;
    private float pixelWidth;
    private float pixelHeight;

    /// <summary>
    /// The dispatcher the canvas is drawn on, kept from the first paint so that the icon font - which
    /// arrives asynchronously, out of the app package - can ask for another one from whichever thread
    /// its own loading finished on.
    /// </summary>
    private IDispatcher? paintDispatcher;

    public RadialMenuView()
    {
        PaintSurface += OnPaintSurface;
        EnableTouchEvents = true;
        Touch += OnTouch;
        _ = LoadIconTypefaceAsync();
    }

    /// <summary>
    /// Gets the icon font ready and redraws once it is there, so a menu that was opened before the
    /// font had finished loading still ends up with its icons. Nothing is drawn twice if the font was
    /// already in hand, which is the usual case after the first menu of the session.
    /// </summary>
    private async Task LoadIconTypefaceAsync()
    {
        if (await IconFont.GetTypefaceAsync().ConfigureAwait(false) is null)
        {
            // The blocks fall back to their names - see DrawWedgeIcon - so there is nothing to redraw.
            return;
        }

        // No dispatcher means no paint has happened yet, and no menu can be waiting for one either:
        // the only way the ring comes up is through a paint of its own.
        paintDispatcher?.Dispatch(InvalidateSurface);
    }

    /// <summary>
    /// Takes a click or a tap the menu is up for. The view only listens while the ring is up - it is
    /// transparent to touches the rest of the time - and reports whatever was clicked the same way a
    /// released finger is reported, so there is one place that decides what a choice means.
    /// </summary>
    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        if (!IsOpen)
        {
            return;
        }

        e.Handled = true;
        var point = FromCanvas(e.Location);

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
            case SKTouchAction.Moved:
                Highlight(point.X, point.Y);
                break;

            case SKTouchAction.Released:
                var hit = Release(point.X, point.Y);
                if (hit.Kind != RadialMenuHitKind.None)
                {
                    // The middle is reported like anything else: it holds no choice, but it is where
                    // the ring that is out is closed from, and that is the owner's to act on.
                    ChoiceRequested?.Invoke(this, hit);
                }

                break;

            case SKTouchAction.Exited:
            case SKTouchAction.Cancelled:
                Release(point.X, point.Y);
                break;
        }
    }

    /// <summary>Width of the canvas in pixels, or the laid-out width while it has not been painted yet.</summary>
    private float ViewWidth => pixelWidth > 0 ? pixelWidth : (float)Width;

    /// <summary>Height of the canvas in pixels, or the laid-out height while it has not been painted yet.</summary>
    private float ViewHeight => pixelHeight > 0 ? pixelHeight : (float)Height;

    /// <summary>A point the page laid out, in the pixels the canvas is drawn in.</summary>
    private SKPoint ToCanvas(double x, double y) => new((float)x * pixelScale, (float)y * pixelScale);

    /// <summary>
    /// The way back: a point the canvas reported, in the units the page laid out. A touch arrives in
    /// canvas pixels, the ring was placed in laid-out units, and comparing one against the other
    /// would put the buttons somewhere other than where they are drawn.
    /// </summary>
    private SKPoint FromCanvas(SKPoint point) =>
        new(point.X / pixelScale, point.Y / pixelScale);

    /// <summary>
    /// Raised when a segment is taken by a click or a tap of its own rather than by the finger that
    /// opened the menu letting go over it. The hit says which group and which entry was picked; the
    /// page reads it exactly like the one a released finger reports.
    /// </summary>
    public event EventHandler<RadialMenuHit>? ChoiceRequested;

    /// <summary>Whether a ring is up and waiting for the finger.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Puts the menu up around <paramref name="x"/>,<paramref name="y"/>, which are in the same units
    /// as the space the view fills. The point is pulled inwards when it is too close to an edge for
    /// the whole ring to fit, so a menu opened near the bottom of the note is still complete.
    /// </summary>
    /// <param name="items">
    /// The blocks to offer on the inner ring. Each gets an arc of it, in the order given, and all of
    /// them are up at once - which is the point: a finger that is already resting on the note should
    /// not have to travel through one kind of choice to reach the next. The choices inside a block
    /// come up only when the block is opened, on the ring outside it. A block that carries an action
    /// has no choices and is still put up: it is taken by being chosen, not by being opened.
    /// </param>
    public void Open(IReadOnlyList<RadialMenuGroup> items, double x, double y)
    {
        groups.Clear();
        foreach (var group in items)
        {
            if (group.Entries.Count > 0 || group.IsAction)
            {
                groups.Add(group);
            }
        }

        detailGroup = -1;
        highlightedGroup = -1;
        highlightedIndex = -1;
        IsOpen = groups.Count > 0;
        InputTransparent = !IsOpen;

        var width = ViewWidth;
        var height = ViewHeight;
        var shortest = width > 0 && height > 0 ? Math.Min(width, height) : 0f;
        var scale = pixelScale;
        var margin = EdgeMargin * scale;

        radius = Math.Min(
            Math.Clamp(shortest * RadiusShare, MinimumRadius * scale, MaximumRadius * scale),
            Math.Max((shortest / 2f) - margin, 40f * scale));

        var point = ToCanvas(x, y);
        centerX = ClampCenter(point.X, radius, width, margin);
        centerY = ClampCenter(point.Y, radius, height, margin);

        InvalidateSurface();
    }

    /// <summary>
    /// Opens or closes one block's choices on the outer ring. A block that is already open is closed
    /// again, so the same click that opens a fan also puts it away and the inner ring is never a
    /// dead end. The inner ring itself stays where it is either way: it is what says where one is.
    /// </summary>
    /// <param name="group">Index of the block, in the order the groups were given.</param>
    public void ToggleDetail(int group)
    {
        if (!IsOpen || group < 0 || group >= groups.Count)
        {
            return;
        }

        detailGroup = detailGroup == group ? -1 : group;
        highlightedGroup = -1;
        highlightedIndex = -1;
        InvalidateSurface();
    }

    /// <summary>Whether a block currently has its choices fanned out on the ring outside it.</summary>
    public bool IsDetailOpen => IsOpen && detailGroup >= 0 && detailGroup < groups.Count;

    /// <summary>
    /// Puts the fan away and leaves the ring of blocks standing. The middle of the menu is the second
    /// way out of a fan besides the block that opened it, and it is the one that does not have to be
    /// found first: it is where the finger already is.
    /// </summary>
    public void CloseDetail()
    {
        if (!IsDetailOpen)
        {
            return;
        }

        detailGroup = -1;
        highlightedGroup = -1;
        highlightedIndex = -1;
        InvalidateSurface();
    }

    /// <summary>
    /// Follows the finger around the ring. Only the block or the choice under it is lifted, and the
    /// middle names both which block that is and what it is, so the finger never has to be lifted to
    /// see what it is about to choose.
    /// </summary>
    public RadialMenuHit Highlight(double x, double y)
    {
        if (!IsOpen)
        {
            return new RadialMenuHit(RadialMenuHitKind.None, -1, -1);
        }

        var hit = HitTest(x, y);
        var group = hit.Kind == RadialMenuHitKind.Group ? hit.Group : -1;
        var index = hit.Kind == RadialMenuHitKind.Entry ? hit.Index : -1;

        if (group == highlightedGroup && index == highlightedIndex)
        {
            return hit;
        }

        highlightedGroup = group;
        highlightedIndex = index;
        InvalidateSurface();
        return hit;
    }

    /// <summary>Reports what the finger let go of, and puts the highlight away.</summary>
    public RadialMenuHit Release(double x, double y)
    {
        var hit = HitTest(x, y);
        highlightedGroup = -1;
        highlightedIndex = -1;
        InvalidateSurface();
        return hit;
    }

    /// <summary>Takes the menu down.</summary>
    public void Close()
    {
        IsOpen = false;
        InputTransparent = true;
        detailGroup = -1;
        highlightedGroup = -1;
        highlightedIndex = -1;
        groups.Clear();
        InvalidateSurface();
    }

    /// <summary>
    /// Where a point is in the menu: the middle, a block on the inner ring, one of the choices fanned
    /// out on the outer ring, or nothing at all. Angles are counted from straight up and clockwise,
    /// which is the way the rings are drawn and the way a hand reads them, so the two can never drift
    /// apart. Which block a point falls in is decided by that angle alone rather than by asking each
    /// block in turn, so the answer does not depend on how many blocks there happen to be.
    /// </summary>
    private RadialMenuHit HitTest(double x, double y)
    {
        if (!IsOpen)
        {
            return new RadialMenuHit(RadialMenuHitKind.None, -1, -1);
        }

        var point = ToCanvas(x, y);
        var dx = point.X - centerX;
        var dy = point.Y - centerY;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (distance <= radius * DiscShare)
        {
            return new RadialMenuHit(RadialMenuHitKind.Center, -1, -1);
        }

        var fromTop = ((Math.Atan2(dy, dx) * 180 / Math.PI) + 90 + 360) % 360;

        // The outer ring is only there while a block is open, and while it is it owns everything
        // outside the inner ring - a point between the two rings belongs to no choice, but it is
        // still nearer the fan than the block, so it must not fall through to the inner ring.
        if (detailGroup >= 0 && detailGroup < groups.Count)
        {
            if (distance > radius)
            {
                return new RadialMenuHit(RadialMenuHitKind.Outside, -1, -1);
            }

            if (distance < radius * DetailInnerShare)
            {
                return new RadialMenuHit(RadialMenuHitKind.None, detailGroup, -1);
            }

            var entries = groups[detailGroup].Entries;
            var step = 360f / entries.Count;
            var index = (int)(fromTop / step);
            return new RadialMenuHit(
                RadialMenuHitKind.Entry,
                detailGroup,
                Math.Clamp(index, 0, entries.Count - 1));
        }

        // With no block open the menu is the ring and the disc and nothing else, and it is drawn that
        // way too - so what lies beyond the ring is outside the menu and closes it, rather than being
        // a wide rim of paper that swallows a click and does nothing with it.
        if (distance > radius * GroupOuterShare)
        {
            return new RadialMenuHit(RadialMenuHitKind.Outside, -1, -1);
        }

        if (distance < radius * GroupInnerShare)
        {
            return new RadialMenuHit(RadialMenuHitKind.None, -1, -1);
        }

        var sweep = GroupSweep;
        var arc = sweep - GroupGap;
        var shifted = (fromTop - GroupStart(0) + 360) % 360;
        var group = Math.Clamp((int)(shifted / sweep), 0, groups.Count - 1);
        var within = shifted - (group * sweep);

        // The angle between two blocks belongs to no block, so a finger resting there chooses
        // nothing rather than whichever block happens to be nearest. Inside a block there is no such
        // hole: it holds one wedge, and its own choices are a ring further out.
        return within >= arc
            ? new RadialMenuHit(RadialMenuHitKind.None, group, -1)
            : new RadialMenuHit(RadialMenuHitKind.Group, group, -1);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        // The one place the ratio between the two kinds of units is known for certain, and the place
        // every other number in this view is measured against.
        pixelScale = Width > 0 ? (float)(e.Info.Width / Width) : 1f;
        pixelWidth = e.Info.Width;
        pixelHeight = e.Info.Height;

        // Kept for the icon font, which has no paint of its own to ask on - see LoadIconTypefaceAsync.
        paintDispatcher = Dispatcher;

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (!IsOpen || groups.Count == 0 || radius <= 0)
        {
            return;
        }

        var scale = pixelScale;
        var outerRadius = radius;
        var groupOuter = radius * GroupOuterShare;
        var groupInner = radius * GroupInnerShare;
        var detailInner = radius * DetailInnerShare;
        var discRadius = radius * DiscShare;
        var isFanOpen = detailGroup >= 0 && detailGroup < groups.Count;

        // The menu is only as big as what is on it: the ring and the disc with no block open, and a
        // ring further out once one is. The centre was placed so that the whole of the larger one
        // fits, so nothing shifts when a block is opened - the ring stays where the hand left it and
        // the choices simply appear around it.
        var backdrop = isFanOpen ? outerRadius : groupOuter;

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

        // A hint of a shadow, so the ring reads as lying on the note rather than being part of it.
        fill.Color = new SKColor(0, 0, 0, 38);
        canvas.DrawCircle(centerX, centerY + (3 * scale), backdrop + (2 * scale), fill);

        fill.Color = Paper;
        canvas.DrawCircle(centerX, centerY, backdrop, fill);

        var sweep = GroupSweep;
        var arc = sweep - GroupGap;

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var isHighlighted = groupIndex == highlightedGroup;
            var isOpen = groupIndex == detailGroup;
            var start = -90f + GroupStart(groupIndex);
            var segmentOuter = isHighlighted ? groupOuter + (HighlightLift * scale) : groupOuter;

            using var wedge = Wedge(centerX, centerY, groupInner, segmentOuter, start, arc);

            // The block whose choices are out is marked the way a hovered one is, so the outer ring
            // can be read as belonging to it without having to hold the finger over it.
            fill.Color = isHighlighted || isOpen ? HighlightFill : Paper;
            canvas.DrawPath(wedge, fill);

            line.Color = isHighlighted || isOpen ? Ink : Separator;
            line.StrokeWidth = (isHighlighted ? 2f : 1.5f) * scale;
            canvas.DrawPath(wedge, line);

            DrawWedgeIcon(canvas, groups[groupIndex], groupInner, groupOuter, start, arc, scale);
        }

        if (isFanOpen)
        {
            DrawDetailRing(canvas, groups[detailGroup], detailInner, outerRadius, scale, fill, line);
        }

        fill.Color = Paper;
        canvas.DrawCircle(centerX, centerY, discRadius, fill);
        line.Color = new SKColor(0x1E, 0x22, 0x30, 0x44);
        line.StrokeWidth = 1.5f * scale;
        canvas.DrawCircle(centerX, centerY, discRadius, line);

        DrawCenter(canvas, discRadius, fill);
    }

    /// <summary>
    /// Fans one block's choices out around the whole outer ring. Sharing the ring with the other
    /// blocks is what would not scale: ten colours in half a ring would leave each of them a sliver,
    /// whereas a block that has the ring to itself can hold ten as easily as two.
    /// </summary>
    private void DrawDetailRing(
        SKCanvas canvas,
        RadialMenuGroup group,
        float innerRadius,
        float outerRadius,
        float scale,
        SKPaint fill,
        SKPaint line)
    {
        var entries = group.Entries;
        var step = 360f / entries.Count;
        var gap = Math.Min(step * 0.08f, 2.5f * scale);

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var isHighlighted = index == highlightedIndex;
            var start = -90f + (index * step) + (gap / 2f);
            var span = step - gap;
            var segmentOuter = isHighlighted ? outerRadius + (HighlightLift * scale) : outerRadius;

            using var wedge = Wedge(centerX, centerY, innerRadius, segmentOuter, start, span);

            fill.Color = entry.FillHex is { } hex
                ? SKColor.Parse(hex)
                : isHighlighted ? HighlightFill : Paper;
            canvas.DrawPath(wedge, fill);

            if (entry.DotDiameter > 0)
            {
                var middle = (start + (span / 2f)) * Math.PI / 180f;
                var dotRadius = (innerRadius + segmentOuter) / 2f;
                fill.Color = Ink;
                canvas.DrawCircle(
                    centerX + (float)(Math.Cos(middle) * dotRadius),
                    centerY + (float)(Math.Sin(middle) * dotRadius),
                    entry.DotDiameter * scale / 2f,
                    fill);
            }

            line.Color = isHighlighted ? Ink : Separator;
            line.StrokeWidth = (isHighlighted ? 2f : 1.5f) * scale;
            canvas.DrawPath(wedge, line);
        }
    }

    /// <summary>
    /// Draws a block's icon into its wedge on the inner ring. The ring says what a block is by the
    /// picture on it, and only the middle says it in words - and only while the finger is over the
    /// block - so the ring can be read at a glance and stays out of the way of the hand.
    /// <para>
    /// The icon is left upright rather than turned to follow the wedge the way a name had to be: a
    /// glyph on its side or standing on its head is not a picture anyone reads.
    /// </para>
    /// </summary>
    private void DrawWedgeIcon(
        SKCanvas canvas,
        RadialMenuGroup group,
        float innerRadius,
        float outerRadius,
        float start,
        float span,
        float scale)
    {
        if (string.IsNullOrEmpty(group.Icon))
        {
            return;
        }

        var middle = (start + (span / 2f)) * Math.PI / 180f;
        var radiusAt = (innerRadius + outerRadius) / 2f;
        var x = centerX + (float)(Math.Cos(middle) * radiusAt);
        var y = centerY + (float)(Math.Sin(middle) * radiusAt);

        using var paint = new SKPaint { IsAntialias = true, Color = Ink };

        // Without the icon font there is no glyph to draw, so the block falls back to its name: a
        // wedge that says what it is beats an empty one.
        if (IconFont.Typeface is not { } iconTypeface)
        {
            var room = radiusAt * span * (float)Math.PI / 180f;
            var degrees = (float)(middle * 180 / Math.PI) + 90;

            // Turned to follow the wedge, but never past the point where the name would stand on its head.
            if (degrees > 90 && degrees < 270)
            {
                degrees -= 180;
            }

            using var fallback = FitFont(group.Title, room, (outerRadius - innerRadius) * 0.42f, paint);
            canvas.Save();
            canvas.Translate(x, y);
            canvas.RotateDegrees(degrees);
            canvas.DrawText(group.Title, 0, 0, SKTextAlign.Center, fallback, paint);
            canvas.Restore();
            return;
        }

        // Sized against the thickness of the wedge rather than against its length: the glyph has to
        // fit between the two edges wherever on the ring it is, and a long wedge would only make it
        // wide. The glyphs of the icon font fill their em box and sit on the middle of the line, so
        // half a line below the middle is where the baseline puts a picture on a point - measured
        // against the font itself, where a quarter line would have left every icon a little high.
        var size = Math.Max((outerRadius - innerRadius) * 0.62f, 12f * scale);
        using var font = new SKFont(iconTypeface, size);
        canvas.DrawText(group.Icon, x, y + (size * 0.5f), SKTextAlign.Center, font, paint);
    }

    /// <summary>
    /// Writes the middle of the menu, which is the one place words are still used: the ring is read
    /// by its icons, and the name of whatever the finger is over only appears here, under the hand,
    /// for as long as it is over it. Nothing is written while the finger is between blocks or before
    /// it has arrived anywhere, so the middle of a menu nobody is pointing at is empty.
    /// </summary>
    private void DrawCenter(SKCanvas canvas, float discRadius, SKPaint paint)
    {
        if (detailGroup < 0 && highlightedGroup >= 0)
        {
            var group = groups[highlightedGroup];

            // A block that does something has nothing set that could be read out beside its name, so
            // its name is the whole of the middle and sits in the middle rather than above it.
            if (group.IsAction)
            {
                DrawCenterLine(canvas, discRadius, paint, group.Title, 0.09f, 0.26f, Ink);
                return;
            }

            DrawCenterLine(canvas, discRadius, paint, group.Title, -0.1f, 0.2f, Ink.WithAlpha(0x9E));
            DrawCenterLine(canvas, discRadius, paint, group.Current, 0.46f, 0.26f, Ink);
            return;
        }

        if (highlightedIndex >= 0 && detailGroup >= 0 && detailGroup < groups.Count)
        {
            var group = groups[detailGroup];
            DrawCenterLine(canvas, discRadius, paint, group.Title, -0.1f, 0.2f, Ink.WithAlpha(0x9E));
            DrawCenterLine(canvas, discRadius, paint, group.Entries[highlightedIndex].Name, 0.46f, 0.26f, Ink);
        }
    }

    /// <summary>
    /// One line in the middle of the menu, sized to fit the disc rather than to a fixed size: the
    /// names that go here are pen colours and pen widths, and they are not all the same length.
    /// </summary>
    private void DrawCenterLine(
        SKCanvas canvas,
        float discRadius,
        SKPaint paint,
        string text,
        float offset,
        float sizeShare,
        SKColor color)
    {
        if (text.Length == 0)
        {
            return;
        }

        paint.Color = color;
        using var font = FitFont(text, discRadius * 1.6f, discRadius * sizeShare, paint);
        canvas.DrawText(text, centerX, centerY + (discRadius * offset), SKTextAlign.Center, font, paint);
    }

    /// <summary>A font small enough for <paramref name="text"/> to fit into <paramref name="maximumWidth"/>.</summary>
    private static SKFont FitFont(string text, float maximumWidth, float preferredSize, SKPaint paint, SKTypeface? typeface = null)
    {
        var face = typeface ?? SKTypeface.Default;
        var font = new SKFont(face, preferredSize);
        var width = font.MeasureText(text, paint);
        if (width <= maximumWidth || width <= 0)
        {
            return font;
        }

        font.Dispose();
        return new SKFont(face, Math.Max(preferredSize * maximumWidth / width, 8f));
    }

    /// <summary>
    /// A ring segment: the outer arc, then back along the inner one. Both are struck from the same
    /// pair of angles, so the two ends of the piece are radial and the ring reads as cut rather than
    /// as drawn.
    /// </summary>
    private static SKPath Wedge(float x, float y, float inner, float outer, float startAngle, float sweepAngle)
    {
        using var builder = new SKPathBuilder();
        builder.ArcTo(new SKRect(x - outer, y - outer, x + outer, y + outer), startAngle, sweepAngle, false);
        builder.ArcTo(new SKRect(x - inner, y - inner, x + inner, y + inner), startAngle + sweepAngle, -sweepAngle, false);
        builder.Close();
        return builder.Detach();
    }

    /// <summary>Pulls a point inwards until the whole ring fits around it.</summary>
    private static float ClampCenter(float value, float radius, float size, float margin)
    {
        if (size <= 0)
        {
            return value;
        }

        var minimum = radius + margin;
        var maximum = size - radius - margin;
        return minimum >= maximum ? size / 2f : Math.Clamp(value, minimum, maximum);
    }
}
