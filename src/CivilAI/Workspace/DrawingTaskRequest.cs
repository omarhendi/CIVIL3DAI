using System.Text.RegularExpressions;

namespace CivilAI.Workspace;

internal enum DrawingTask
{
    Inspect,
    FindSlopes,
    AssessArea,
    CreateTin,
    OffsetLevels,
    AddTinBoundary,
    SpotElevations
}

internal static class DrawingTaskRequest
{
    public static DrawingTask? Parse(string request)
    {
        if (request.Length > 200) return null;
        var phrase = Regex.Replace(request.Trim(), @"[ \t]+", " ")
            .TrimEnd('.', '?', '!').ToLowerInvariant();
        if (phrase.StartsWith("please ", StringComparison.Ordinal)) phrase = phrase[7..];
        if (phrase.StartsWith("can you ", StringComparison.Ordinal)) phrase = phrase[8..];

        return phrase switch
        {
            "inspect drawing" or "inspect the drawing" or "show surfaces" or "list surfaces" or
                "list feature lines" or "show drawing objects" => DrawingTask.Inspect,
            "find steep triangles" or "find steep slopes" or "locate steep triangles" or
                "check surface slope limits" => DrawingTask.FindSlopes,
            "assess pavement slopes" or "assess floor slopes" or "assess area slopes" or
                "check pavement slopes" => DrawingTask.AssessArea,
            "create a proposed tin from selected feature lines" or "create a proposed surface from these feature lines" or
                "create a surface from feature lines" => DrawingTask.CreateTin,
            "create an offset feature line from reference elevations" or "offset a feature line with reference elevations" or
                "create a feature line from reference elevations" => DrawingTask.OffsetLevels,
            "add an outer boundary" or "trim the selected surface" => DrawingTask.AddTinBoundary,
            "add spot elevations" or "label spot elevations" => DrawingTask.SpotElevations,
            _ => null
        };
    }
}
