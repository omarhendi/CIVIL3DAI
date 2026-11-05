using System.Globalization;
using System.Reflection;

namespace CivilAI;

internal static class PropertyInspection
{
    // Autodesk 2026 Entity properties whose meaning depends on reference status.
    // IsReferenceObject / IsReadOnlyReferenceObject / IsCWSReferenceObject /
    // IsCWSSourceObject are classification flags, safe to inspect on native entities.
    private static readonly HashSet<string> ReferenceOnly = new(StringComparer.Ordinal)
    {
        "IsReferencedSourceExisting", "IsReferenceValid", "IsReferenceStale",
        "IsReferenceSubObject", "IsPartialReferenceObject"
    };

    public static IReadOnlyList<PropertyValue> ReadScalars(object value, bool? isReference, Action<string> report)
    {
        var result = new List<PropertyValue>();
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).OrderBy(p => p.Name))
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!(type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                  || type.FullName is "Autodesk.AutoCAD.DatabaseServices.ObjectId" or "Autodesk.AutoCAD.DatabaseServices.Handle")) continue;
            if (ReferenceOnly.Contains(property.Name) && isReference != true)
            {
                result.Add(new PropertyValue(property.Name, isReference == false
                    ? "Not applicable (native entity)" : "Not read (reference status unavailable)"));
                continue; // In particular, NEVER invoke IsReferencedSourceExisting for a native entity.
            }
            result.Add(ReadValue(property.Name, () => property.GetValue(value), report));
        }
        return result;
    }

    public static PropertyValue ReadValue(string name, Func<object?> getter, Action<string> report)
    {
        try { return new PropertyValue(name, Convert.ToString(getter(), CultureInfo.InvariantCulture) ?? "(null)"); }
        catch (Exception ex) { return Failure(name, ex, report); }
    }

    public static PropertyValue Failure(string name, Exception exception, Action<string> report)
    {
        var cause = exception.GetBaseException();
        var message = $"{cause.GetType().Name}: {cause.Message}";
        report($"{name}: {message}");
        return new PropertyValue(name, $"Unavailable: {message}");
    }
}
