using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CivilAI.Workspace;
using Autodesk.Civil.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilAI;

public sealed partial class Plugin
{
    private sealed record SpotGrid(ObjectId SurfaceId, ObjectId[] Labels);
    private static readonly Dictionary<Document, SpotGrid> spotGrids = new();

    private static void ForgetSpotGrid(object sender, DocumentCollectionEventArgs args) =>
        spotGrids.Remove(args.Document);

    [CommandMethod("CIVILAISPOTELEVATIONS", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void AddSpotElevations() => RunSpotElevations(replace: false);

    [CommandMethod("CIVILAIREPLACESPOTS", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void ReplaceSpotElevations() => RunSpotElevations(replace: true);

    private static void RunSpotElevations(bool replace)
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;
        var editor = document.Editor;
        var requestedMeters = ConsumeTaskDistance(document,
            replace ? DrawingTask.ReplaceSpotElevations : DrawingTask.SpotElevations);
        var database = document.Database;

        try
        {
            // Replacement may only run from the explicit approval in Chat.
            if (replace && requestedMeters is null) return;
            SpotGrid? previous = null;
            if (replace)
            {
                var selection = editor.SelectImplied();
                previous = selection.Status == PromptStatus.OK
                    ? ReadSelectedSpotGrid(selection.Value.GetObjectIds(), database)
                    : spotGrids.GetValueOrDefault(document)
                        ?? throw new InvalidOperationException("No tracked grid in this drawing. Select the existing spot elevation labels and retry.");
            }

            var surfaceId = previous?.SurfaceId ??
                PickTin(editor, database, "\nSelect the TIN for spot elevation labels: ");
            if (surfaceId.IsNull) return;

            var spacing = ResolveTaskDistance(editor, database, requestedMeters, 1d, "Grid spacing");
            if (!double.IsFinite(spacing) || spacing <= 0) return;

            var points = new List<Point2d>();
            string name;
            using (var read = database.TransactionManager.StartOpenCloseTransaction())
            {
                var surface = (TinSurface)read.GetObject(surfaceId, OpenMode.ForRead);
                if (surface.IsReferenceObject &&
                    (!surface.IsReferenceValid || surface.IsReferenceStale || !surface.IsReferencedSourceExisting))
                    throw new InvalidOperationException("The reference TIN is missing, invalid, or stale.");

                name = surface.Name;
                var bounds = surface.GetGeneralProperties();
                var firstX = Math.Ceiling(bounds.MinimumCoordinateX / spacing);
                var firstY = Math.Ceiling(bounds.MinimumCoordinateY / spacing);
                var columns = Math.Floor(bounds.MaximumCoordinateX / spacing) - firstX + 1;
                var rows = Math.Floor(bounds.MaximumCoordinateY / spacing) - firstY + 1;
                if (columns <= 0 || rows <= 0)
                    throw new InvalidOperationException("No grid intersections fall within the surface extents. Try a smaller spacing.");
                if (!double.IsFinite(columns * rows) || columns * rows > 100_000 ||
                    Math.Abs(firstX) + columns >= 9e15 || Math.Abs(firstY) + rows >= 9e15)
                    throw new InvalidOperationException("The grid is too dense for this surface. Increase the spacing.");
                for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                {
                    var x = (firstX + column) * spacing;
                    var y = (firstY + row) * spacing;
                    try
                    {
                        if (!double.IsFinite(surface.FindElevationAtXY(x, y))) continue;
                    }
                    catch (Autodesk.Civil.PointNotOnEntityException) { continue; }

                    if (points.Count == 5_000)
                        throw new InvalidOperationException("This grid would place more than 5,000 labels. Increase the spacing.");
                    points.Add(new Point2d(x, y));
                }
            }

            if (points.Count == 0)
                throw new InvalidOperationException("No grid intersections fall on this surface. Try a smaller spacing.");
            if (!replace && !agentExecuting)
            {
                editor.WriteMessage($"\nPreview: {points.Count} native spot elevation labels on '{name}', spaced {spacing:G} drawing units in WCS X and Y.");
                var confirmation = new PromptKeywordOptions("\nPlace these labels?") { AllowNone = true };
                confirmation.Keywords.Add("Place");
                confirmation.Keywords.Add("Cancel");
                confirmation.Keywords.Default = "Cancel";
                var answer = editor.GetKeywords(confirmation);
                if (answer.Status != PromptStatus.OK || answer.StringResult != "Place") return;
            }

            ObjectId[] newLabels;
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                if (previous is not null)
                {
                    foreach (var id in previous.Labels)
                    {
                        if (id.IsErased || transaction.GetObject(id, OpenMode.ForWrite) is not SurfaceElevationLabel label ||
                            label.FeatureId != surfaceId)
                            throw new InvalidOperationException("The old grid has changed. Select the labels to replace and try again.");
                        label.Erase();
                    }
                }
                newLabels = points.Select(point => SurfaceElevationLabel.Create(surfaceId, point)).ToArray();
                transaction.Commit();
            }

            spotGrids[document] = new(surfaceId, newLabels);
            var result = previous is null
                ? $"Placed {newLabels.Length} spot elevation labels on '{name}'."
                : $"Replaced {previous.Labels.Length} labels with {newLabels.Length} on '{name}' at {spacing:G} drawing units.";
            panel?.InvalidateDrawing(result);
            workspace?.ShowTaskResult(result);
            editor.WriteMessage("\n" + result);
        }
        catch (System.Exception ex)
        {
            var message = $"Spot elevations stopped: {ex.Message}";
            panel?.InvalidateDrawing(message);
            workspace?.ShowTaskResult(message);
            editor.WriteMessage("\n" + message);
        }
    }

    private static SpotGrid ReadSelectedSpotGrid(ObjectId[] ids, Database database)
    {
        var surfaceId = ObjectId.Null;
        using var read = database.TransactionManager.StartOpenCloseTransaction();
        foreach (var id in ids)
        {
            if (read.GetObject(id, OpenMode.ForRead) is not SurfaceElevationLabel label ||
                (!surfaceId.IsNull && label.FeatureId != surfaceId))
                throw new InvalidOperationException("Select only the spot elevation labels to replace, all on one TIN.");
            surfaceId = label.FeatureId;
        }
        if (surfaceId.IsNull || read.GetObject(surfaceId, OpenMode.ForRead) is not TinSurface)
            throw new InvalidOperationException("The selected spot labels must belong to a TIN surface.");
        return new(surfaceId, ids);
    }

    private static double ResolveTaskDistance(Editor editor, Database database, double? requestedMeters,
        double defaultMeters, string label)
    {
        double? unitsPerMeter = database.Insunits switch
        {
            UnitsValue.Meters => 1d,
            UnitsValue.Centimeters => 100d,
            UnitsValue.Millimeters => 1000d,
            UnitsValue.Feet => 1 / 0.3048,
            UnitsValue.Inches => 1 / 0.0254,
            _ => null
        };
        if (requestedMeters is double meters && unitsPerMeter is double scale) return meters * scale;

        var fallback = unitsPerMeter * defaultMeters;
        var prompt = new PromptDoubleOptions(fallback is double suggested
            ? $"\n{label} in drawing units <{suggested:G}>: "
            : $"\n{label} in drawing units (enter the equivalent of {requestedMeters ?? defaultMeters:G} m): ")
        {
            AllowNegative = false,
            AllowZero = false,
            AllowNone = fallback.HasValue,
            UseDefaultValue = fallback.HasValue
        };
        if (fallback is double defaultValue) prompt.DefaultValue = defaultValue;
        var result = editor.GetDouble(prompt);
        return result.Status switch
        {
            PromptStatus.OK => result.Value,
            PromptStatus.None => fallback ?? double.NaN,
            _ => double.NaN
        };
    }

    private static ObjectId PickTin(Editor editor, Database database, string prompt)
    {
        var implied = editor.SelectImplied();
        if (implied.Status == PromptStatus.OK)
        {
            using var read = database.TransactionManager.StartOpenCloseTransaction();
            var surfaces = implied.Value.GetObjectIds()
                .Where(id => read.GetObject(id, OpenMode.ForRead) is TinSurface).ToArray();
            if (surfaces.Length == 1) return surfaces[0];
        }

        var picked = editor.GetEntity(prompt);
        if (picked.Status != PromptStatus.OK) return ObjectId.Null;
        using (var read = database.TransactionManager.StartOpenCloseTransaction())
            if (read.GetObject(picked.ObjectId, OpenMode.ForRead) is TinSurface) return picked.ObjectId;
        throw new ArgumentException("Select a TIN surface.");
    }
}
