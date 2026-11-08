using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            Check(approvals == 1 && provider.Sends == 0, "Declining approval prevents provider send");
            approveNext = true;
            Named<CheckBox>("IncludeDrawingSnapshot").IsChecked = true;
            Click("Review & send");
            Check(approvals == 2 && provider.Sends == 1, "Approved snapshot request invokes provider exactly once");
            Check(Named<ListBox>("ConversationHistory").Items.Cast<ChatMessage>().Any(m => m.Role == "assistant" && m.Content == "response"), "Approved provider reply appears in history");
            Check(!Named<TextBox>("WorkspaceDiagnostics").Text.Contains("test-key"), "Diagnostics contain no key values");
            workspace.SetSnapshot(null);
            Check(Named<CheckBox>("IncludeDrawingSnapshot").IsChecked == false, "Drawing switch clears snapshot opt-in");
            tabs.SelectedIndex = 2;
            Check(store.LoadSettings().SelectedTab == 2 && store.LoadSettings().Provider == "Local", "Tab and provider UI settings persist");
            selector.ApplyTemplate();
            var selectionPresenter = selector.Template.FindName("SelectionPresenter", selector) as ContentPresenter;
            Check(selectionPresenter?.Content is not null, "Dark ComboBox template preserves selected agent display");
            for (var tab = 0; tab < tabs.Items.Count; tab++)
            {
                tabs.SelectedIndex = tab;
                workspace.Measure(new Size(640, 800)); workspace.Arrange(new Rect(0, 0, 640, 800)); workspace.UpdateLayout();
                Check(workspace.ActualHeight == 800 && tabs.ActualHeight > 0, $"Workspace tab {tab} lays out at palette size");
                foreach (var text in Descendants(workspace).OfType<TextBlock>())
                    Check(Contrast(text.Foreground, (Brush)workspace.Resources["SelectedBrush"]) >= 4.5,
                        $"Tab {tab} informational text stays light independently of selected-tab inheritance");
                foreach (var control in controls.OfType<Control>().Where(c => c is TextBox or PasswordBox or ComboBox or System.Windows.Controls.Button or DataGrid or TabItem))
                {
                    control.ApplyTemplate();
                    Check(Contrast(control.Foreground, control.Background ?? workspace.Background) >= 4.5,
                        $"{control.GetType().Name} normal/selected text has AA contrast");
                    var enabled = control.IsEnabled;
                    control.IsEnabled = false;
                    Check(Contrast(control.Foreground, control.Background ?? workspace.Background) >= 4.5,
                        $"{control.GetType().Name} disabled text has AA contrast");
                    control.IsEnabled = enabled;
                }
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
    private static double Contrast(Brush foreground, Brush background)
    {
        static double Light(Brush brush)
        {
            if (brush is not SolidColorBrush color || color.Color.A != 255)
                throw new InvalidOperationException("Expected opaque explicit theme brush.");
            static double Linear(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.Color.R) + .7152 * Linear(color.Color.G) + .0722 * Linear(color.Color.B);
        }
        var a = Light(foreground); var b = Light(background);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
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
