using System.Globalization;

namespace CivilAI;

// Presentation only: raw detached properties remain available for inspection.
internal static class SurfaceSummary
{
    public static IReadOnlyList<PropertyValue> Create(DrawingItem item)
    {
        if (item.Kind != "TIN surface") return new[] { new PropertyValue("Summary", "Select a TIN surface to view its summary.") };
        var result = new List<PropertyValue> { new("Surface", item.Name) };
        foreach (var (key, label, factor, suffix) in new[]
        {
            ("MinimumElevation", "Minimum elevation", 1d, "drawing units"),
            ("MaximumElevation", "Maximum elevation", 1d, "drawing units"),
            ("MeanElevation", "Mean elevation", 1d, "drawing units"),
            ("SurfaceArea2D", "Plan area", 1d, "drawing units²"),
            ("SurfaceArea3D", "Surface area", 1d, "drawing units²"),
            ("MinimumGradeOrSlope (API raw)", "Minimum slope", 100d, "%"),
            ("MaximumGradeOrSlope (API raw)", "Maximum slope", 100d, "%"),
            ("MeanGradeOrSlope (API raw)", "Mean slope", 100d, "%")
        })
        {
            var raw = item.Properties.FirstOrDefault(p => p.Name == key)?.Value;
            var valid = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value * factor);
            result.Add(new(label, valid ? $"{(value * factor).ToString("0.#####", CultureInfo.InvariantCulture)} {suffix}" : "Unavailable — no usable statistic returned."));
        }
        var issues = item.Properties.Where(p => p.Name == "Diagnostic" || p.Value.StartsWith("Unavailable", StringComparison.Ordinal)).ToArray();
        foreach (var issue in issues)
            result.Add(new("Diagnostic", issue.Name + ": " + (issue.Value.Contains("InvalidOperationException") ? "No statistics available. The surface may be empty or its data inaccessible; no rebuild was attempted." : issue.Value)));
        result.Add(new("Interpretation", "Slope = rise/run × 100. Elevations/areas retain drawing units; no metre/foot conversion. These statistics do not establish grading compliance."));
        result.Add(new("Snapshot", "Read-only results. Refresh after drawing edits; no surface rebuild is performed."));
        return result;
    }
}


internal static class SlopeLimits
{
    public static PropertyValue Evaluate(DrawingItem item, string minimum, string maximum)
    {
        if (string.IsNullOrWhiteSpace(minimum) && string.IsNullOrWhiteSpace(maximum))
            return new("Slope check", "Not configured — enter both limits in percent.");
        bool Parse(string text, out double value) => double.TryParse(text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out value) && double.IsFinite(value) && value >= 0;
        if (!Parse(minimum, out var lower) || !Parse(maximum, out var upper) || lower > upper)
            return new("Slope check", "Invalid limits — use nonnegative numbers, minimum ≤ maximum, and a decimal point (for example 2.5).");
        var minText = item.Properties.FirstOrDefault(p => p.Name == "MinimumGradeOrSlope (API raw)")?.Value;
        var maxText = item.Properties.FirstOrDefault(p => p.Name == "MaximumGradeOrSlope (API raw)")?.Value;
        if (!Parse(minText ?? "", out var min) || !Parse(maxText ?? "", out var max) || min > max || !double.IsFinite(max * 100))
            return new("Slope check", "Unavailable — missing or inconsistent surface slope statistics.");
        min *= 100; max *= 100;
        var below = min < lower;
        var above = max > upper;
        var verdict = below && above ? "Outside limits: minimum below range and maximum above range."
            : below ? "Outside limits: minimum below range."
            : above ? "Outside limits: maximum above range."
            : "Within configured limits (inclusive).";
        return new("Slope check", verdict + $" Range: {lower.ToString("G", CultureInfo.InvariantCulture)}–{upper.ToString("G", CultureInfo.InvariantCulture)} %. Whole-surface extrema only; does not locate affected triangles or establish compliance.");
    }
}
