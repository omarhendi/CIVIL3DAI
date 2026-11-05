using System.Globalization;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace CivilAI;

internal static class Inspection
{
    public static DrawingSnapshot Read(Document document)
    {
        var items = new List<DrawingItem>();
        var seen = new HashSet<ObjectId>();
        var unavailable = 0;
        using var transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
        void Inspect(ObjectId id)
        {
            if (!seen.Add(id) || id.IsErased) return;
            try
            {
                var value = transaction.GetObject(id, OpenMode.ForRead);
                if (value is not TinSurface && value is not FeatureLine) return;
                var properties = ReadProperties(value);
                var name = properties.FirstOrDefault(p => p.Name == "Name")?.Value ?? value.Handle.ToString();
                items.Add(new DrawingItem(value is TinSurface ? "TIN surface" : "Feature line", name, properties));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { unavailable++; }
        }
        // Civil's surface collection includes surfaces independently of block ownership.
        foreach (ObjectId id in CivilApplication.ActiveDocument.GetSurfaceIds()) Inspect(id);
        var blocks = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId blockId in blocks)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            if (block.IsFromExternalReference || block.IsFromOverlayReference) continue;
            foreach (ObjectId id in block) Inspect(id);
        }
        // Return detached strings only. No DBObject escapes the read transaction.
        return new DrawingSnapshot(document.Name, items.OrderBy(i => i.Kind).ThenBy(i => i.Name).ToArray(), unavailable);
    }

    private static IReadOnlyList<PropertyValue> ReadProperties(DBObject value)
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
            catch (Exception ex) when (ex is TargetInvocationException or Autodesk.AutoCAD.Runtime.Exception)
            {
                result.Add(new PropertyValue(property.Name, "Unavailable in this drawing"));
            }
        }
        return result;
    }
}
