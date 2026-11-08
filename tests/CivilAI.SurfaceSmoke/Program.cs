using CivilAI;
void Check(bool condition) { if (!condition) throw new Exception("Surface statistic assertion failed"); }
var stats = new SurfaceStatistics();
stats.Read("MinimumElevation", () => -10);
stats.Read("MaximumElevation", () => 10);
stats.Read("MeanElevation", () => 0);
Check(!stats.Finish().Any(x => x.Name == "Diagnostic"));
var broken = new SurfaceStatistics();
broken.Read("NaN", () => double.NaN);
broken.Read("Infinity", () => double.PositiveInfinity);
broken.Read("Negative", () => -1, true);
broken.Read("Throw", () => throw new InvalidOperationException("secret drawing path"));
broken.Read("MinimumElevation", () => 10);
broken.Read("MaximumElevation", () => 0);
broken.Read("SurfaceArea2D", () => 0, true);
var rows = broken.Finish();
Check(rows.Count(x => x.Value.StartsWith("Unavailable")) == 4);
Check(rows.Count(x => x.Name == "Diagnostic") == 2);
Check(!rows.Any(x => x.Value.Contains("secret")));
Console.WriteLine("Surface statistics checks passed.");
var summary = SurfaceSummary.Create(new DrawingItem("TIN surface", "Plane", new[] {
    new PropertyValue("MinimumGradeOrSlope (API raw)", "0.0000413010296311"),
    new PropertyValue("MaximumGradeOrSlope (API raw)", "1.70262928212"),
    new PropertyValue("MeanGradeOrSlope (API raw)", "0.128970485874") }));
Check(summary.Single(p => p.Name == "Minimum slope").Value == "0.00413 %");
Check(summary.Single(p => p.Name == "Maximum slope").Value == "170.26293 %");
Check(summary.Single(p => p.Name == "Mean slope").Value == "12.89705 %");
Check(summary.Single(p => p.Name == "Minimum elevation").Value.StartsWith("Unavailable"));
Console.WriteLine("Surface summary conversion and missing-data checks passed.");
DrawingItem Slopes(string min, string max) => new("TIN surface", "Synthetic", new[] {
    new PropertyValue("MinimumGradeOrSlope (API raw)", min), new PropertyValue("MaximumGradeOrSlope (API raw)", max) });
Check(SlopeLimits.Evaluate(Slopes("0.01", "0.05"), "1", "5").Value.StartsWith("Within"));
Check(SlopeLimits.Evaluate(Slopes("0.001", "0.06"), "1", "5").Value.Contains("minimum below range and maximum above"));
Check(SlopeLimits.Evaluate(Slopes("0.01", "0.06"), "1", "5").Value.Contains("maximum above"));
foreach (var limits in new[] { ("NaN", "5"), ("1", "Infinity"), ("-1", "5"), ("6", "5"), ("1,5", "5"), ("", "5") })
    Check(SlopeLimits.Evaluate(Slopes("0", "1"), limits.Item1, limits.Item2).Value.StartsWith("Invalid"));
Check(SlopeLimits.Evaluate(Slopes("NaN", "1"), "0", "5").Value.StartsWith("Unavailable"));
Check(SlopeLimits.Evaluate(Slopes("2", "1"), "0", "5").Value.StartsWith("Unavailable"));
Check(SlopeLimits.Evaluate(Slopes("0", "1"), "", "").Value.StartsWith("Not configured"));
Console.WriteLine("Slope limit boundary and invalid-input checks passed.");
