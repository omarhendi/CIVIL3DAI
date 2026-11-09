using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilAI;

public sealed partial class Plugin
{
    [CommandMethod("CIVILAIBOUNDARY", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AddTinBoundary()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;
        var editor = document.Editor;
        var database = document.Database;

        try
        {
            const double midOrdinate = 0.01;
            var surfaceId = ObjectId.Null;
            var implied = editor.SelectImplied();
            if (implied.Status == PromptStatus.OK)
            {
                using var read = database.TransactionManager.StartOpenCloseTransaction();
                var surfaces = implied.Value.GetObjectIds()
                    .Where(id => read.GetObject(id, OpenMode.ForRead) is TinSurface).ToArray();
                if (surfaces.Length == 1) surfaceId = surfaces[0];
            }

            if (surfaceId.IsNull)
            {
                var picked = editor.GetEntity("\nSelect the TIN surface to trim: ");
                if (picked.Status != PromptStatus.OK) return;
                surfaceId = picked.ObjectId;
            }

            string name;
            using (var read = database.TransactionManager.StartOpenCloseTransaction())
                name = RequireEditableTin(read, surfaceId).Name;

            var outline = editor.GetEntity($"\nSelect a closed polyline or feature line for '{name}': ");
            if (outline.Status != PromptStatus.OK) return;
            using (var read = database.TransactionManager.StartOpenCloseTransaction())
            {
                var entity = read.GetObject(outline.ObjectId, OpenMode.ForRead);
                var plan = ReadOffsetEdge(entity, read, true, midOrdinate * 100, out var closed);
                if (!closed) throw new ArgumentException("The boundary outline must be closed.");
                AreaGeometry.Boundary(plan.Select(p => new NetTopologySuite.Geometries.Coordinate(p.X, p.Y)).ToArray());
            }

            editor.WriteMessage($"\nPreview: apply an outer boundary to '{name}' from outline {outline.ObjectId.Handle}. The new outline becomes the active outer boundary.");
            var confirmation = new PromptKeywordOptions("\nApply boundary?") { AllowNone = true };
            confirmation.Keywords.Add("Apply");
            confirmation.Keywords.Add("Cancel");
            confirmation.Keywords.Default = "Cancel";
            var answer = editor.GetKeywords(confirmation);
            if (answer.Status != PromptStatus.OK || answer.StringResult != "Apply") return;

            int triangles;
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var surface = RequireEditableTin(transaction, surfaceId);
                surface.UpgradeOpen();
                surface.BoundariesDefinition.AddBoundaries(
                    new ObjectIdCollection(new[] { outline.ObjectId }), midOrdinate, SurfaceBoundaryType.Outer, true);
                surface.Rebuild();
                triangles = surface.GetTriangles(false).Count;
                if (triangles == 0)
                    throw new InvalidOperationException("The outline leaves no visible triangles. Pick an outline that encloses part of the surface.");
                transaction.Commit();
            }

            ClearTriangleLocations();
            lastSnapshot = null;
            workspace?.SetSnapshot(null);
            var result = $"Applied the outer boundary to '{name}'. {triangles} visible triangle(s) remain.";
            panel?.InvalidateDrawing(result);
            editor.WriteMessage("\n" + result);
        }
        catch (System.Exception ex)
        {
            var message = $"Surface boundary stopped: {ex.Message}";
            panel?.InvalidateDrawing(message);
            editor.WriteMessage("\n" + message);
        }
    }

    private static TinSurface RequireEditableTin(Transaction transaction, ObjectId id)
    {
        if (transaction.GetObject(id, OpenMode.ForRead) is not TinSurface surface ||
            surface.IsReferenceObject || surface.IsReadOnlyReferenceObject || surface.Lock)
            throw new InvalidOperationException("Select an unlocked TIN owned by the active drawing.");
        return surface;
    }
}
