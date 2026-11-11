using System.IO;
using System.Text.Json;

namespace CivilAI.Workspace;

public sealed record AgentProfile(string Id, string Markdown)
{
    public string Name => Markdown.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("# ") && l[2..].Trim().Length > 0)?[2..].Trim() ?? "Untitled agent";
    public override string ToString() => Name;
}
public sealed record ChatMessage(string Role, string Content);
public sealed class WorkspaceSettings
{
    public string Provider { get; set; } = "Offline";
    public string Model { get; set; } = "";
    public string LocalEndpoint { get; set; } = "http://localhost:11434/v1/";
    public string SelectedAgent { get; set; } = "";
    public int SelectedTab { get; set; }
    public double HistoryRatio { get; set; } = 3;
    public double FontSize { get; set; } = 13;
}

public sealed class WorkspaceStore
{
    public string Root { get; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public WorkspaceStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CivilAI", "0.3");
        Directory.CreateDirectory(Path.Combine(Root, "Agents"));
    }
    public WorkspaceSettings LoadSettings() => File.Exists(Path.Combine(Root, "settings.json"))
        ? JsonSerializer.Deserialize<WorkspaceSettings>(File.ReadAllText(Path.Combine(Root, "settings.json"))) ?? new() : new();
    public void SaveSettings(WorkspaceSettings settings)
    {
        // Endpoint credentials/query parameters must not leak into the non-secret settings file.
        JsonAiProvider.ValidateLocalEndpoint(settings.LocalEndpoint);
        AtomicWrite(Path.Combine(Root, "settings.json"), JsonSerializer.Serialize(settings, Json));
    }
    public IReadOnlyList<AgentProfile> LoadAgents() => Directory.EnumerateFiles(Path.Combine(Root, "Agents"), "*.md")
        .Select(p => new AgentProfile(Path.GetFileNameWithoutExtension(p), ReadMarkdown(p))).OrderBy(a => a.Name).ToArray();
    public AgentProfile SaveAgent(string? id, string markdown)
    {
        ValidateMarkdown(markdown);
        id ??= Guid.NewGuid().ToString("N");
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid agent identifier.");
        AtomicWrite(Path.Combine(Root, "Agents", id + ".md"), markdown);
        return new AgentProfile(id, markdown);
    }
    public static string ReadMarkdown(string path)
    {
        if (new FileInfo(path).Length > 1_048_576) throw new InvalidOperationException("Agent Markdown is limited to 1 MiB.");
        var text = File.ReadAllText(path);
        ValidateMarkdown(text);
        return text;
    }
    private static void ValidateMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || System.Text.Encoding.UTF8.GetByteCount(text) > 1_048_576)
            throw new ArgumentException("Provide nonempty Markdown, at most 1 MiB, starting with a # Agent name heading.");
        if (!text.Split('\n').Any(l => l.TrimStart().StartsWith("# ") && l.Trim()[2..].Trim().Length > 0)) throw new ArgumentException("Add a # Agent name heading.");
    }
    public void SaveConversation(IEnumerable<ChatMessage> messages) => AtomicWrite(Path.Combine(Root, "conversation.json"), JsonSerializer.Serialize(messages, Json));
    public List<ChatMessage> LoadConversation() => File.Exists(Path.Combine(Root, "conversation.json"))
        ? JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(Path.Combine(Root, "conversation.json"))) ?? new() : new();
    private static void AtomicWrite(string path, string text)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class ReadOnlyToolRegistry
{
    private readonly Func<Task<DrawingSnapshot?>> inspect;
    public ReadOnlyToolRegistry(Func<Task<DrawingSnapshot?>> inspect) => this.inspect = inspect;
    public IReadOnlyList<string> Names { get; } = new[] { "drawing.inspect" };
    public Task<DrawingSnapshot?> ExecuteAsync(string name) => name == "drawing.inspect" ? inspect()
        : throw new InvalidOperationException("Unknown tool. Only the registered read-only drawing.inspect tool is permitted. Drawing modifications are unavailable and would require explicit user approval.");
}

