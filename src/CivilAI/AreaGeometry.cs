using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;
namespace CivilAI;

internal static class AreaGeometry
{
    internal static readonly GeometryFactory Factory = new();
    public static Polygon Boundary(Coordinate[] points)
    {
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw new ArgumentException("Nonfinite coordinates.");
        var ring = points[0].Equals2D(points[^1]) ? points : points.Append(points[0].Copy()).ToArray();
        var polygon = Factory.CreatePolygon(ring);
        if (!polygon.IsValid || polygon.Area <= 0) throw new ArgumentException("Boundary must be a simple closed polygon with positive plan area.");
        return polygon;
    }
    public static List<Polygon> Partition(Polygon boundary, IEnumerable<Coordinate[]> lines)
    {
        var edges = new List<Geometry> { boundary.Boundary };
        foreach (var line in lines) edges.Add(Factory.CreateLineString(line).Intersection(boundary));
        var polygonizer = new Polygonizer();
        polygonizer.Add(UnaryUnionOp.Union(edges));
        var zones = polygonizer.GetPolygons().Where(p => boundary.Covers(p.InteriorPoint)).OrderBy(p => p.EnvelopeInternal.MinX).ThenBy(p => p.EnvelopeInternal.MinY).ToList();
        if (zones.Count == 0 || zones.Count > 100 || Math.Abs(zones.Sum(p => p.Area) - boundary.Area) > Math.Max(1, boundary.Area)*1e-8)
            throw new ArgumentException("Cannot construct a complete partition (maximum 100 subareas). Check boundary and breaklines.");
        return zones;
    }
}
internal sealed class AreaTotals
{
    public double Covered, Below, Above, Weighted, Minimum = double.PositiveInfinity, Maximum;
    public void Add(double area, double slope, double lower, double upper)
    {
        if (!double.IsFinite(area) || !double.IsFinite(slope) || area < 0 || slope < 0) throw new ArgumentException("Invalid area or slope.");
        if (area == 0) return;
        Covered += area; Weighted += area*slope; Minimum = Math.Min(Minimum,slope); Maximum = Math.Max(Maximum,slope);
        if (slope < lower) Below += area;
        if (slope > upper) Above += area;
    }
    public IReadOnlyList<PropertyValue> Rows(double area, double lower, double upper)
    {
        string F(double x) => x.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
        var tolerance = Math.Max(1,area)*1e-8;
        var available = Covered > 0 && Covered <= area+tolerance;
        return new[] {
            new PropertyValue("Assessment", !available ? "Unavailable — no usable coverage or overlapping coverage." : Covered < area-tolerance ? "INCOMPLETE coverage — no whole-area verdict." : Below+Above > tolerance ? "Outside configured limits in part of this area." : "Covered area within configured limits."),
            new PropertyValue("Limits", $"{F(lower)}–{F(upper)} % (inclusive)"),
            new PropertyValue("Plan area", F(area)+" drawing units²"),
            new PropertyValue("Assessed coverage", F(Covered)+" drawing units²"),
            new PropertyValue("Unassessed area", F(Math.Max(0,area-Covered))+" drawing units²"),
            new PropertyValue("Below minimum", F(Below)+" drawing units²"),
            new PropertyValue("Above maximum", F(Above)+" drawing units²"),
            new PropertyValue("Outside limits / assessed area", available ? F(100*(Below+Above)/Covered)+" %" : "Unavailable"),
            new PropertyValue("Slope min / max", available ? F(Minimum)+" / "+F(Maximum)+" %" : "Unavailable"),
            new PropertyValue("Plan-area-weighted mean slope", available ? F(Weighted/Covered)+" %" : "Unavailable"),
            new PropertyValue("Scope", "Steepest planar slope magnitude; not longitudinal grade or crossfall. No drainage or compliance verdict. No triangle-level results. Boundaries and selected breaklines projected to WCS XY; refresh by rerunning the assessment.") };
    }
}
