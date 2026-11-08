using NetTopologySuite.Geometries;
namespace CivilAI;
internal static class PlanCurve
{
    public static Coordinate[] Read(IReadOnlyList<Coordinate> points, bool closed, Func<int,double> bulgeAt, double tolerance, out double length)
    {
        if(points.Count<2 || points.Count>10000 || !double.IsFinite(tolerance) || tolerance<=0) throw new ArgumentException("Invalid curve size or chord tolerance.");
        var result=new List<Coordinate>(); length=0;
        var count=points.Count;
        if(closed && points[0].Equals2D(points[^1])) count--;
        var segments=closed ? count : count-1;
        for(var i=0;i<segments;i++)
        {
            var a=points[i]; var b=points[(i+1)%count]; var bulge=bulgeAt(i);
            if(!double.IsFinite(a.X)||!double.IsFinite(a.Y)||!double.IsFinite(b.X)||!double.IsFinite(b.Y)||!double.IsFinite(bulge)) throw new ArgumentException("Nonfinite curve data.");
            var dx=b.X-a.X; var dy=b.Y-a.Y; var chord=Math.Sqrt(dx*dx+dy*dy);
            if(chord<=0) throw new ArgumentException("Repeated plan vertex or vertical segment is unsupported.");
            result.Add(a.Copy());
            if(bulge==0) { length+=chord; continue; }
            var sweep=4*Math.Atan(bulge); var radius=chord*(1+bulge*bulge)/(4*Math.Abs(bulge));
            length+=radius*Math.Abs(sweep);
            var maxAngle=2*Math.Acos(Math.Clamp(1-tolerance/radius,-1,1));
            var steps=Math.Ceiling(Math.Abs(sweep)/Math.Min(Math.PI/2,maxAngle));
            if(!double.IsFinite(steps)||steps>10000||result.Count+steps>10000) throw new ArgumentException("Curve exceeds 10,000 points at this chord tolerance. Use a larger tolerance.");
            var offset=chord*(1-bulge*bulge)/(4*bulge);
            var cx=a.X+dx/2-dy/chord*offset; var cy=a.Y+dy/2+dx/chord*offset;
            var start=Math.Atan2(a.Y-cy,a.X-cx);
            for(var j=1;j<(int)steps;j++) { var angle=start+sweep*j/steps; result.Add(new(cx+radius*Math.Cos(angle),cy+radius*Math.Sin(angle))); }
        }
        result.Add((closed ? points[0] : points[^1]).Copy());
        if(result.Count>10000) throw new ArgumentException("Curve exceeds 10,000 points.");
        return result.ToArray();
    }
}
