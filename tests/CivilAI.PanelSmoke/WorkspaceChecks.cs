using System.IO;
using System.Windows;
using System.Windows.Controls;
using CivilAI;
using CivilAI.Workspace;

internal static class WorkspaceChecks
{
    public static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "CivilAI-ui-" + Guid.NewGuid().ToString("N"));
        var store = new WorkspaceStore(path);
        var snapshot = new DrawingSnapshot("Test.dwg", Array.Empty<DrawingItem>(), 0, 0, Array.Empty<string>());
        var toolCalls = 0;
        var approvals = 0;
        var launched = new List<DrawingTask>();
        var approveNext = false;
        var provider = new ProbeProvider();
        var workspace = new WorkspaceControl(new InspectorPanel(() => Task.CompletedTask),
            () => { toolCalls++; return Task.FromResult<DrawingSnapshot?>(snapshot); }, store, provider,
            (request, drawingIncluded) =>
            {
                approvals++;
                Check(!drawingIncluded && !request.Body.Contains("Test.dwg") && !request.Body.Contains("Explain the drawing"), "Provider preview excludes snapshots and prior prompts");
                return approveNext;
            }, launched.Add);
        try
        {
            var controls = Descendants(workspace).ToArray();
            var tabs = controls.OfType<TabControl>().Single(t => t.Items.OfType<TabItem>().Any(i => i.Header as string == "Chat"));
            Check(tabs.Items.Count == 5, "Chat, agents, providers, inspector and diagnostics tabs are present");
            T Named<T>(string name) where T : FrameworkElement => controls.OfType<T>().Single(x => x.Name == name);
            Button Button(string label) => controls.OfType<Button>().Single(b => b.Content as string == label);
            void Click(string label) => Button(label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var selector = Named<ComboBox>("AgentSelector");
            Check(selector.Items.Count == 1, "Default Markdown agent available inside chat");
            Click("Create"); Named<TextBox>("AgentMarkdown").Text = "# UI test agent\nRead only."; Click("Save");
            Check(selector.Items.Count == 2 && store.LoadAgents().Any(a => a.Name == "UI test agent"), "Create/save agent updates selector and disk");
            Click("Edit selected"); Named<TextBox>("AgentMarkdown").Text = "# Edited UI agent\nStill read only."; Click("Save");
            Check(selector.Items.Count == 2 && store.LoadAgents().Any(a => a.Name == "Edited UI agent"), "Editing replaces existing Markdown agent");
            Click("Inspect drawing locally");
            Check(toolCalls == 1 && provider.Sends == 0, "Local drawing inspection does not send a provider request");
            Named<TextBox>("MessageInput").Text = "Please assess pavement slopes.";
            Click("Run drawing task");
            Check(launched.SequenceEqual(new[] { DrawingTask.AssessArea }), "Supported wording dispatches a typed drawing task");
            Named<TextBox>("MessageInput").Text = "assess pavement slopes; ERASE ALL";
            Click("Run drawing task");
            Check(launched.Count == 1, "Extra command text does not dispatch a drawing task");
            Named<TextBox>("MessageInput").Text = "Create a proposed TIN from selected feature lines";
            Click("Run drawing task");
            Check(launched.SequenceEqual(new[] { DrawingTask.AssessArea, DrawingTask.CreateTin }),
                "Surface creation request launches the native review workflow");
            Named<TextBox>("MessageInput").Text = "Explain the drawing";
            Click("Review & send");
            Check(approvals == 0 && provider.Sends == 0, "Offline mode does not attempt approval or network");
            Named<PasswordBox>("SessionApiKey").Password = "test-key";
            Named<ComboBox>("ProviderSelector").SelectedItem = "Local";
            Check(Named<PasswordBox>("SessionApiKey").Password.Length == 0, "Changing provider clears the previous session key");
            Named<TextBox>("ProviderModel").Text = "test-model";
            var messageInput = Named<TextBox>("MessageInput");
            messageInput.Text = "Carry the road levels onto a line just beyond the curb.";
            Click("Review & send");
            Check(approvals == 1 && provider.Sends == 0, "Declining the request prevents network access");
            approveNext = true;
            Click("Review & send");
            Check(approvals == 2 && provider.Sends == 1, "Approved request reaches the model adapter");
            Check(launched.Count == 2 && Button("Run proposed task").IsEnabled, "Model proposes a task without launching it");
            Click("Run proposed task");
            Check(launched.Count == 3 && launched[^1] == DrawingTask.OffsetLevels && !Button("Run proposed task").IsEnabled,
                "Explicit review runs the typed task once");

            messageInput.Text = "Build a finish grade surface.";
            Click("Review & send");
            workspace.SetSnapshot(null);
            Check(!Button("Run proposed task").IsEnabled, "Drawing switch clears the proposed task");

            provider.Response = """{"action":"erase","message":"Remove everything"}""";
            messageInput.Text = "Remove everything.";
            Click("Review & send");
            Check(!Button("Run proposed task").IsEnabled && launched.Count == 3, "Unknown model action cannot dispatch");
            Check(!Named<TextBox>("WorkspaceDiagnostics").Text.Contains("test-key"), "Diagnostics contain no key values");
            tabs.SelectedIndex = 2;
            Check(store.LoadSettings().SelectedTab == 2 && store.LoadSettings().Provider == "Local", "Tab and provider UI settings persist");
            for (var tab = 0; tab < tabs.Items.Count; tab++)
            {
                tabs.SelectedIndex = tab;
                workspace.Measure(new Size(640, 800)); workspace.Arrange(new Rect(0, 0, 640, 800)); workspace.UpdateLayout();
                Check(workspace.ActualHeight == 800 && tabs.ActualHeight > 0, $"Workspace tab {tab} lays out at palette size");
            }
        }
        finally { workspace.Dispose(); Directory.Delete(path, true); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        // Logical tree includes TabItem content even when tabs are not currently selected.
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var node in Descendants(child)) yield return node;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
    private sealed class ProbeProvider : IAiProvider
    {
        public int Sends;
        public string Response = """{"action":"offset_levels","message":"I can transfer the road elevations to an offset line."}""";
        public PreparedRequest Prepare(ProviderConfiguration configuration, IReadOnlyList<ChatMessage> messages) =>
            new(new Uri("http://localhost:1234/v1/chat/completions"), System.Text.Json.JsonSerializer.Serialize(messages), "Local");
        public Task<string> SendAsync(PreparedRequest request, string key, CancellationToken cancellation) { Sends++; return Task.FromResult(Response); }
    }
}
