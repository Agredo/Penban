using Penban.Models;

namespace Penban.Recognition;

/// <summary>Which shape a stroke that was held still has been read as.</summary>
public enum InkShape
{
    /// <summary>A straight line from where the stroke began to where it ended.</summary>
    Line,

    /// <summary>A closed four-cornered shape - a square when it came out as wide as it is tall.</summary>
    Rectangle,

    /// <summary>A closed round shape - a circle when it came out as wide as it is tall.</summary>
    Ellipse,

    /// <summary>A closed three-cornered shape - the ring a three-membered molecule is drawn as.</summary>
    Triangle,

    /// <summary>A closed five-cornered shape - the ring a five-membered molecule is drawn as.</summary>
    Pentagon,

    /// <summary>A closed six-cornered shape - the ring a six-membered molecule, benzene above all, is
    /// drawn as.</summary>
    Hexagon,
}

/// <summary>What a stroke was read as, together with the points the shape is drawn from.</summary>
/// <param name="Shape">The shape itself, which the note stores nothing about - only the points are.</param>
/// <param name="Points">The stroke's points as the shape sees them, in <see cref="InkDocument"/> units.</param>
public sealed record RecognizedShape(InkShape Shape, IReadOnlyList<InkPoint> Points)
{
    /// <summary>
    /// How far the shape was turned to be read, in radians, and therefore how far its points have been
    /// turned back to sit in the note's own frame. Zero for every shape that was read upright, which is
    /// every line, and for the closed shapes that were drawn upright to begin with.
    /// <para>
    /// A shape that is scaled has to be scaled in the frame it was read in: its box is only the shape's
    /// own box there. Pulling an upright box around a slanted rectangle shears it into a parallelogram
    /// instead of resizing it.
    /// </para>
    /// </summary>
    public float Angle { get; init; }
}

/// <summary>
/// Reads a freehand stroke as one of the shapes a note is drawn from: a line, a rectangle, an ellipse,
/// or a ring of three, five or six corners. Square and circle are those same two shapes with equal
/// sides, so a square is not a thing of its own here - a rectangle that came out as wide as it is tall
/// simply is one. The rings are the shapes a molecule is drawn as, and they are the only ones here that
/// are counted rather than measured: three, five and six are what a ring can be drawn as and still be
/// told from the round shapes it would otherwise be taken for.
/// <para>
/// Everything is measured against the box the stroke covers rather than against an ideal drawn from
/// the ends of the stroke, because that is what makes a hand-drawn shape read the same as a carefully
/// drawn one: a circle that starts and ends a little away from where it began still covers the same
/// box, and a line drawn slowly with a wobble still runs between the same two points.
/// </para>
/// <para>
/// Nothing here strays far from the shape it looks for: a stroke that could be a rectangle has to
/// run around its box and back to where it began, so a "C" - three sides and a gap - is only ever
/// read as a line or not at all. That is deliberate. Turning a stroke into a shape it is not is the
/// one mistake this can make that cannot be drawn over, and everything it does make can be undone
/// with a single step.
/// </para>
/// </summary>
public static class ShapeRecognizer
{
    /// <summary>
    /// The smallest side a shape may have, in document units - about 2.4% of the note's side. Below
    /// that it is a mark someone made while writing rather than a shape they drew: the pen wobbles
    /// that far on its own while a letter is being formed.
    /// </summary>
    public const float MinimumExtent = 24f;

    /// <summary>
    /// How far a stroke may stray from the straight line between its ends and still be one, as a
    /// share of that line's own length. A line drawn along a rule strays by about a percent of its
    /// length, one drawn quickly by pen by five; twelve leaves room for the second without swallowing
    /// the arcs someone draws on purpose.
    /// </summary>
    private const float LineDeviation = 0.12f;

    /// <summary>
    /// How far the two ends of a stroke may stand apart and still count as closed, as a share of the
    /// diagonal of the box it covers. Nobody draws a circle perfectly shut; leaving it quite open is
    /// taken to mean the shape was meant to be open.
    /// </summary>
    private const float Closure = 0.25f;

    /// <summary>
    /// How far the radius of a stroke may wander from the one the box suggests, as a share of that
    /// radius, to be read as an ellipse. The corners of a rectangle sit 41% outside the ellipse its
    /// box suggests, which is what keeps the two apart.
    /// </summary>
    private const float EllipseDeviation = 0.12f;

