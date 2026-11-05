using System.Globalization;
using System.Reflection;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace CivilAI;

internal static class Inspection
{
    public static DrawingSnapshot Read(Document document, Action<string> progress)
    {
        var items = new List<DrawingItem>();
        var seen = new HashSet<ObjectId>();
        var unavailable = 0;
        var diagnostics = new List<string>();
        void Report(string message)
        {
            if (diagnostics.Count < 100) diagnostics.Add(message);
            else if (diagnostics.Count == 100) diagnostics.Add("Further diagnostics omitted (100-message limit).");
        }
        progress("Starting open/close read transaction.");
        using var transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
        void Inspect(ObjectId id)
        {
            if (!seen.Add(id)) return;
            try
            {
                if (id.IsErased) return;
                var value = transaction.GetObject(id, OpenMode.ForRead);
                if (value is not TinSurface && value is not FeatureLine) return;
                var properties = ReadProperties(value, Report);
                var name = properties.FirstOrDefault(p => p.Name == "Name")?.Value ?? value.Handle.ToString();
                items.Add(new DrawingItem(value is TinSurface ? "TIN surface" : "Feature line", name, properties));
            }
            catch (Exception ex)
            {
                unavailable++;
                Report($"Object {id}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        // Civil's surface collection includes surfaces independently of block ownership.
        try
        {
            // Resolve Civil context from this exact database, rather than a second global active-document lookup.
            progress("Resolving Civil document from the active database.");
            var civil = CivilDocument.GetCivilDocument(document.Database);
            progress("Reading Civil surface IDs.");
            var surfaceIds = civil.GetSurfaceIds();
            Report($"Civil surface collection: {surfaceIds.Count} surfaces (all types).");
            foreach (ObjectId id in surfaceIds) Inspect(id);
        }
        catch (Exception ex)
        {
            Report($"Civil surface collection failed: {ex.GetType().Name}: {ex.Message}. Continuing local entity scan.");
        }
        progress("Scanning local block records for TIN surfaces and feature lines (ForRead). API errors will be listed below.");
        var blocks = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId blockId in blocks)
        {
            try
            {
                var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
                if (block.IsFromExternalReference || block.IsFromOverlayReference) continue;
                foreach (ObjectId id in block) Inspect(id);
            }
            catch (Exception ex) { Report($"Block {blockId}: {ex.GetType().Name}: {ex.Message}"); }
        }
        progress($"Scan finished: {items.Count} matching objects; {unavailable} failed reads. Creating detached property snapshot.");
        // Return detached strings only. No DBObject escapes the read transaction.
        return new DrawingSnapshot(document.Name, items.OrderBy(i => i.Kind).ThenBy(i => i.Name).ToArray(), unavailable, seen.Count, diagnostics.ToArray());
    }

    private static IReadOnlyList<PropertyValue> ReadProperties(DBObject value, Action<string> report)
    {
        var result = new List<PropertyValue>();
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).OrderBy(p => p.Name))
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            // Scalar properties only: do not traverse geometry, collections or linked objects.
            if (!(type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                  || type == typeof(ObjectId) || type == typeof(Handle))) continue;
            try
            {
                var raw = property.GetValue(value);
                result.Add(new PropertyValue(property.Name, Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "(null)"));
            }
            catch (Exception ex)
            {
                var cause = ex.GetBaseException();
                var message = $"{cause.GetType().Name}: {cause.Message}";
                result.Add(new PropertyValue(property.Name, $"Unavailable: {message}"));
                report($"{value.Handle}.{property.Name}: {message}");
            }
        }
        return result;
    }
}
