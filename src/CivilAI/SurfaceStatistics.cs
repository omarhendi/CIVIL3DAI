using System.Globalization;

namespace CivilAI;

// Detached scalar validation: no Autodesk objects, I/O, or networking.
internal sealed class SurfaceStatistics
{
    private readonly List<PropertyValue> rows = new();
    private readonly Dictionary<string, double> values = new();
    public void Read(string name, Func<double> getter, bool nonnegative = false)
    {
        try
        {
            var value = getter();
            if (!double.IsFinite(value) || (nonnegative && value < 0))
            {
                rows.Add(new(name, "Unavailable: invalid numeric statistic"));
                return;
            }
            values[name] = value;
            rows.Add(new(name, value.ToString("G12", CultureInfo.InvariantCulture)));
        }
        catch (Exception ex)
        {
            // Exception messages can contain drawing paths or native implementation details.
            rows.Add(new(name, $"Unavailable ({ex.GetBaseException().GetType().Name})"));
        }
    }
    public void Unavailable(string group, Exception ex) => rows.Add(new(group,
        $"Unavailable ({ex.GetBaseException().GetType().Name}); empty or inaccessible surface data. No rebuild attempted."));
    public IReadOnlyList<PropertyValue> Finish()
    {
        CheckRange("MinimumElevation", "MeanElevation", "MaximumElevation");
        CheckRange("MinimumGradeOrSlope (API raw)", "MeanGradeOrSlope (API raw)", "MaximumGradeOrSlope (API raw)");
        if (values.TryGetValue("SurfaceArea2D", out var area))
        {
            if (area == 0) rows.Add(new("Diagnostic", "Zero plan area: no usable terrain coverage."));
            if (values.TryGetValue("SurfaceArea3D", out var spatial) && spatial + Math.Max(1, area) * 1e-10 < area)
                rows.Add(new("Diagnostic", "3D area is smaller than plan area; verify surface statistics."));
        }
        rows.Add(new("Statistics scope", "Civil 3D current surface statistics; no rebuild, triangle sampling, or engineering compliance assessment. Elevations and areas use drawing units. Grade/slope values are raw API values; no percent conversion assumed."));
        return rows;
    }
    private void CheckRange(string min, string mean, string max)
    {
        if (values.TryGetValue(min, out var a) && values.TryGetValue(max, out var b) &&
            (a > b || (values.TryGetValue(mean, out var m) && (m < a || m > b))))
            rows.Add(new("Diagnostic", $"Inconsistent {min}/{mean}/{max}; verify surface data."));
    }
}
