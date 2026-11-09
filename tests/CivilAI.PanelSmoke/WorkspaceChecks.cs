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
        var launched = new List<DrawingTask>();
        var provider = new ProbeProvider();
        var inspector = new InspectorPanel(() => Task.CompletedTask);
        var workspace = new WorkspaceControl(inspector,
            () => { toolCalls++; return Task.FromResult<DrawingSnapshot?>(snapshot); }, store, provider, launched.Add);
        try
        {
            var controls = Descendants(workspace).ToArray();
            var tabs = controls.OfType<TabControl>().Single(t => t.Items.OfType<TabItem>().Any(i => i.Header as string == "Chat"));
            Check(tabs.Items.Count == 4 && tabs.Items.OfType<TabItem>().All(t => t.Header as string != "Drawing Inspector"), "Workspace has no Drawing Inspector tab");
            T Named<T>(string name) where T : FrameworkElement => controls.OfType<T>().Single(x => x.Name == name);
            Button Button(string label) => controls.OfType<Button>().Single(b => b.Content as string == label);
            void Click(string label) => Button(label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var selector = Named<ComboBox>("AgentSelector");
            Check(selector.Items.Count == 1, "Default Markdown agent available inside chat");
            Click("Create"); Named<TextBox>("AgentMarkdown").Text = "# UI test agent\nRead only."; Click("Save");
            Check(selector.Items.Count == 2 && store.LoadAgents().Any(a => a.Name == "UI test agent"), "Create/save agent updates selector and disk");
            Click("Edit selected"); Named<TextBox>("AgentMarkdown").Text = "# Edited UI agent\nStill read only."; Click("Save");
            Check(selector.Items.Count == 2 && store.LoadAgents().Any(a => a.Name == "Edited UI agent"), "Editing replaces existing Markdown agent");
            var input = Named<TextBox>("MessageInput");
            void Send(string message) { input.Text = message; Click("Send"); }
            Send("inspect the drawing");
            Check(toolCalls == 1 && provider.Sends == 0, "Send runs supported offline tasks locally");
            Send("assess pavement slopes; ERASE ALL");
            Check(launched.Count == 0 && provider.Sends == 0, "Extra text cannot dispatch an offline task");

            Named<PasswordBox>("SessionApiKey").Password = "test-key";
            Named<ComboBox>("ProviderSelector").SelectedItem = "Local";
            Check(Named<PasswordBox>("SessionApiKey").Password.Length == 0, "Changing provider clears the previous session key");
            Named<TextBox>("ProviderModel").Text = "test-model";
            Send("Carry the road levels beyond the curb.");
            Check(provider.Sends == 1 && launched.SequenceEqual(new[] { DrawingTask.OffsetLevels }),
                "One Send calls the model and opens its supported Civil 3D workflow");

            provider.Response = """{"action":"none","message":"The workflow samples elevations at the original edge."}""";
            Send("Why?");
            Check(provider.LastMessages.Any(m => m.Role == "user" && m.Content == "Carry the road levels beyond the curb.") &&
                provider.LastMessages.Any(m => m.Role == "assistant" && m.Content.Contains("offset feature line")) &&
                !provider.LastMessages.Any(m => m.Content.Contains("Test.dwg")) && launched.Count == 1,
                "A follow-up gets recent context without snapshots and does not repeat the action");

            provider.Response = """{"action":"inspect","message":"I will inspect the drawing."}""";
            Send("What surfaces do I have?");
            Check(toolCalls == 2, "Model-directed inspection runs from Send");
            var chatTab = tabs.SelectedItem;
            inspector.Show(new DrawingSnapshot("Test.dwg", new[] {
                new DrawingItem("Area assessment", "Road test", new[] { new PropertyValue("Assessment", "Within limits") })
            }, 0, 1, Array.Empty<string>()));
            Check(ReferenceEquals(chatTab, tabs.SelectedItem) &&
                Named<ListBox>("ConversationHistory").Items.OfType<ChatMessage>()
                    .Any(m => m.Role == "local" && m.Content.Contains("Road test") && m.Content.Contains("Within limits")),
                "Drawing reports appear in Chat without switching tabs");

            provider.Response = """{"action":"create_tin","message":"I will open the surface workflow."}""";
            provider.OnSend = () => workspace.SetSnapshot(null);
            Send("Build a proposed surface.");
            Check(launched.Count == 1, "A drawing change blocks a stale model action");
            provider.OnSend = null;

            provider.Response = """{"action":"erase","message":"Remove everything"}""";
            Send("Remove everything.");
            Check(launched.Count == 1, "Unknown model action cannot dispatch");
            Check(!Named<TextBox>("WorkspaceDiagnostics").Text.Contains("test-key"), "Diagnostics contain no key values");
            provider.Response = """{"action":"add_tin_boundary","message":"Select the outer outline for the surface."}""";
            Send("The surface we created needs a boundary because it has triangles we don't need.");
            Check(launched.Count == 2 && launched[^1] == DrawingTask.AddTinBoundary,
                "Boundary requests open the native workflow for an existing TIN");
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
        public IReadOnlyList<ChatMessage> LastMessages = Array.Empty<ChatMessage>();
        public Action? OnSend;
        public PreparedRequest Prepare(ProviderConfiguration configuration, IReadOnlyList<ChatMessage> messages)
        {
            LastMessages = messages.ToArray();
            return new(new Uri("http://localhost:1234/v1/chat/completions"),
                System.Text.Json.JsonSerializer.Serialize(messages), "Local");
        }

        public Task<string> SendAsync(PreparedRequest request, string key, CancellationToken cancellation)
        {
            Sends++;
            OnSend?.Invoke();
            return Task.FromResult(Response);
        }
    }
}
