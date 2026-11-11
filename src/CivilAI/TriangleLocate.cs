using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.Civil.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilAI;

public sealed partial class Plugin
{
    private static Document? triangleDocument;
    private static ObjectId triangleSource;
    private static Dictionary<string, Point3d[]> triangleLocations = new();
    private static bool locating;
    private static void ClearTriangleLocations()
    {
        triangleDocument = null;
        triangleSource = ObjectId.Null;
        triangleLocations.Clear();
    }
    private static async Task LocateTriangleAsync(string key)
    {
        if (locating) return;
        var requested = triangleDocument;
        var source = triangleSource;
        if (requested is null || Application.DocumentManager.MdiActiveDocument != requested || !triangleLocations.TryGetValue(key, out var points))
        { panel?.Stage("Triangle location expired. Run CIVILAIFINDSLOPES again."); return; }
        locating = true;
        try
        {
            await Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                try
                {
                    if (Application.DocumentManager.MdiActiveDocument != requested || triangleDocument != requested || triangleSource != source)
                        throw new InvalidOperationException("Drawing context changed.");
                    var editor = requested.Editor;
                    if (!requested.Database.TileMode || Convert.ToInt32(Application.GetSystemVariable("CVPORT")) == 1)
                    { panel?.Stage("Locate is available in the Model tab only."); return Task.CompletedTask; }
                    using var documentLock = requested.LockDocument();
                    // Revalidate the triangle against current geometry before showing cached coordinates.
                    using (var transaction = requested.Database.TransactionManager.StartOpenCloseTransaction())
                    {
                        var surface = (TinSurface)transaction.GetObject(source, OpenMode.ForRead);
                        if (surface.IsReferenceObject && (!surface.IsReferenceValid || surface.IsReferenceStale || !surface.IsReferencedSourceExisting))
                            throw new InvalidOperationException("Reference unavailable.");
                        using var current = surface.FindTriangleAtXY(points.Average(p => p.X), points.Average(p => p.Y));
                        using var a = current.Vertex1; using var b = current.Vertex2; using var c = current.Vertex3;
                        var actual = new[] { a.Location, b.Location, c.Location };
                        if (!current.IsVisible || !points.All(p => actual.Any(q => p.DistanceTo(q) <= 1e-8)))
                            throw new InvalidOperationException("Triangle changed; rescan.");
                    }
                    using var previous = editor.GetCurrentView();
                    using var view = editor.GetCurrentView();
                    var viewports = new IntegerCollection();
                    var lines = new List<Line>();
                    try
                    {
                        var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X);
                        var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
                        var ratio = previous.Width / previous.Height;
                        if (!double.IsFinite(ratio) || ratio <= 0) throw new InvalidOperationException("Invalid viewport.");
                        view.ViewDirection = Vector3d.ZAxis; view.ViewTwist = 0; view.Target = Point3d.Origin;
                        view.PerspectiveEnabled = false;
                        view.CenterPoint = new Point2d(minX / 2 + maxX / 2, minY / 2 + maxY / 2);
                        view.Height = Math.Max(1, Math.Max((maxY-minY)*1.6, (maxX-minX)*1.6/ratio));
                        view.Width = view.Height * ratio;
                        editor.SetCurrentView(view);
                        for (var i = 0; i < 3; i++)
                        {
                            var line = new Line(points[i], points[(i+1)%3]) { ColorIndex = 1, LineWeight = LineWeight.LineWeight050 };
                            lines.Add(line);
                            if (!TransientManager.CurrentTransientManager.AddTransient(line, TransientDrawingMode.DirectTopmost, 128, viewports))
                                throw new InvalidOperationException("Transient display unavailable.");
                        }
                        editor.UpdateScreen();
                        editor.GetString("\nCivil AI temporary triangle outline (red), WCS top view. Press Enter or Esc to clear and restore view: ");
                    }
                    finally
                    {
                        foreach (var line in lines)
                        {
                            try { TransientManager.CurrentTransientManager.EraseTransient(line, viewports); }
                            catch (System.Exception) { panel?.Stage("Transient cleanup failed; regenerate the view to clear display artifacts."); }
                            finally { line.Dispose(); }
                        }
                        editor.SetCurrentView(previous);
                        editor.UpdateScreen();
                    }
                }
                catch (System.Exception ex) { panel?.Stage($"Locate ended or unavailable ({ex.GetType().Name}). If the surface changed, run CIVILAIFINDSLOPES again."); }
                return Task.CompletedTask;
            }, null);
        }
        finally { locating = false; }
    }
}


