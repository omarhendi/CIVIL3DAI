using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using System.Globalization;

namespace CivilAI;

public sealed partial class Plugin
{
    [CommandMethod("CIVILAIFINDSLOPES", CommandFlags.Modal)]
    public void FindSlopes()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) return;
        var editor = document.Editor;
        try
        {
            var options = new PromptEntityOptions("\nSelect a TIN surface to locate out-of-range triangles: ");
            options.SetRejectMessage("\nSelect a TIN surface.");
            options.AddAllowedClass(typeof(TinSurface), true);
            var selected = editor.GetEntity(options);
            if (selected.Status != PromptStatus.OK) return;
            var lower = editor.GetDouble(new PromptDoubleOptions("\nMinimum slope percent: ") { AllowNegative = false, AllowZero = true });
            if (lower.Status != PromptStatus.OK) return;
            var upper = editor.GetDouble(new PromptDoubleOptions("\nMaximum slope percent: ") { AllowNegative = false, AllowZero = true });
            if (upper.Status != PromptStatus.OK) return;
            if (!double.IsFinite(lower.Value) || !double.IsFinite(upper.Value) || lower.Value > upper.Value)
            { editor.WriteMessage("\nInvalid limits: minimum must not exceed maximum.\n"); return; }
            var rows = new List<PropertyValue>();
            var findings = new List<(int Index, double Slope, TrianglePoint Centre)>();
            var visited = 0; var invalid = 0; var outside = 0; var complete = true;
            string name;
            using (document.LockDocument())
            using (var transaction = document.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var surface = (TinSurface)transaction.GetObject(selected.ObjectId, OpenMode.ForRead);
                name = surface.Name;
                if (surface.IsReferenceObject && (!surface.IsReferenceValid || surface.IsReferenceStale || !surface.IsReferencedSourceExisting))
                { editor.WriteMessage("\nUnavailable: invalid, stale, or missing reference. No synchronization attempted.\n"); return; }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    foreach (TinSurfaceTriangle triangle in surface.GetTriangles(false))
                    {
                        using (triangle)
                        {
                            if (visited >= 100000 || clock.Elapsed.TotalSeconds >= 5) { complete = false; break; }
                            visited++;
                            try
                            {
                                using var v1 = triangle.Vertex1; using var v2 = triangle.Vertex2; using var v3 = triangle.Vertex3;
                                var a = v1.Location; var b = v2.Location; var c = v3.Location;
                                if (!TriangleSlope.TryCalculate(new(a.X,a.Y,a.Z), new(b.X,b.Y,b.Z), new(c.X,c.Y,c.Z), out var slope, out var centre)) { invalid++; continue; }
                                if (slope < lower.Value || slope > upper.Value)
                                {
                                    outside++;
                                    if (findings.Count < 100) findings.Add((visited, slope, centre));
                                }
                            }
                            catch (System.Exception) { invalid++; }
                        }
                    }
                }
                catch (System.Exception ex)
                { complete = false; rows.Add(new("Triangle scan diagnostic", $"Enumeration stopped ({ex.GetType().Name}).")); }
            }
            rows.Insert(0, new("Triangle scan", $"{(complete ? "Enumeration complete" : "PARTIAL scan")}; read {visited}; unusable {invalid}; outside limits {outside}. Showing first {findings.Count} findings in enumeration order, not ranked."));
            rows.Add(new("Triangle scan limits", FormattableString.Invariant($"{lower.Value}–{upper.Value} %. Visible triangles only. Coordinates are WCS drawing units. Indices apply only to this scan.")));
            foreach (var finding in findings)
                rows.Add(new($"Triangle #{finding.Index}", FormattableString.Invariant($"Slope {finding.Slope:0.#####} %; centre X={finding.Centre.X:0.######}, Y={finding.Centre.Y:0.######}, Z={finding.Centre.Z:0.######}")));
            rows.Add(new("Triangle scan scope", "Locations only: no markers, zoom, rebuild, or geometry edits. No pavement/landscape classification or compliance verdict. Missing/partial data cannot establish a pass. Scan stops after 100,000 triangles or 5 seconds between API calls; a native API call itself cannot be interrupted."));
            EnsurePanel();
            palette!.Visible = true;
            lastSnapshot = null;
            workspace?.SetSnapshot(null);
            panel!.Show(new DrawingSnapshot(document.Name, new[] { new DrawingItem("Triangle locations", name, rows) }, invalid, visited, Array.Empty<string>()));
            editor.WriteMessage("\nCivil AI triangle locations are in Drawing Inspector → All Properties.\n");
        }
        catch (System.Exception ex)
        { editor.WriteMessage($"\nCivil AI triangle scan unavailable ({ex.GetType().Name}). No rebuild or drawing edits attempted.\n"); }
    }
}
