using NetTopologySuite.Geometries;

namespace CivilAI;

internal static class ReferenceOffsetGeometry
{
    internal static TrianglePoint[] Offset(IReadOnlyList<TrianglePoint> source, bool closed, double signedDistance)
    {
        var count = source.Count;
        if (count < (closed ? 3 : 2) || !double.IsFinite(signedDistance) || signedDistance == 0)
            throw new ArgumentException("A valid edge and nonzero offset are required.");

        var result = new TrianglePoint[count];
        (double X, double Y) Direction(int from, int to)
        {
            var dx = source[to].X - source[from].X;
            var dy = source[to].Y - source[from].Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(length) || length <= 1e-9)
                throw new ArgumentException("The edge has repeated plan points or a vertical segment.");
            return (dx / length, dy / length);
        }

        for (var i = 0; i < count; i++)
        {
            var incoming = i == 0 && !closed ? Direction(0, 1) : Direction((i + count - 1) % count, i);
            var outgoing = i == count - 1 && !closed ? incoming : Direction(i, (i + 1) % count);
            var denominator = 1 + incoming.X * outgoing.X + incoming.Y * outgoing.Y;
            if (denominator < 0.125)
                throw new ArgumentException("The offset would make an excessively sharp join. Split the edge at that corner.");
            var p = source[i];
            if (!double.IsFinite(p.Z)) throw new ArgumentException("The edge has an invalid elevation.");
            result[i] = new(p.X - signedDistance * (incoming.Y + outgoing.Y) / denominator,
                p.Y + signedDistance * (incoming.X + outgoing.X) / denominator, p.Z);
        }

        var segments = closed ? count : count - 1;
        for (var i = 0; i < segments; i++)
        {
            var next = (i + 1) % count;
            var direction = Direction(i, next);
            var forward = (result[next].X - result[i].X) * direction.X +
                (result[next].Y - result[i].Y) * direction.Y;
            if (!double.IsFinite(forward) || forward <= 1e-9)
                throw new ArgumentException("The offset collapses or reverses a segment. Use a smaller distance or split the edge.");
        }

        LineString Plan(IReadOnlyList<TrianglePoint> points)
        {
            var coordinates = points.Select(p => new Coordinate(p.X, p.Y)).ToList();
            if (closed) coordinates.Add(coordinates[0].Copy());
            return new GeometryFactory().CreateLineString(coordinates.ToArray());
        }

        var sourcePlan = Plan(source);
        var resultPlan = Plan(result);
        if (!sourcePlan.IsSimple || !resultPlan.IsSimple || sourcePlan.Intersects(resultPlan))
            throw new ArgumentException("The edge or its offset intersects itself or the reference edge. Split the edge or use a smaller distance.");
        return result;
    }
}
