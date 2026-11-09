using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilAI;

public sealed partial class Plugin
{
    [CommandMethod("CIVILAICREATETIN", CommandFlags.Modal)]
    public void CreateProposedTin()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;
        var editor = document.Editor;

        try
        {
            var selection = editor.GetSelection(new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect feature lines for the proposed surface: "
            });
            if (selection.Status != PromptStatus.OK) return;
            var ids = selection.Value.GetObjectIds().Distinct().ToArray();
            if (ids.Length == 0) return;

            var named = editor.GetString(new PromptStringOptions("\nProposed surface name: ") { AllowSpaces = true });
            if (named.Status != PromptStatus.OK) return;
            var name = named.StringResult.Trim();
            if (name.Length == 0)
            {
                editor.WriteMessage("\nEnter a nonempty surface name.");
                return;
            }

            var database = document.Database;
            var civil = CivilDocument.GetCivilDocument(database);
            var sources = new List<string>();
            using (var read = database.TransactionManager.StartOpenCloseTransaction())
            {
                CheckSurfaceName(read, civil, name);
                foreach (var id in ids)
                {
                    var line = RequireLocalFeatureLine(read, id);
                    sources.Add(string.IsNullOrWhiteSpace(line.Name)
                        ? line.Handle.ToString()
                        : $"{line.Name} ({line.Handle})");
                }
            }

            var tolerance = editor.GetDouble(new PromptDoubleOptions(
                "\nArc mid-ordinate distance in drawing units <0.01>: ")
            {
                AllowNegative = false, AllowZero = false, AllowNone = true,
                DefaultValue = 0.01, UseDefaultValue = true
            });
            if (tolerance.Status != PromptStatus.OK && tolerance.Status != PromptStatus.None) return;
            var midOrdinate = tolerance.Status == PromptStatus.None ? 0.01 : tolerance.Value;
            if (!double.IsFinite(midOrdinate) || midOrdinate <= 0) return;

            editor.WriteMessage($"\nPreview: Create TIN '{name}' with {ids.Length} standard breakline source(s), default surface style, no outer boundary, and arc mid-ordinate distance {midOrdinate:G} drawing units.");
            editor.WriteMessage($"\nSources: {string.Join(", ", sources.Take(12))}{(sources.Count > 12 ? $", plus {sources.Count - 12} more" : "")}.");
            var confirmation = new PromptKeywordOptions("\nCreate this surface?") { AllowNone = true };
            confirmation.Keywords.Add("Create");
            confirmation.Keywords.Add("Cancel");
            confirmation.Keywords.Default = "Cancel";
            var answer = agentExecuting ? null : editor.GetKeywords(confirmation);
            if (answer is not null && (answer.Status != PromptStatus.OK || answer.StringResult != "Create")) return;

            int triangleCount;
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                CheckSurfaceName(transaction, civil, name);
                foreach (var id in ids) RequireLocalFeatureLine(transaction, id);

                var surfaceId = TinSurface.Create(database, name);
                var surface = (TinSurface)transaction.GetObject(surfaceId, OpenMode.ForWrite);
                surface.BreaklinesDefinition.AddStandardBreaklines(
                    new ObjectIdCollection(ids), midOrdinate, 0, 0, 0);
                surface.Rebuild();
                triangleCount = surface.GetTriangles(false).Count;
                if (triangleCount == 0)
                    throw new InvalidOperationException("The selected feature lines produced no triangles. Select lines that enclose or span an area.");
                transaction.Commit();
            }

            ClearTriangleLocations();
            lastSnapshot = null;
            workspace?.SetSnapshot(null);
            panel?.InvalidateDrawing($"Created TIN '{name}' with {triangleCount} visible triangle(s) from {ids.Length} feature line(s).");
            editor.WriteMessage($"\nCreated TIN '{name}' with {triangleCount} visible triangle(s) from {ids.Length} feature line(s).");
        }
        catch (System.Exception ex)
        {
            editor.WriteMessage($"\nCivil AI surface creation stopped: {ex.Message}");
        }
    }

    private static void CheckSurfaceName(Transaction transaction, CivilDocument civil, string name)
    {
        foreach (ObjectId id in civil.GetSurfaceIds())
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface surface &&
                string.Equals(surface.Name, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"A surface named '{name}' already exists.");
        }
    }

    private static FeatureLine RequireLocalFeatureLine(Transaction transaction, ObjectId id)
    {
        if (transaction.GetObject(id, OpenMode.ForRead) is not FeatureLine line || line.IsReferenceObject || line.IsReadOnlyReferenceObject)
            throw new InvalidOperationException("Select only feature lines owned by the current drawing.");
        return line;
    }
}