    /// <summary>
    /// How far a point of an ellipse may sit beyond its radius before the stroke stops being taken
    /// for one at all, as a share of the radius. This catches the stroke that was drawn as a spiral
    /// or with a tail, which the average deviation above would let through.
    /// </summary>
    private const float EllipseMaxRadius = 1.6f;

    /// <summary>
    /// How far a point of a rectangle may sit inside its box and still count as on one of its sides,
    /// as a share of the box's shorter side. Corners drawn round sit furthest from the sides, and this
    /// is wide enough for a corner radius of about a fifth of the shorter side.
    /// </summary>
    private const float EdgeTolerance = 0.14f;

    /// <summary>
    /// How close the stroke has to come to a corner of its box, as a share of the box's shorter side,
    /// before it is read as a shape that has corners at all. Every rectangle runs into the corners of
    /// its box, however round its own corners were drawn - a corner radius of two fifths of the shorter
    /// side still comes within this. An ellipse does not: the closest one gets to a corner of its box is
    /// a fifth of the shorter side, which this sits well inside of.
    /// </summary>
    private const float CornerTouch = 0.18f;

    /// <summary>
    /// How much of every side of the box a stroke has to run along to be read as a rectangle. All
    /// four sides have to be there - a stroke of three sides and a gap is not a rectangle - and half
    /// of each is asked for so a rectangle drawn from a corner that only half reaches the next one is
    /// still read.
    /// </summary>
    private const float SideCoverage = 0.5f;

    /// <summary>
    /// How far apart two points on the same side may lie and still count as the side being drawn in
    /// one stretch, as a share of the side's length. A side drawn in a hurry is sampled in jumps, and
    /// those jumps are what a rectangle's sides are made of, not gaps. The ends of a side that was
    /// never drawn, which the two corners next to it leave behind, are a whole side apart and are not
    /// bridged by this.
    /// </summary>
    private const float SideGap = 0.2f;

    /// <summary>How many points the shortest side of a generated ellipse is drawn from.</summary>
    private const int MinimumEllipsePoints = 32;

    /// <summary>How many points the longest side of one is drawn from.</summary>
    private const int MaximumEllipsePoints = 180;

    /// <summary>
    /// How many angles the stroke is tried at when the box that fits it most tightly is looked for.
    /// A rectangle that was drawn tilted is only as small as it gets once it is turned back upright,
    /// and a quarter turn covers every angle a box can be seen at.
    /// </summary>
    private const int TurnSteps = 24;

    /// <summary>
    /// How much of a stroke's ends may be dropped when no rectangle comes out of the stroke as drawn,
    /// as a share of its points. A stroke that ran on past the corner it closed the shape at leaves a
    /// tail, and the tail is what the ends are made of.
    /// </summary>
    private const float TrimShare = 0.12f;

    /// <summary>
    /// How many corners a stroke may be read as having, in the order they are tried. Three, five and
    /// six are the rings a molecule is drawn as; four is left out because a four-cornered stroke is a
    /// rectangle, and it is read as one from its box rather than from where its corners came out.
    /// </summary>
    private static readonly int[] PolygonSides = [3, 5, 6];

    /// <summary>How many times the corners of a ring are looked for again before they are taken as they
    /// came out - see <see cref="TrySides"/>. Two rounds are enough to settle a shape drawn by hand;
    /// the rounds after them change nothing and are there for the ones that do not settle.</summary>
    private const int CornerRounds = 4;

    /// <summary>
    /// How far apart two corners of a ring have to sit, as a share of the angle one side of it covers.
    /// Two corners closer together than this are not two corners but one the stroke wandered around,
    /// and a shape built from them would have a side of no length in it.
    /// </summary>
    private const float CornerSeparation = 0.5f;

    /// <summary>
    /// How far along the stroke either side of a corner the corner is measured over, as a share of the
    /// side of the ring it belongs to. Measured over the stroke rather than over the corners, because
    /// what is being asked is whether the stroke turns there at all.
    /// </summary>
    private const float CornerReach = 0.15f;

    /// <summary>
    /// How far a corner has to stand out of the straight line between the two points the reach either
    /// side of it lands on, as a share of that reach, before it counts as a corner at all. A corner of
    /// a ring drawn by hand stands out by about half the reach - cos of half the angle it turns, which
    /// is 0.5 for the 120 degrees of a hexagon and 0.87 for the 60 of a triangle. The same place on a
    /// circle stands out by the sagitta of an arc, a twentieth of the reach and less. This is what
    /// tells a hexagon from the ellipse it would otherwise be read as, and it is the one test here that
    /// a round stroke cannot pass.
    /// </summary>
    private const float CornerSharpness = 0.22f;

