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
        var approveNext = false;
        var provider = new ProbeProvider();
        var workspace = new WorkspaceControl(new InspectorPanel(() => Task.CompletedTask),
            () => { toolCalls++; return Task.FromResult<DrawingSnapshot?>(snapshot); }, store, provider,
            (request, drawingIncluded) =>
            {
                approvals++;
                Check(drawingIncluded == request.Body.Contains("Test.dwg"), "Approval preview includes drawing only after explicit opt-in");
                return approveNext;
            });
        try
        {
            var controls = Descendants(workspace).ToArray();
            var tabs = controls.OfType<TabControl>().Single();
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
            Named<TextBox>("MessageInput").Text = "Explain the drawing";
            Click("Review & send");
            Check(approvals == 0 && provider.Sends == 0, "Offline mode does not attempt approval or network");
            Named<PasswordBox>("SessionApiKey").Password = "test-key";
            Named<ComboBox>("ProviderSelector").SelectedItem = "Local";
            Check(Named<PasswordBox>("SessionApiKey").Password.Length == 0, "Changing provider clears the previous session key");
            Named<TextBox>("ProviderModel").Text = "test-model";
            Click("Review & send");
            Check(approvals == 0 && provider.Sends == 0, "Offline milestone prevents provider approval");
            approveNext = true;
            Named<CheckBox>("IncludeDrawingSnapshot").IsChecked = true;
            Click("Review & send");
            Check(approvals == 0 && provider.Sends == 0, "Offline milestone blocks even opted-in snapshot requests");
            Check(!Named<ListBox>("ConversationHistory").Items.Cast<ChatMessage>().Any(m => m.Role == "assistant"), "Offline milestone has no provider reply");
            Check(!Named<TextBox>("WorkspaceDiagnostics").Text.Contains("test-key"), "Diagnostics contain no key values");
            workspace.SetSnapshot(null);
            Check(Named<CheckBox>("IncludeDrawingSnapshot").IsChecked == false, "Drawing switch clears snapshot opt-in");
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
        public PreparedRequest Prepare(ProviderConfiguration configuration, IReadOnlyList<ChatMessage> messages) =>
            new(new Uri("http://localhost:1234/v1/chat/completions"), System.Text.Json.JsonSerializer.Serialize(messages), "Local");
        public Task<string> SendAsync(PreparedRequest request, string key, CancellationToken cancellation) { Sends++; return Task.FromResult("response"); }
    }
}

