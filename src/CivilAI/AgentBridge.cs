using System.Globalization;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using CivilAI.Workspace;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CivilAI;

public sealed partial class Plugin
{
    private static bool agentExecuting;
    private static readonly Dictionary<string, MethodInfo> NativeAgentCommands = typeof(Plugin)
        .GetMethods().SelectMany(method => method.GetCustomAttributes<CommandMethodAttribute>()
            .Select(attribute => (attribute.GlobalName, Method: method)))
        .Where(entry => entry.GlobalName.StartsWith("CIVILAI") && entry.GlobalName != "CIVILAI")
        .ToDictionary(entry => entry.GlobalName, entry => entry.Method, StringComparer.OrdinalIgnoreCase);

    // This catalog covers native operations rather than natural-language intent phrases.
    private static readonly HashSet<string> AgentCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "LINE", "PLINE", "3DPOLY", "CIRCLE", "ARC", "RECTANG", "MOVE", "COPY", "ROTATE",
        "SCALE", "MIRROR", "OFFSET", "ERASE", "TRIM", "EXTEND", "FILLET", "CHAMFER",
        "JOIN", "PEDIT", "EXPLODE", "REVERSE", "MATCHPROP", "CHPROP", "REGEN"
    };

    private static async Task<string> ExecuteAgentTool(string tool, JsonElement args, CancellationToken cancellation)
    {
        var document = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("Open a drawing first.");
        if (!document.Editor.IsQuiescent)
            throw new InvalidOperationException("Finish the active Civil 3D command first.");
        string result = "";
        await Application.DocumentManager.ExecuteInCommandContextAsync(async _ =>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (Application.DocumentManager.MdiActiveDocument != document)
                    throw new InvalidOperationException("The active drawing changed.");
                var db = document.Database;
                var editor = document.Editor;
                if (tool == "select")
                {
                    var handles = args.GetProperty("handles").EnumerateArray().ToArray();
                    if (handles.Length > 200) throw new InvalidOperationException("Selection is limited to 200 objects.");
                    var selected = handles.Select(value => db.GetObjectId(false,
                        new Handle(long.Parse(value.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), 0)).ToArray();
                    if (selected.Any(id => id.IsErased)) throw new InvalidOperationException("An object was erased.");
                    editor.SetImpliedSelection(selected);
                    result = JsonSerializer.Serialize(new { status = "selected", count = selected.Length });
                    return;
                }
                if (tool == "context")
                {
                    var selection = editor.SelectImplied();
                    var ids = selection.Status == PromptStatus.OK ? selection.Value.GetObjectIds() : Array.Empty<ObjectId>();
                    using var read = db.TransactionManager.StartOpenCloseTransaction();
                    var objects = ids.Take(200).Select(id =>
                    {
                        var entity = read.GetObject(id, OpenMode.ForRead);
                                                return new { handle = id.Handle.ToString(), type = entity.GetType().FullName,
                            layer = (entity as Entity)?.Layer,
                            name = (entity as Autodesk.Civil.DatabaseServices.Entity)?.Name };
                    }).ToArray();
                    result = JsonSerializer.Serialize(new { units = db.Insunits.ToString(), selected = objects,
                        selectedCount = ids.Length, commands = AgentCommands.Order().ToArray(), civilCommands = NativeAgentCommands.Keys.Order().ToArray() });
                    return;
                }
                var name = args.GetProperty("name").GetString() ?? "";
                if (NativeAgentCommands.TryGetValue(name, out var native))
                {
                    var notices = new List<string>();
                    var created = new List<object>();
                    void Created(object sender, ObjectEventArgs e)
                    {
                        if (created.Count < 200)
                            created.Add(new { handle = e.DBObject.Handle.ToString(), type = e.DBObject.GetType().FullName });
                    }
                    db.ObjectAppended += Created;
                    void Notice(string text) => notices.Add(text);
                    if (panel is not null) panel.Notice += Notice;
                    agentExecuting = true;
                    try
                    {
                        if (args.TryGetProperty("distance_meters", out var distance))
                        {
                            var meters = distance.GetDouble();
                            if (!double.IsFinite(meters) || meters <= 0) throw new InvalidOperationException("Invalid distance.");
                            var task = name.ToUpperInvariant() switch
                            {
                                "CIVILAISPOTELEVATIONS" => DrawingTask.SpotElevations,
                                "CIVILAIREPLACESPOTS" => DrawingTask.ReplaceSpotElevations,
                                "CIVILAIOFFSETLEVELS" => DrawingTask.OffsetLevels,
                                _ => throw new InvalidOperationException("This command does not take a distance.")
                            };
                            pendingDistance = (document, task, meters, Environment.TickCount64);
                        }
                        native.Invoke(new Plugin(), null);
                        result = JsonSerializer.Serialize(new { status = "workflow returned", messages = notices, created,
                            instruction = "Only explicit result messages establish completion. An empty result may mean cancellation." });
                    }
                    finally
                    {
                        db.ObjectAppended -= Created;
                        pendingDistance = null;
                        agentExecuting = false;
                        if (panel is not null) panel.Notice -= Notice;
                    }
                    return;
                }
                if (!AgentCommands.Contains(name)) throw new InvalidOperationException("Command is outside the current execution catalog.");
                var tokens = args.GetProperty("tokens");
                if (tokens.ValueKind != JsonValueKind.Array || tokens.GetArrayLength() > 512)
                    throw new InvalidOperationException("Invalid command arguments.");
                var command = new List<object> { "_." + name };
                foreach (var token in tokens.EnumerateArray())
                {
                    switch (token.ValueKind)
                    {
                        case JsonValueKind.String:
                            var text = token.GetString()!;
                            if (text.Length > 256 || text.Any(char.IsControl) || text.IndexOfAny(new[] { '(', ')', '\\', '/', ';' }) >= 0)
                                throw new InvalidOperationException("Invalid command text.");
                            command.Add(text);
                            break;
                        case JsonValueKind.Number:
                            var number = token.GetDouble();
                            if (!double.IsFinite(number)) throw new InvalidOperationException("Invalid number.");
                            command.Add(number);
                            break;
                        case JsonValueKind.Array:
                            var xyz = token.EnumerateArray().Select(x => x.GetDouble()).ToArray();
                            if (xyz.Length != 3 || xyz.Any(x => !double.IsFinite(x))) throw new InvalidOperationException("Invalid point.");
                            command.Add(new Point3d(xyz[0], xyz[1], xyz[2]).TransformBy(editor.CurrentUserCoordinateSystem.Inverse()));
                            break;
                        case JsonValueKind.Object:
                            var handle = long.Parse(token.GetProperty("entity").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            var id = db.GetObjectId(false, new Handle(handle), 0);
                            if (id.IsErased) throw new InvalidOperationException("The requested object was erased.");
                            command.Add(id);
                            break;
                        default: throw new InvalidOperationException("Invalid argument type.");
                    }
                }
                var added = new HashSet<string>();
                var changed = new HashSet<string>();
                void Added(object sender, ObjectEventArgs e) => added.Add(e.DBObject.Handle.ToString());
                void Changed(object sender, ObjectEventArgs e) => changed.Add(e.DBObject.Handle.ToString());
                db.ObjectAppended += Added;
                db.ObjectModified += Changed;
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    await editor.CommandAsync(command.ToArray());
                    result = JsonSerializer.Serialize(new { status = "command ended", command = name,
                        added = added.Take(200).ToArray(), modified = changed.Take(200).ToArray(),
                        addedCount = added.Count, modifiedCount = changed.Count });
                }
                finally { db.ObjectAppended -= Added; db.ObjectModified -= Changed; }
            }
            catch (System.Exception ex)
            {
                result = JsonSerializer.Serialize(new { status = "stopped", error = ex.Message,
                    instruction = "Partial changes may exist. Inspect before retrying." });
            }
        }, null);
        return result;
    }
}
