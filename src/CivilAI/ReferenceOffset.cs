using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CivilAI.Workspace;
using Autodesk.Civil.DatabaseServices;
using NetTopologySuite.Geometries;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilAI;

public sealed partial class Plugin
{
    [CommandMethod("CIVILAIOFFSETLEVELS", CommandFlags.Modal)]
    public void OffsetReferenceLevels()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;
        var editor = document.Editor;
        var requestedMeters = ConsumeTaskDistance(document, DrawingTask.OffsetLevels);
        try
        {
            var edge = editor.GetEntity("\nSelect the original reference edge (line, polyline, or feature line): ");
            if (edge.Status != PromptStatus.OK) return;

            var source = new PromptKeywordOptions("\nTake elevations from");
            source.Keywords.Add("Surface");
            source.Keywords.Add("Edge");
            source.Keywords.Default = "Surface";
            source.AllowNone = true;
            var mode = editor.GetKeywords(source);
            if (mode.Status != PromptStatus.OK && mode.Status != PromptStatus.None) return;
            var surfaceId = ObjectId.Null;
            if (mode.StringResult != "Edge")
            {
                var surfacePick = editor.GetEntity("\nSelect the reference TIN surface (road, datum, or survey): ");
                if (surfacePick.Status != PromptStatus.OK) return;
                surfaceId = surfacePick.ObjectId;
            }

            var database = document.Database;
            var offset = ResolveTaskDistance(editor, database, requestedMeters, 0.15d, "Horizontal offset");
            if (!double.IsFinite(offset) || offset <= 0) return;

            var pickedSide = editor.GetPoint("\nClick on the side where the new feature line should go: ");
            if (pickedSide.Status != PromptStatus.OK) return;
            var sidePoint = pickedSide.Value.TransformBy(editor.CurrentUserCoordinateSystem);

            bool closed;
            List<TrianglePoint> samples;
            string sourceLabel;
            using (var read = database.TransactionManager.StartOpenCloseTransaction())
            {
                var surface = surfaceId.IsNull ? null : (read.GetObject(surfaceId, OpenMode.ForRead) as TinSurface
                    ?? throw new ArgumentException("Select a TIN surface as the elevation reference."));
                if (surface is { IsReferenceObject: true } &&
                    (!surface.IsReferenceValid || surface.IsReferenceStale || !surface.IsReferencedSourceExisting))
                    throw new ArgumentException("The referenced TIN is missing, invalid, or stale.");

                var entity = read.GetObject(edge.ObjectId, OpenMode.ForRead);
                var edgePoints = ReadOffsetEdge(entity, read, surface is not null, offset, out closed);
                samples = surface is null ? edgePoints : SampleReferenceTin(surface, edgePoints, closed);
                sourceLabel = surface?.Name ?? "selected edge";
            }

            if (samples.Count < (closed ? 3 : 2))
                throw new ArgumentException("The reference edge has too few usable points.");
            var signedOffset = PickOffsetSide(samples, closed, sidePoint) * offset;
            var vertices = ReferenceOffsetGeometry.Offset(samples, closed, signedOffset);
            editor.WriteMessage($"\nPreview: one feature line, {offset:G} drawing units horizontally from the selected edge. Elevations copied from '{sourceLabel}' at the original edge. {vertices.Length} points, Z {vertices.Min(p => p.Z):G} to {vertices.Max(p => p.Z):G}.");
            var confirmation = new PromptKeywordOptions("\nCreate the offset feature line?") { AllowNone = true };
            confirmation.Keywords.Add("Create");
            confirmation.Keywords.Add("Cancel");
            confirmation.Keywords.Default = "Cancel";
            var answer = editor.GetKeywords(confirmation);
            if (answer.Status != PromptStatus.OK || answer.StringResult != "Create") return;

            ObjectId featureId;
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
                var points = new Point3dCollection(vertices.Select(p => new Point3d(p.X, p.Y, p.Z)).ToArray());
                using var temporary = new Polyline3d(Poly3dType.SimplePoly, points, closed);
                space.AppendEntity(temporary);
                transaction.AddNewlyCreatedDBObject(temporary, true);
                featureId = FeatureLine.Create(string.Empty, temporary.ObjectId);
                if (!temporary.IsErased) temporary.Erase();
                transaction.Commit();
            }

