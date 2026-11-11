using System.Net;
using System.Text;
using System.Text.Json;
using CivilAI;
using CivilAI.Workspace;

var passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); passed++; }
void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } catch (InvalidOperationException) { Check(true, label); return; } throw new Exception(label); }
var root = Path.Combine(Path.GetTempPath(), "CivilAI-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new WorkspaceStore(root);
    Check(store.LoadSettings().Provider == "Offline", "Offline default");
    var markdown = "# Landscape reviewer\n\n```powershell\nRemove-Item anything\n```\nExplain available surface properties.";
    var agent = store.SaveAgent(null, markdown);
    Check(store.LoadAgents().Single().Markdown == markdown, "Markdown round trip remains inert text");
    store.SaveAgent(agent.Id, "# Revised\nReview only.");
    Check(store.LoadAgents().Single().Name == "Revised", "Agent edits persist");
    var export = Path.Combine(root, "export.md"); File.WriteAllText(export, markdown);
    Check(WorkspaceStore.ReadMarkdown(export) == markdown, "Import/export preserves Markdown");
    Reject(() => store.SaveAgent("../../escape", markdown), "Agent path traversal rejected");
    Reject(() => store.SaveAgent(null, ""), "Empty agent rejected");
    Reject(() => store.SaveAgent(null, "# Big\n" + new string('x', 1_048_576)), "Oversize agent rejected");
    store.SaveSettings(new WorkspaceSettings { Provider = "Google", Model = "configured-model", SelectedAgent = agent.Id, HistoryRatio = 2, FontSize = 15, SelectedTab = 2 });
    var settings = store.LoadSettings();
    Check(settings.Provider == "Google" && settings.FontSize == 15 && settings.SelectedAgent == agent.Id, "Non-secret settings persist");
    Check(!File.ReadAllText(Path.Combine(root, "settings.json")).Contains("ApiKey"), "No API key setting exists");
    Reject(() => store.SaveSettings(new WorkspaceSettings { LocalEndpoint = "https://user:secret@example.com/v1" }), "Credentials cannot be persisted in endpoint settings");
    Check(!File.ReadAllText(Path.Combine(root, "settings.json")).Contains("secret"), "Rejected endpoint leaves saved settings unchanged");
    store.SaveConversation(new[] { new ChatMessage("user", "hello"), new ChatMessage("assistant", "hi") });
    Check(store.LoadConversation().Count == 2, "Conversation history persists");
    var snapshot = new DrawingSnapshot("PrivateProject.dwg", new[] { new DrawingItem("TIN surface", "Ground", new[] { new PropertyValue("SurfaceArea2D", "12") }) }, 0, 1, Array.Empty<string>());
    var calls = 0;
    var registry = new ReadOnlyToolRegistry(() => { calls++; return Task.FromResult<DrawingSnapshot?>(snapshot); });
    Check((await registry.ExecuteAsync("drawing.inspect")) == snapshot && calls == 1, "Registered read-only tool returns structured snapshot");
    Reject(() => registry.ExecuteAsync("drawing.modify"), "Write/unregistered tool blocked");
    Reject(() => registry.ExecuteAsync("powershell Remove-Item anything"), "Markdown/script command cannot resolve to a tool");
    var engine = new ConversationEngine();
    engine.Messages.AddRange(store.LoadConversation()); engine.Messages.Add(new("local", "PrivateProject.dwg"));
    var withoutDrawing = engine.Compose(agent, "Review", null);
    Check(!JsonSerializer.Serialize(withoutDrawing).Contains("PrivateProject"), "Drawing/local tool results excluded by default");
    Check(withoutDrawing[0].Role == "system" && withoutDrawing[0].Content.Contains(markdown), "Selected agent supplies instruction text");
    Check(engine.Compose(agent, "Review", snapshot).Last().Content.Contains("SurfaceArea2D"), "Explicit snapshot includes structured properties");
    Reject(() => engine.Compose(agent, " ", null), "Blank prompt blocked");
    var transport = new FakeTransport();
    using var provider = new JsonAiProvider(transport);
    var expected = new Dictionary<string, string> { ["OpenAI"] = "api.openai.com", ["Anthropic"] = "api.anthropic.com", ["Google"] = "generativelanguage.googleapis.com", ["Groq"] = "api.groq.com", ["DeepSeek"] = "api.deepseek.com", ["Local"] = "localhost" };
    foreach (var entry in expected)
    {
        var sendsBeforePreparation = transport.Calls;
        var request = provider.Prepare(new(entry.Key, "test-model", "http://localhost:1234/v1"), withoutDrawing);
        Check(request.Endpoint.Host == entry.Value && request.Body.Contains("Review"), entry.Key + " request destination and payload");
        Check(transport.Calls == sendsBeforePreparation, entry.Key + " preparation does not send");
        transport.Provider = entry.Key;
        var reply = await provider.SendAsync(request, entry.Key == "Local" ? "" : "session-test-key", CancellationToken.None);
        Check(reply == "answer" && transport.HeaderCorrect, entry.Key + " adapter authorization and response parsing (fake HTTP)");
    }
    Reject(() => provider.Prepare(new("Offline", "model", ""), withoutDrawing), "Offline requests blocked");
    Reject(() => provider.Prepare(new("Local", "model", "http://remote.example/v1"), withoutDrawing), "Remote cleartext endpoint blocked");
    Reject(() => provider.Prepare(new("Local", "model", "https://key:secret@remote.example/v1"), withoutDrawing), "Endpoint credentials blocked");
    Reject(() => provider.Prepare(new("Local", "model", "https://remote.example/v1?key=secret"), withoutDrawing), "Endpoint query credentials blocked");
    var groq = provider.Prepare(new("Groq", "", "https://example.invalid/v1"), withoutDrawing);
    using (var groqBody = System.Text.Json.JsonDocument.Parse(groq.Body))
    {
        Check(groq.Endpoint.AbsoluteUri == "https://api.groq.com/openai/v1/chat/completions" &&
            groqBody.RootElement.GetProperty("model").GetString() == "openai/gpt-oss-20b" &&
            groqBody.RootElement.GetProperty("response_format").GetProperty("type").GetString() == "json_object",
            "Groq uses its fixed endpoint, default model and JSON mode");
    }
    var deepseek = provider.Prepare(new("DeepSeek", "", ""), withoutDrawing);
    using (var body = System.Text.Json.JsonDocument.Parse(deepseek.Body))
        Check(deepseek.Endpoint.AbsoluteUri == "https://api.deepseek.com/chat/completions" &&
            body.RootElement.GetProperty("model").GetString() == JsonAiProvider.DeepSeekDefaultModel &&
            body.RootElement.GetProperty("response_format").GetProperty("type").GetString() == "json_object" &&
            body.RootElement.GetProperty("thinking").GetProperty("type").GetString() == "disabled",
            "DeepSeek default model and agent JSON configuration");
    var prepared = provider.Prepare(new("OpenAI", "test", ""), withoutDrawing);
    var before = transport.Calls;
    try { await provider.SendAsync(prepared, "", CancellationToken.None); throw new Exception("Expected missing key error"); }
    catch (InvalidOperationException) { Check(transport.Calls == before, "Missing key rejected before network"); }
    transport.Status = HttpStatusCode.Unauthorized;
    try { await provider.SendAsync(prepared, "session-test-key", CancellationToken.None); throw new Exception("Expected HTTP failure"); }
    catch (HttpRequestException ex) { Check(!ex.Message.Contains("server-echoed-secret"), "HTTP errors do not leak response bodies"); }
    transport.Status = HttpStatusCode.OK;
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { await provider.SendAsync(prepared, "session-test-key", cancellation.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(true, "Canceled send is reported"); }
    Console.WriteLine($"{passed} checks passed. No external services contacted.");
}
finally { Directory.Delete(root, true); }

internal sealed class FakeTransport : HttpMessageHandler
{
    public int Calls;
    public string Provider = "OpenAI";
    public bool HeaderCorrect;
    public HttpStatusCode Status = HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); Calls++;
        HeaderCorrect = Provider switch
        {
            "Anthropic" => message.Headers.Contains("x-api-key") && message.Headers.Contains("anthropic-version"),
            "Google" => message.Headers.Contains("x-goog-api-key"),
            "Local" => message.Headers.Authorization is null,
            _ => message.Headers.Authorization?.Scheme == "Bearer"
        };
        using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(cancellation));
        if (Provider == "Anthropic" && !body.RootElement.TryGetProperty("system", out _)) throw new Exception("Missing Anthropic system instructions");
        if (Provider == "Google" && !body.RootElement.TryGetProperty("systemInstruction", out _)) throw new Exception("Missing Google system instructions");
        var response = Provider switch
        {
            "Anthropic" => "{\"content\":[{\"type\":\"text\",\"text\":\"answer\"}]}",
            "Google" => "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"answer\"}]}}]}",
            _ => "{\"choices\":[{\"message\":{\"content\":\"answer\"}}]}"
        };
        return new HttpResponseMessage(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? response : "server-echoed-secret", Encoding.UTF8, "application/json") };
    }
}