    /// <summary>
    /// Reads <paramref name="points"/> as a shape, or returns <c>null</c> when they are none of them.
    /// The points are the ones a stroke is made of, in <see cref="InkDocument"/> units, and the ones
    /// that come back are what the stroke is to be drawn from instead - the shape itself is not
    /// stored anywhere.
    /// </summary>
    public static RecognizedShape? Recognize(IReadOnlyList<InkPoint> points)
    {
        if (points.Count < 2)
        {
            return null;
        }

        // The line comes first because it is the one shape the others cannot be mistaken for: it is
        // the only one that covers no box worth speaking of, and the box of the others cannot be read
        // off a stroke that is flat in one direction.
        if (TryLine(points, out var line))
        {
            return line;
        }

        var box = Box(points);
        if (!IsClosed(points, box))
        {
            return null;
        }

        if (TryClosed(points, out var shape))
        {
            return shape;
        }

        // A stroke that ran on past the corner it closed the shape at leaves a tail outside it, and the
        // tail widens the box until the side it ran along is left standing inside it. Dropping the ends
        // - which is where a tail is - leaves the rectangle that was drawn. Which end it is at cannot be
        // known, so both are dropped, by more and more of them.
        var most = (int)(points.Count * TrimShare);
        var step = Math.Max(1, most / 6);

        for (var trim = step; trim <= most; trim += step)
        {
            if (TryClosed(Range(points, trim, points.Count), out var fromStart))
            {
                return fromStart;
            }

            if (TryClosed(Range(points, 0, points.Count - trim), out var fromEnd))
            {
                return fromEnd;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a closed stroke is an ellipse or a rectangle. Both are read in the frame the stroke sits
    /// most squarely in, because a box is only ever as big as the shape in it when the shape is upright:
    /// a rectangle drawn at a slant covers a box far larger than itself, and in a box like that nothing
    /// of the stroke is what it was drawn as any more.
    /// </summary>
    private static bool TryClosed(IReadOnlyList<InkPoint> points, out RecognizedShape? shape)
    {
        shape = null;

        var angle = TightestAngle(points);
        var turned = angle == 0f ? points : Turned(points, angle);
        var box = Box(turned);

        // Whether the stroke runs into the corners of its box, which every rectangle does and no
        // ellipse does.
        var polygon = TouchesCorners(turned, box);
        RecognizedShape? found = null;

        // The rings of three, five and six corners come before the two round shapes, and a hexagon is
        // the reason: its corners sit only a seventh of its radius outside the ellipse of its box, so
        // it passes for one and would never be found if the ellipse were asked first. Nothing round
        // can be taken for a ring in its place, because a ring is only read from a stroke that turns a
        // corner at every one of its corners - see TryPolygon.
        if (!TryPolygon(turned, box, out found))
        {
            // The ellipse before the rectangle because the two overlap everywhere else: a circle drawn
            // with flat sides and a rectangle drawn with rounded corners are the same stroke, and the
            // ellipse is the reading that keeps the stroke's roundness. Only a stroke that was drawn
            // with corners - which the ellipse test would flatten, sides and all - is asked for the
            // rectangle first.
            var rounded = !polygon && TryEllipse(turned, box, out found);

            if (!rounded && !TryRectangle(turned, box, out found))
            {
                return false;
            }
        }

        if (found is null)
        {
            return false;
        }

        // The shape was read in the frame the stroke sits most squarely in, and what the stroke is
        // drawn from has to sit in the note's own frame again. Which frame that was is kept on the
        // shape, because scaling one has to happen in the frame it was read in: pulling an upright box
        // around a slanted rectangle shears it into a parallelogram.
        shape = angle == 0f
            ? found
            : found with { Points = Turned(found.Points, -angle), Angle = angle };

        return true;
    }

    private static bool TryLine(IReadOnlyList<InkPoint> points, out RecognizedShape? shape)
    {
        shape = null;

        var first = points[0];
        var last = points[^1];
        var dx = last.X - first.X;
        var dy = last.Y - first.Y;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));

        if (length < MinimumExtent)
        {
            return false;
        }

        // How far every point sits from the line through the two ends, which is the area of the
        // triangle the point makes with them over the length of that line.
        foreach (var point in points)
        {
            var deviation = MathF.Abs(((point.X - first.X) * dy) - ((point.Y - first.Y) * dx)) / length;
            if (deviation > length * LineDeviation)
            {
                return false;
            }
        }

        // The line is drawn between the very points the pen was put down on and lifted from, at the
        // width the stroke was written with - the ends are where the writer meant them, so moving them
        // to the ends of the box would only move the line away from where it was drawn.
        var pressure = AveragePressure(points);
        shape = new RecognizedShape(InkShape.Line, [With(first, pressure), With(last, pressure)]);
        return true;
    }

    private static bool TryEllipse(IReadOnlyList<InkPoint> points, InkBounds box, out RecognizedShape? shape)
    {
        shape = null;

        var radiusX = (box.Width) / 2f;
        var radiusY = (box.Height) / 2f;

        // A shape that is this much longer than it is wide is a line, a stroke or a gesture - never a
        // circle - and the two radii below would be dividing by almost nothing.
        if (MathF.Min(radiusX, radiusY) < MinimumExtent / 2f)
        {
            return false;
        }

        var centerX = box.MinX + radiusX;
        var centerY = box.MinY + radiusY;
        var total = 0f;

        foreach (var point in points)
        {
            var x = (point.X - centerX) / radiusX;
            var y = (point.Y - centerY) / radiusY;
            var radius = MathF.Sqrt((x * x) + (y * y));

            if (radius > EllipseMaxRadius)
            {
                return false;
            }

            total += MathF.Abs(radius - 1f);
        }

        if (total / points.Count > EllipseDeviation)
        {
            return false;
        }

        // The circumference of the ellipse, by the approximation that is exact enough for a pen and
        // costs nothing: what it is used for is how many points to walk the shape in, and a point more
        // or less in a curve is invisible.
        var circumference = MathF.PI * (3f * (radiusX + radiusY) - MathF.Sqrt(((3f * radiusX) + radiusY) * (radiusX + (3f * radiusY))));
        var count = Math.Clamp((int)(circumference / 6f), MinimumEllipsePoints, MaximumEllipsePoints);
        var generated = new List<InkPoint>(count + 1);
        var pressure = AveragePressure(points);
        var tick = points[0].TimestampMs;

        for (var i = 0; i <= count; i++)
        {
            var angle = 2f * MathF.PI * i / count;
            generated.Add(With(
                points[0],
                centerX + (radiusX * MathF.Cos(angle)),
                centerY + (radiusY * MathF.Sin(angle)),
                pressure,
                tick + i));
        }

        shape = new RecognizedShape(InkShape.Ellipse, generated);
        return true;
    }

    private static bool TryRectangle(IReadOnlyList<InkPoint> points, InkBounds box, out RecognizedShape? shape)
    {
        shape = null;

        if (MathF.Min(box.Width, box.Height) < MinimumExtent)
        {
            return false;
        }

        var tolerance = MathF.Min(box.Width, box.Height) * EdgeTolerance;
        foreach (var point in points)
        {
            var inside = MathF.Min(
                MathF.Min(point.X - box.MinX, box.MaxX - point.X),
                MathF.Min(point.Y - box.MinY, box.MaxY - point.Y));

            if (inside > tolerance)
            {
                return false;
            }
        }

        if (!CoversEverySide(points, box, tolerance))
        {
            return false;
        }

        // The four corners and back to the first one, so the shape is a closed loop the way the stroke
        // was. Which corner it begins at and which way round it runs cannot be seen in the result, so
        // they are not read out of the stroke.
        var pressure = AveragePressure(points);
        var tick = points[0].TimestampMs;

        shape = new RecognizedShape(
            InkShape.Rectangle,
            [
                With(points[0], box.MinX, box.MinY, pressure, tick),
                With(points[0], box.MaxX, box.MinY, pressure, tick + 1),
                With(points[0], box.MaxX, box.MaxY, pressure, tick + 2),
                With(points[0], box.MinX, box.MaxY, pressure, tick + 3),
                With(points[0], box.MinX, box.MinY, pressure, tick + 4),
            ]);

        return true;
    }

    /// <summary>
    /// Whether a closed stroke is a ring of three, five or six corners.
    /// <para>
    /// The corners are not looked for by their angles, which a stroke drawn by hand never holds: they
    /// are found by how far out they sit. Seen from the middle of a closed shape, the distance to the
    /// stroke grows towards every corner and shrinks again along every side - no point between two
    /// corners can be further out than the corners themselves, whatever shape the ring has. So the
    /// stroke is cut into as many shares of the circle as the ring has corners, and the point furthest
    /// out in each share is one of them.
    /// </para>
    /// <para>
    /// What comes out of that is then asked for everything a ring has to be: every corner turned the
    /// same way round, every point of the stroke near one of the sides, and every corner a corner the
    /// stroke actually turns at rather than a point on a round stroke that a share happened to land on.
    /// The last of those is what keeps a circle from being read as a hexagon, which is the one mistake
    /// that would be made here otherwise - see <see cref="CornerSharpness"/>.
    /// </para>
    /// </summary>
    private static bool TryPolygon(IReadOnlyList<InkPoint> points, InkBounds box, out RecognizedShape? shape)
    {
        shape = null;

        // A ring of any of the three sizes covers most of the box it was drawn in, so a stroke that is
        // flat in one direction is none of them - and the shares below would be cut across nothing.
        if (MathF.Min(box.Width, box.Height) < MinimumExtent)
        {
            return false;
        }

        var centerX = box.MinX + (box.Width / 2f);
        var centerY = box.MinY + (box.Height / 2f);

        // Where every point sits as seen from the middle of the box, and how far out it is.
        var angles = new float[points.Count];
        var radii = new float[points.Count];

        for (var i = 0; i < points.Count; i++)
        {
            var dx = points[i].X - centerX;
            var dy = points[i].Y - centerY;
            radii[i] = MathF.Sqrt((dx * dx) + (dy * dy));
            angles[i] = MathF.Atan2(dy, dx);
        }

        // The point furthest out is one of the corners, and the middle of the box the middle the
        // corners are seen from.
        foreach (var sides in PolygonSides)
        {
            if (TrySides(points, angles, radii, sides, box, out shape))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the stroke is a ring of <paramref name="sides"/> corners, given where each of its points
    /// sits around the middle and how far out it is.
    /// </summary>
    private static bool TrySides(
        IReadOnlyList<InkPoint> points,
        float[] angles,
        float[] radii,
        int sides,
        InkBounds box,
        out RecognizedShape? shape)
    {
        shape = null;

        var span = (2f * MathF.PI) / sides;

        // The corners are the points furthest out. The distance to a closed stroke, seen from the
        // middle of it, grows towards every corner and falls away along the sides next to it, so a
        // corner is a point nothing else on the stroke stands further out than - and the corner count
        // is the count of those. They are taken from the furthest out inwards, and a point that close
        // to a corner already taken is part of that corner rather than a corner of its own: a side of a
        // ring drawn by hand bulges, and the bulge stands out nearly as far as a corner does.
        var apart = span * CornerSeparation;
        var corners = new List<int>(sides);

        foreach (var i in Enumerable.Range(0, angles.Length).OrderByDescending(i => radii[i]))
        {
            if (corners.All(corner => MathF.Abs(Shortest(angles[i], angles[corner])) >= apart))
            {
                corners.Add(i);
                if (corners.Count == sides)
                {
                    break;
                }
            }
        }

        // Fewer corners than the ring has means the stroke never went near one of its sides, so what
        // was drawn is not a ring of this many corners.
        if (corners.Count < sides)
        {
            return false;
        }

        // The corners in the order the stroke runs through them, which is the order the sides are
        // walked in below.
        var from = angles[corners[0]];
        corners.Sort((a, b) => Wrapped(angles[a] - from).CompareTo(Wrapped(angles[b] - from)));

        // A corner of a hand-drawn ring sits somewhere inside the run of points that belongs to it
        // rather than exactly at the point furthest out, so every point is given to the corner it is
        // nearest to around the middle, the furthest point of each of those groups is the corner again,
        // and that is repeated until it settles.
        var settled = corners.ToArray();

        for (var round = 0; round < CornerRounds; round++)
        {
            var moved = new int[sides];
            var furthest = new float[sides];

            for (var corner = 0; corner < sides; corner++)
            {
                moved[corner] = -1;
            }

            for (var i = 0; i < angles.Length; i++)
            {
                var nearest = 0;
                var closest = float.MaxValue;

                for (var corner = 0; corner < sides; corner++)
                {
                    var gap = MathF.Abs(Shortest(angles[i], angles[settled[corner]]));
                    if (gap < closest)
                    {
                        closest = gap;
                        nearest = corner;
                    }
                }

                if (moved[nearest] < 0 || radii[i] > furthest[nearest])
                {
                    furthest[nearest] = radii[i];
                    moved[nearest] = i;
                }
            }

            // A round that left a corner with no point of its own has nothing to say about where the
            // corners are, so the round before it stands.
            if (moved.Any(corner => corner < 0))
            {
                break;
            }

            settled = moved;
        }

        corners.Clear();
        corners.AddRange(settled);

        // Two corners on top of each other are one corner the stroke wandered around, not two.
        for (var corner = 0; corner < sides; corner++)
        {
            var next = (corner + 1) % sides;
            if (MathF.Abs(Shortest(angles[corners[next]], angles[corners[corner]])) < span * CornerSeparation)
            {
                return false;
            }
        }

        var vertices = new InkPoint[sides];
        for (var corner = 0; corner < sides; corner++)
        {
            vertices[corner] = points[corners[corner]];
        }

        // Every corner has to turn the same way round: a ring with a bite taken out of it turns one way
        // at its own corners and the other way at the bite.
        var turn = 0;
        for (var corner = 0; corner < sides; corner++)
        {
            var a = vertices[corner];
            var b = vertices[(corner + 1) % sides];
            var c = vertices[(corner + 2) % sides];
            var cross = ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));

            if (cross == 0f)
            {
                return false;
            }

            var way = cross > 0f ? 1 : -1;
            if (turn == 0)
            {
                turn = way;
            }
            else if (turn != way)
            {
                return false;
            }
        }

        // Every point of the stroke has to be near one of the sides. This is what rules out the shapes
        // the corners alone would let through: a square read as a triangle has one corner standing in
        // the middle of it, and a pentagon read as a hexagon has a corner no side comes near.
        var tolerance = MathF.Min(box.Width, box.Height) * EdgeTolerance;
        foreach (var point in points)
        {
            var nearest = float.MaxValue;
            for (var corner = 0; corner < sides; corner++)
            {
                nearest = MathF.Min(
                    nearest,
                    DistanceToSegment(point.X, point.Y, vertices[corner], vertices[(corner + 1) % sides]));
            }

            if (nearest > tolerance)
            {
                return false;
            }
        }

        // And every corner has to be a corner the stroke turns at, measured over the stroke itself
        // rather than over the corners: the points a reach either side of it, and how far the corner
        // stands out of the line between them.
        var perimeter = 0f;
        for (var corner = 0; corner < sides; corner++)
        {
            perimeter += Between(vertices[corner], vertices[(corner + 1) % sides]);
        }

        var reach = perimeter / sides * CornerReach;
        for (var corner = 0; corner < sides; corner++)
        {
            var at = corners[corner];
            var before = Reached(points, at, reach, -1);
            var after = Reached(points, at, reach, 1);

            if (before < 0 || after < 0)
            {
                return false;
            }

            if (DistanceToSegment(points[at].X, points[at].Y, points[before], points[after]) < reach * CornerSharpness)
            {
                return false;
            }
        }

        // The corners and back to the first one, so the shape is a closed loop the way the stroke was -
        // the same shape a rectangle is written as, see TryRectangle.
        var pressure = AveragePressure(points);
        var tick = points[0].TimestampMs;
        var generated = new List<InkPoint>(sides + 1);

        for (var corner = 0; corner < sides; corner++)
        {
            generated.Add(With(points[0], vertices[corner].X, vertices[corner].Y, pressure, tick + corner));
        }

        generated.Add(With(points[0], vertices[0].X, vertices[0].Y, pressure, tick + sides));

        shape = new RecognizedShape(
            sides switch
            {
                3 => InkShape.Triangle,
                5 => InkShape.Pentagon,
                _ => InkShape.Hexagon,
            },
            generated);

        return true;
    }

    /// <summary>
    /// The point a <paramref name="reach"/> along the stroke from <paramref name="from"/>, walking
    /// towards its start or its end. The stroke is a closed shape, so the walk carries on from one end
    /// of it to the other. <c>-1</c> when the walk came all the way back round to where it began, which
    /// means the stroke is shorter than the reach asked for.
    /// </summary>
    private static int Reached(IReadOnlyList<InkPoint> points, int from, float reach, int step)
    {
        var walked = 0f;
        var at = from;

        for (var taken = 0; taken < points.Count; taken++)
        {
            var next = at + step;
            if (next < 0)
            {
                next = points.Count - 1;
            }
            else if (next >= points.Count)
            {
                next = 0;
            }

            if (next == from)
            {
                return -1;
            }

            walked += Between(points[at], points[next]);
            if (walked >= reach)
            {
                return next;
            }

            at = next;
        }

        return -1;
    }

    /// <summary>The distance between two points.</summary>
    private static float Between(InkPoint from, InkPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>The angle <paramref name="angle"/> turned back to sit within one whole turn of
    /// <paramref name="from"/>, in [0, 2π).</summary>
    private static float Wrapped(float angle, float from = 0f)
    {
        var turned = (angle - from) % (2f * MathF.PI);
        return turned < 0f ? turned + (2f * MathF.PI) : turned;
    }

    /// <summary>The shortest way round from <paramref name="from"/> to <paramref name="angle"/>, in
    /// (-π, π] - how far apart the two are, whichever way round they are nearer.</summary>
    private static float Shortest(float angle, float from)
    {
        var turned = Wrapped(angle, from);
        return turned > MathF.PI ? turned - (2f * MathF.PI) : turned;
    }

    /// <summary>
    /// Whether the stroke runs along all four sides of its box. Without this a stroke that covers
    /// three sides and stops - which is a "C", or the "U" of a letter - passes every other test a
    /// rectangle is asked: it lies on the box, and its ends are close together, because they are the
    /// two ends of the side that is missing.
    /// </summary>
    private static bool CoversEverySide(IReadOnlyList<InkPoint> points, InkBounds box, float tolerance)
    {
        // How far along each side of the box the stroke runs: a point is counted on every side it is
        // close enough to, which is what puts the corners of a rectangle on two sides at once.
        var along = new List<float>[4];
        for (var side = 0; side < 4; side++)
        {
            along[side] = new List<float>();
        }

        foreach (var point in points)
        {
            var down = box.Height > 0 ? (point.Y - box.MinY) / box.Height : 0f;
            var across = box.Width > 0 ? (point.X - box.MinX) / box.Width : 0f;

            Mark(0, down, point.X - box.MinX);
            Mark(1, down, box.MaxX - point.X);
            Mark(2, across, point.Y - box.MinY);
            Mark(3, across, box.MaxY - point.Y);

            void Mark(int side, float position, float distance)
            {
                if (distance <= tolerance)
                {
                    along[side].Add(Math.Clamp(position, 0f, 1f));
                }
            }
        }

        // A side counts as drawn when one unbroken stretch of it is there. Two ends with nothing in
        // between - what the corners at either end of a missing side leave behind - are not a side.
        foreach (var side in along)
        {
            if (side.Count == 0)
            {
                return false;
            }

            side.Sort();

            var start = side[0];
            var end = side[0];
            var longest = 0f;

            for (var i = 1; i < side.Count; i++)
            {
                if (side[i] - end > SideGap)
                {
                    longest = MathF.Max(longest, end - start);
                    start = side[i];
                }

                end = side[i];
            }

            if (MathF.Max(longest, end - start) < SideCoverage)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the stroke runs into every corner of its box. This is the one thing a rectangle has that
    /// an ellipse has not, and it is asked for because the two are otherwise the same stroke: a
    /// rectangle whose sides were drawn with a bow in them sits close enough to the ellipse of its box
    /// to be read as one, and an ellipse drawn with a flat side to be read as a rectangle.
    /// Measured against the segments between the points rather than the points themselves, because the
    /// points of a quickly drawn arc can be far apart: a corner rounded off to a fifth of the shorter
    /// side leaves a gap that a sparse arc steps over without ever landing inside it.
    /// </summary>
    private static bool TouchesCorners(IReadOnlyList<InkPoint> points, InkBounds box)
    {
        var reach = MathF.Min(box.Width, box.Height) * CornerTouch;

        return Near(box.MinX, box.MinY)
            && Near(box.MaxX, box.MinY)
            && Near(box.MinX, box.MaxY)
            && Near(box.MaxX, box.MaxY);

        bool Near(float x, float y)
        {
            for (var index = 1; index < points.Count; index++)
            {
                if (DistanceToSegment(x, y, points[index - 1], points[index]) <= reach)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static float DistanceToSegment(float x, float y, InkPoint from, InkPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = (dx * dx) + (dy * dy);

        var t = lengthSquared <= 0f
            ? 0f
            : Math.Clamp((((x - from.X) * dx) + ((y - from.Y) * dy)) / lengthSquared, 0f, 1f);

        var nearestX = from.X + (t * dx);
        var nearestY = from.Y + (t * dy);

        dx = x - nearestX;
        dy = y - nearestY;

        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// The angle the stroke is turned by before the box that fits it is measured, in radians: the turn
    /// whose box covers the least. A rectangle is as small as it gets at the angle it was drawn at. A
    /// stroke that has no such angle comes out at one that fits it no better than the others, and is
    /// thrown out by the rectangle test below all the same.
    /// <para>
    /// Half a quarter turn either way is all of them: a box covers the same area when it is seen from
    /// the side, so a rectangle drawn past halfway to the corner is the same stroke as one drawn at the
    /// angle left over.
    /// </para>
    /// </summary>
    private static float TightestAngle(IReadOnlyList<InkPoint> points)
    {
        var tightest = 0f;
        var smallest = float.MaxValue;

        for (var step = 0; step <= TurnSteps; step++)
        {
            var angle = (MathF.PI / 2f) * (step - (TurnSteps / 2)) / TurnSteps;
            var box = Box(Turned(points, angle));
            var area = box.Width * box.Height;

            if (area < smallest)
            {
                smallest = area;
                tightest = angle;
            }
        }

        return tightest;
    }

    /// <summary>The points turned about the origin by <paramref name="angle"/> radians.</summary>
    internal static List<InkPoint> Turned(IReadOnlyList<InkPoint> points, float angle)
    {
        var sin = MathF.Sin(angle);
        var cos = MathF.Cos(angle);
        var turned = new List<InkPoint>(points.Count);

        foreach (var point in points)
        {
            turned.Add(With(
                point,
                (point.X * cos) - (point.Y * sin),
                (point.X * sin) + (point.Y * cos),
                point.Pressure,
                point.TimestampMs));
        }

        return turned;
    }

    /// <summary>The points from <paramref name="start"/> up to but not including <paramref name="end"/>.</summary>
    private static List<InkPoint> Range(IReadOnlyList<InkPoint> points, int start, int end)
    {
        var range = new List<InkPoint>(end - start);
        for (var i = start; i < end; i++)
        {
            range.Add(points[i]);
        }

        return range;
    }

    /// <summary>
    /// Whether the stroke ends where it began, near enough: a closed shape is drawn in one go and has
    /// to come back round, which is the one thing that tells a circle apart from an arc and a
    /// rectangle from the corner of one.
    /// </summary>
    private static bool IsClosed(IReadOnlyList<InkPoint> points, InkBounds box)
    {
        var first = points[0];
        var last = points[^1];
        var dx = last.X - first.X;
        var dy = last.Y - first.Y;
        var gap = MathF.Sqrt((dx * dx) + (dy * dy));

        return gap <= Closure * MathF.Sqrt((box.Width * box.Width) + (box.Height * box.Height));
    }

    /// <summary>
    /// The box the points cover, which is what the ellipse and the rectangle are measured against: a
    /// hand-drawn circle covers the box of the circle it was meant to be, and a hand-drawn rectangle
    /// the box of its corners. It may be flat in one direction - the shape of a line - which every
    /// caller below has to be able to live with.
    /// </summary>
    internal static InkBounds Box(IReadOnlyList<InkPoint> points)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        foreach (var point in points)
        {
            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }

        return new InkBounds(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// The pressure a stroke was written with on average, which is what every point of the shape made
    /// from it is drawn at: a shape has no press and lift to it, and the width it is drawn in is the
    /// width the stroke was written in rather than the width of its last, usually lighter, point.
    /// </summary>
    private static float AveragePressure(IReadOnlyList<InkPoint> points)
    {
        var total = 0f;
        foreach (var point in points)
        {
            total += point.Pressure > 0 ? point.Pressure : 1f;
        }

        return total / points.Count;
    }

    private static InkPoint With(InkPoint reference, float pressure) => new()
    {
        X = reference.X,
        Y = reference.Y,
        Pressure = pressure,
        Tilt = reference.Tilt,
        Azimuth = reference.Azimuth,
        TimestampMs = reference.TimestampMs,
    };

    /// <summary>
    /// A point of a generated shape. The lean of the pen is taken from the stroke: a shape drawn with
    /// a leaning pen keeps the width a leaning pen gives it, so straightening a line does not make it
    /// thinner than the strokes around it.
    /// </summary>
    internal static InkPoint With(InkPoint reference, float x, float y, float pressure, long timestamp) => new()
    {
        X = x,
        Y = y,
        Pressure = pressure,
        Tilt = reference.Tilt,
        Azimuth = reference.Azimuth,
        TimestampMs = timestamp,
    };
}