            ClearTriangleLocations();
            lastSnapshot = null;
            workspace?.SetSnapshot(null);
            panel?.InvalidateDrawing($"Created offset feature line {featureId.Handle} with {vertices.Length} points.");
            editor.WriteMessage($"\nCreated feature line {featureId.Handle} with {vertices.Length} points.");
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage($"\nCivil AI offset feature line stopped: {ex.Message}");
        }
    }

    private static List<TrianglePoint> ReadOffsetEdge(Autodesk.AutoCAD.DatabaseServices.DBObject entity, Transaction read, bool fromSurface,
        double offset, out bool closed)
    {
        closed = false;
        var points = new List<TrianglePoint>();
        void Add(Point3d point) => points.Add(new(point.X, point.Y, point.Z));
        if (entity is Line line)
        {
            Add(line.StartPoint);
            Add(line.EndPoint);
        }
        else if (entity is Polyline3d poly3d && poly3d.PolyType == Poly3dType.SimplePoly)
        {
            closed = poly3d.Closed;
            foreach (ObjectId id in poly3d) Add(((PolylineVertex3d)read.GetObject(id, OpenMode.ForRead)).Position);
        }
        else if (entity is FeatureLine feature)
        {
            if (feature.IsReferenceObject &&
                (!feature.IsReferenceValid || feature.IsReferenceStale || !feature.IsReferencedSourceExisting))
                throw new ArgumentException("The reference feature line is missing, invalid, or stale.");
            closed = feature.Closed;
            if (fromSurface)
            {
                foreach (Point3d p in feature.GetPoints(Autodesk.Civil.FeatureLinePointType.PIPoint)) Add(p);
                return ReadOffsetPlan(points, closed, feature.GetBulge, offset, feature.Length2D);
            }

            var piCount = feature.PIPointsCount;
            for (var i = 0; i < (closed ? piCount : piCount - 1); i++)
                if (feature.GetBulge(i) != 0)
                    throw new ArgumentException("Curved feature lines require Surface mode for elevation transfer.");
            foreach (Point3d p in feature.GetPoints(Autodesk.Civil.FeatureLinePointType.AllPoints)) Add(p);
        }
        else if (entity is Polyline polyline)
        {
            if (Math.Abs(polyline.Normal.DotProduct(Vector3d.ZAxis) - 1) > 1e-8)
                throw new ArgumentException("Select a polyline in the WCS horizontal plane.");
            closed = polyline.Closed;
            for (var i = 0; i < polyline.NumberOfVertices; i++) Add(polyline.GetPoint3dAt(i));
            var plan = ReadOffsetPlan(points, closed, polyline.GetBulgeAt, offset, polyline.Length);
            return fromSurface ? plan : plan.Select(p => new TrianglePoint(p.X, p.Y, polyline.Elevation)).ToList();
        }
        else throw new ArgumentException("Select a line, lightweight polyline, simple 3D polyline, or feature line.");

        if (closed && points.Count > 1 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        return points;
    }

    private static List<TrianglePoint> ReadOffsetPlan(List<TrianglePoint> points, bool closed,
        Func<int, double> bulgeAt, double offset, double expectedLength)
    {
        var plan = PlanCurve.Read(points.Select(p => new Coordinate(p.X, p.Y)).ToArray(),
            closed, bulgeAt, offset / 100, out var length);
        if (!double.IsFinite(expectedLength) || Math.Abs(length - expectedLength) > Math.Max(1, expectedLength) * 1e-7)
            throw new ArgumentException("The edge geometry does not match its plan length.");
        return plan.Take(closed ? plan.Length - 1 : plan.Length).Select(p => new TrianglePoint(p.X, p.Y, 0)).ToList();
    }

    private static List<TrianglePoint> SampleReferenceTin(TinSurface surface, List<TrianglePoint> edge, bool closed)
    {
        var result = new List<TrianglePoint>();
        if (edge.Count < (closed ? 3 : 2)) throw new ArgumentException("The reference edge is too short.");
        var segments = closed ? edge.Count : edge.Count - 1;
        double Elevation(double x, double y)
        {
            try { return surface.FindElevationAtXY(x, y); }
            catch (Autodesk.Civil.PointNotOnEntityException)
            {
                throw new ArgumentException($"The original edge is outside '{surface.Name}' at X={x:F3}, Y={y:F3}.");
            }
        }

        for (var i = 0; i < segments; i++)
        {
            var a = edge[i];
            var b = edge[(i + 1) % edge.Count];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (!double.IsFinite(lengthSquared) || lengthSquared <= 1e-18)
                throw new ArgumentException("The reference edge has a repeated plan point.");

            var stations = new List<double> { 0, 1 };
            foreach (Point3d p in surface.SampleElevations(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0)))
            {
                var station = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
                if (station > 0 && station < 1) stations.Add(station);
            }
            stations.Sort();
            var previous = -1d;
            foreach (var station in stations)
            {
                if (station - previous <= 1e-9) continue;
                if (previous >= 0)
                {
                    var middle = (previous + station) / 2;
                    Elevation(a.X + dx * middle, a.Y + dy * middle);
                }
                var x = a.X + dx * station;
                var y = a.Y + dy * station;
                var z = Elevation(x, y);
                if (station != 1 || (!closed && i == segments - 1))
                    result.Add(new(x, y, z));
                previous = station;
                if (result.Count > 25000) throw new ArgumentException("The reference produces more than 25,000 points. Split the edge.");
            }
        }
        return result;
    }

    private static double PickOffsetSide(IReadOnlyList<TrianglePoint> points, bool closed, Point3d pick)
    {
        var bestDistance = double.PositiveInfinity;
        var side = 0d;
        for (var i = 0; i < (closed ? points.Count : points.Count - 1); i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var px = pick.X - a.X;
            var py = pick.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 1e-18) continue;
            var t = Math.Clamp((px * dx + py * dy) / lengthSquared, 0, 1);
            var x = px - t * dx;
            var y = py - t * dy;
            var d = x * x + y * y;
            if (d >= bestDistance) continue;
            bestDistance = d;
            side = (dx * py - dy * px) / Math.Sqrt(lengthSquared);
        }

        if (!double.IsFinite(side) || Math.Abs(side) <= 1e-8)
            throw new ArgumentException("Pick clearly to one side of the edge, near a segment.");
        return Math.Sign(side);
    }
}
