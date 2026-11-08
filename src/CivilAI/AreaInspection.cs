using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using NetTopologySuite.Geometries;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
namespace CivilAI;
public sealed partial class Plugin
{
    [CommandMethod("CIVILAIAREA", CommandFlags.Modal)]
    public void AssessArea()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        ClearTriangleLocations();
        try
        {
            var surfaceOptions = new PromptEntityOptions("\nSelect pavement/floor TIN surface: ");
            surfaceOptions.SetRejectMessage("\nSelect a TIN surface."); surfaceOptions.AddAllowedClass(typeof(TinSurface),true);
            var surfacePick = ed.GetEntity(surfaceOptions); if (surfacePick.Status != PromptStatus.OK) return;
            var boundaryPick = ed.GetEntity("\nSelect closed pavement boundary (feature line or 2D/3D polyline): "); if (boundaryPick.Status != PromptStatus.OK) return;
            var breakPick = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect optional feature-line/polyline breaklines; Enter for whole area: " });
            if (breakPick.Status == PromptStatus.Cancel) return;
            if (breakPick.Status != PromptStatus.OK && breakPick.Status != PromptStatus.Error) return;
            var low = ed.GetDouble(new PromptDoubleOptions("\nMinimum slope percent: ") { AllowNegative=false, AllowZero=true }); if(low.Status != PromptStatus.OK) return;
            var high = ed.GetDouble(new PromptDoubleOptions("\nMaximum slope percent: ") { AllowNegative=false, AllowZero=true }); if(high.Status != PromptStatus.OK) return;
            if (!double.IsFinite(low.Value) || !double.IsFinite(high.Value) || low.Value > high.Value) throw new ArgumentException("Invalid limits.");
            var chord = ed.GetDouble(new PromptDoubleOptions("\nMaximum arc chord deviation in drawing units <0.001>: ") { AllowNegative=false, AllowZero=false, DefaultValue=0.001, UseDefaultValue=true });
            if(chord.Status != PromptStatus.OK) return;
            var items = new List<DrawingItem>();
            int read = 0, skipped = 0;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var surface = (TinSurface)tr.GetObject(surfacePick.ObjectId,OpenMode.ForRead);
                if (surface.IsReferenceObject && (!surface.IsReferenceValid || surface.IsReferenceStale || !surface.IsReferencedSourceExisting)) throw new ArgumentException("Reference is invalid, stale or missing.");
                Coordinate[] ReadPolyline(ObjectId id, bool mustClose)
                {
                    var entity = tr.GetObject(id,OpenMode.ForRead);
                    var points = new List<Coordinate>(); bool closed;
                    if(entity is FeatureLine feature)
                    {
                        if(feature.IsReferenceObject && (!feature.IsReferenceValid || feature.IsReferenceStale || !feature.IsReferencedSourceExisting)) throw new ArgumentException("Feature-line reference is invalid, stale or missing.");
                        closed=feature.Closed;
                        if(mustClose && !closed) throw new ArgumentException("The pavement boundary feature line must be closed.");
                        var pi=feature.GetPoints(Autodesk.Civil.FeatureLinePointType.PIPoint);
                        foreach(Autodesk.AutoCAD.Geometry.Point3d point in pi) points.Add(new(point.X,point.Y));
                        var sampled=PlanCurve.Read(points,closed,feature.GetBulge,chord.Value,out var length);
                        if(!double.IsFinite(feature.Length2D) || Math.Abs(length-feature.Length2D)>Math.Max(1,feature.Length2D)*1e-7) throw new ArgumentException("Feature-line segments do not match its plan length; smoothed or unsupported geometry. No approximation published.");
                        return sampled;
                    }
                    if (entity is Polyline p)
                    {
                        closed = p.Closed;
                        for(var i=0;i<p.NumberOfVertices;i++)
                        {
                            if(p.GetBulgeAt(i)!=0) throw new ArgumentException("Curved polyline segments are not supported in this version.");
                            var point=p.GetPoint3dAt(i); points.Add(new(point.X,point.Y));
                        }
                    }
                    else if(entity is Polyline3d p3)
                    {
                        if(p3.PolyType != Poly3dType.SimplePoly) throw new ArgumentException("Spline-fit 3D polylines are unsupported.");
                        closed=p3.Closed;
                        foreach(ObjectId vertexId in p3) { var point=((PolylineVertex3d)tr.GetObject(vertexId,OpenMode.ForRead)).Position; points.Add(new(point.X,point.Y)); }
                    }
                    else throw new ArgumentException("Select feature line or 2D/3D polylines. Feature lines and other curve types are not yet supported.");
                    if(points.Count<2 || points.Count>10000 || (mustClose && !closed)) throw new ArgumentException("Boundary must be closed; each polyline needs 2–10,000 vertices.");
                    if(closed && !points[0].Equals2D(points[^1])) points.Add(points[0].Copy());
                    return points.ToArray();
                }
                var boundary = AreaGeometry.Boundary(ReadPolyline(boundaryPick.ObjectId,true));
                var ids = breakPick.Status == PromptStatus.OK ? breakPick.Value.GetObjectIds().Distinct().Where(id=>id!=boundaryPick.ObjectId).ToArray() : Array.Empty<ObjectId>();
                if(ids.Length>100) throw new ArgumentException("Select at most 100 breakline polylines.");
                var zones = AreaGeometry.Partition(boundary,ids.Select(id=>ReadPolyline(id,false)));
                var totals = zones.Select(_=>new AreaTotals()).ToArray();
                var clock=System.Diagnostics.Stopwatch.StartNew();
                foreach(TinSurfaceTriangle triangle in surface.GetTriangles(false))
                using(triangle)
                {
                    if(++read>100000 || clock.Elapsed.TotalSeconds>15) throw new ArgumentException("Assessment exceeded its time/triangle limit. Select a smaller area/surface; no partial totals published.");
                    using var av=triangle.Vertex1; using var bv=triangle.Vertex2; using var cv=triangle.Vertex3;
                    var a=av.Location; var b=bv.Location; var c=cv.Location;
                    if(!TriangleSlope.TryCalculate(new(a.X,a.Y,a.Z),new(b.X,b.Y,b.Z),new(c.X,c.Y,c.Z),out var slope,out _)) { skipped++; continue; }
                    var trianglePlan=AreaGeometry.Boundary(new[] { new Coordinate(a.X,a.Y),new Coordinate(b.X,b.Y),new Coordinate(c.X,c.Y) });
                    for(var i=0;i<zones.Count;i++) if(zones[i].EnvelopeInternal.Intersects(trianglePlan.EnvelopeInternal))
                        totals[i].Add(zones[i].Intersection(trianglePlan).Area,slope,low.Value,high.Value);
                }
                var overall = new AreaTotals();
                foreach(var t in totals) { overall.Covered+=t.Covered; overall.Below+=t.Below; overall.Above+=t.Above; overall.Weighted+=t.Weighted; overall.Minimum=Math.Min(overall.Minimum,t.Minimum); overall.Maximum=Math.Max(overall.Maximum,t.Maximum); }
                void Add(string name, AreaTotals t, double area)
                {
                    var rows=t.Rows(area,low.Value,high.Value).ToList();
                    if(skipped>0)
                    {
                        rows[0] = new PropertyValue("Assessment", "INCOMPLETE — unusable surface data; no whole-area verdict.");
                        rows.Insert(0,new("Data quality", $"{skipped} unusable triangles in surface scan; assessment is provisional, not a pass."));
                    }
                    rows.Insert(0,new("Curve approximation", FormattableString.Invariant($"Feature-line arcs use maximum chord deviation {chord.Value} drawing units; areas near curves are approximate. Straight polyline support unchanged.")));
                    rows.Insert(0,new("Subdivision", $"{zones.Count} closed region(s) from boundary and selected breaklines. Dangling/open lines that do not close a region do not subdivide it. No automatic surface-breakline extraction or gap snapping."));
                    items.Add(new DrawingItem("Area assessment",name,rows));
                }
                Add(surface.Name+" — Whole pavement",overall,boundary.Area);
                if(zones.Count>1) for(var i=0;i<zones.Count;i++) Add($"Subarea {i+1}",totals[i],zones[i].Area);
            }
            EnsurePanel(); palette!.Visible=true; lastSnapshot=null; workspace?.SetSnapshot(null);
            panel!.Show(new DrawingSnapshot(doc.Name,items,skipped,read,Array.Empty<string>()));
            ed.WriteMessage("\nArea assessment ready in Drawing Inspector → All Properties.\n");
        }
        catch(System.Exception ex)
        {
            panel?.InvalidateDrawing($"Area assessment unavailable: {(ex is ArgumentException ? ex.Message : ex.GetType().Name)}. No partial assessment retained.");
            ed.WriteMessage($"\nArea assessment stopped: {(ex is ArgumentException ? ex.Message : ex.GetType().Name)}. No drawing modifications made.\n");
        }
    }
}


