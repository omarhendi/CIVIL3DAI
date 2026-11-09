using System.IO;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace CivilAI.Workspace;

internal sealed class WorkspaceControl : UserControl, IDisposable
{
    private readonly WorkspaceStore store;
    private readonly WorkspaceSettings settings;
    private readonly IAiProvider provider;
    private readonly Func<PreparedRequest, bool, bool>? requestApproval;
    private readonly ConversationEngine engine = new();
    private readonly ReadOnlyToolRegistry tools;
    private readonly ObservableCollection<AgentProfile> agents = new();
    private readonly ObservableCollection<ChatMessage> history = new();
    private readonly ComboBox agentSelector = new() { MinWidth = 140, DisplayMemberPath = "Name", Margin = new Thickness(4) };
    private readonly ListBox transcript = new() { Margin = new Thickness(4), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 65, Margin = new Thickness(4) };
    private readonly TextBlock state = new() { Text = "Offline · No network request has been made.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly TextBox log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(6) };
    private readonly ComboBox providers = new() { ItemsSource = new[] { "Offline", "OpenAI", "Anthropic", "Google", "Local" }, Margin = new Thickness(4) };
    private readonly TextBox model = new() { Margin = new Thickness(4) };
    private readonly TextBox endpoint = new() { Margin = new Thickness(4) };
    private readonly PasswordBox key = new() { Margin = new Thickness(4) };
    private readonly TextBox markdown = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(6) };
    private readonly TextBox agentName = new() { IsReadOnly = true, Margin = new Thickness(6) };
    private readonly Button send;
    private readonly Button inspect;
    private readonly Button cancel;
    private readonly TabControl tabs = new() { Margin = new Thickness(6) };
    private readonly Grid chat = new();
    private readonly InspectorPanel inspector;
    private readonly Action<DrawingTask>? launchDrawingTask;
    private readonly Button runProposal;
    private DrawingTask? proposedTask;
    private long drawingContext;
    private AgentProfile? editing;
    private CancellationTokenSource? pending;
    private bool busy;
    private bool disposed;

    public WorkspaceControl(InspectorPanel inspector, Func<Task<DrawingSnapshot?>> capture, WorkspaceStore? storage = null, IAiProvider? aiProvider = null, Func<PreparedRequest, bool, bool>? approveRequest = null, Action<DrawingTask>? launchDrawingTask = null)
    {
        this.inspector = inspector;
        this.launchDrawingTask = launchDrawingTask;
        provider = aiProvider ?? new JsonAiProvider();
        requestApproval = approveRequest;
        store = storage ?? new WorkspaceStore();
        tools = new ReadOnlyToolRegistry(capture);
        agentSelector.Name = "AgentSelector";
        transcript.Name = "ConversationHistory";
        input.Name = "MessageInput";
        state.Name = "WorkspaceStatus";
        log.Name = "WorkspaceDiagnostics";
        providers.Name = "ProviderSelector";
        model.Name = "ProviderModel";
        endpoint.Name = "LocalEndpoint";
        key.Name = "SessionApiKey";
        markdown.Name = "AgentMarkdown";
        Resources = Theme();
        Background = Brush("#151923");
        Foreground = Brush("#E6EAF2");
        FontFamily = new FontFamily("Segoe UI");
        settings = Load(() => store.LoadSettings(), new WorkspaceSettings(), "Settings");
        FontSize = Math.Clamp(settings.FontSize, 11, 20);
        send = ActionButton("Review & send", SendAsync);
        runProposal = ActionButton("Run proposed task", async () =>
        {
            if (busy || proposedTask is not DrawingTask task) return;
            ClearProposal();
            await ExecuteDrawingTaskAsync(task);
        });
        runProposal.IsEnabled = false;
        inspect = ActionButton("Inspect drawing locally", InspectAsync);
        cancel = ActionButton("Cancel request", () => { pending?.Cancel(); return Task.CompletedTask; });
        cancel.IsEnabled = false;
        var root = new DockPanel();
        var header = new StackPanel { Margin = new Thickness(10, 8, 10, 0) };
        header.Children.Add(new TextBlock { Text = "CIVIL AI  /  0.3", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Brush("#70D8D0") });
        header.Children.Add(new TextBlock { Text = "Agent workspace · Drawing tasks", Margin = new Thickness(0, 3, 0, 6) });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(state, Dock.Bottom); root.Children.Add(state);
        root.Children.Add(tabs); Content = root;
        BuildChat(); BuildAgents(); BuildSettings();
        inspector.Background = Brush("#151923"); inspector.Foreground = Foreground;
        tabs.Items.Add(new TabItem { Header = "Drawing Inspector", Content = inspector });
        tabs.Items.Add(new TabItem { Header = "Diagnostics", Content = log });
        foreach (var agent in Load(() => store.LoadAgents(), Array.Empty<AgentProfile>() as IReadOnlyList<AgentProfile>, "Agents")) agents.Add(agent);
        if (agents.Count == 0)
        {
            var defaultAgent = new AgentProfile(Guid.NewGuid().ToString("N"), "# Civil drawing assistant\n\nExplain the user-approved drawing information. Be precise about units, unknown values and reference status. Suggest actions in prose only. Do not execute code or modify drawings.");
            agents.Add(defaultAgent);
            try { store.SaveAgent(defaultAgent.Id, defaultAgent.Markdown); } catch (Exception ex) { Error("Saving default agent", ex); }
        }
        agentSelector.ItemsSource = agents;
        agentSelector.SelectedItem = agents.FirstOrDefault(a => a.Id == settings.SelectedAgent) ?? agents[0];
        foreach (var message in Load(() => store.LoadConversation(), new List<ChatMessage>(), "Conversation"))
            if (message.Role is "user" or "assistant" or "local") { engine.Messages.Add(message); history.Add(message); }
        transcript.ItemsSource = history;
        tabs.SelectedIndex = Math.Clamp(settings.SelectedTab, 0, tabs.Items.Count - 1);
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs) SaveSettings(); };
        agentSelector.SelectionChanged += (_, _) => SaveSettings();
        Log("Workspace ready. Markdown is never executed. Drawing edits require confirmation in Civil 3D.");
    }

    private T Load<T>(Func<T> read, T fallback, string label)
    {
        try { return read(); } catch (Exception ex) { Error("Loading " + label, ex); return fallback; }
    }
    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static ResourceDictionary Theme() => (ResourceDictionary)XamlReader.Parse("""
      <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
        <Style TargetType="TextBox"><Setter Property="Background" Value="#202735"/><Setter Property="Foreground" Value="#E6EAF2"/><Setter Property="CaretBrush" Value="White"/><Setter Property="BorderBrush" Value="#44516A"/><Setter Property="Padding" Value="7"/></Style>
        <Style TargetType="PasswordBox"><Setter Property="Background" Value="#202735"/><Setter Property="Foreground" Value="White"/><Setter Property="CaretBrush" Value="White"/><Setter Property="Padding" Value="7"/></Style>
        <Style TargetType="Button"><Setter Property="Background" Value="#28374C"/><Setter Property="Foreground" Value="#F2F6FC"/><Setter Property="Margin" Value="4"/><Setter Property="Padding" Value="10,7"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Box" Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="Background" Value="#385570"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="Box" Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
        </Style>
        <Style TargetType="ComboBox"><Setter Property="Foreground" Value="Black"/><Setter Property="Background" Value="White"/><Setter Property="Padding" Value="6"/></Style>
        <Style TargetType="ListBox"><Setter Property="Background" Value="#191F2B"/><Setter Property="Foreground" Value="#E6EAF2"/><Setter Property="BorderBrush" Value="#344158"/></Style>
        <Style TargetType="CheckBox"><Setter Property="Foreground" Value="#E6EAF2"/></Style>
        <Style TargetType="TabControl"><Setter Property="Background" Value="#151923"/><Setter Property="BorderBrush" Value="#344158"/></Style>
        <Style TargetType="TabItem"><Setter Property="Background" Value="#202735"/><Setter Property="Foreground" Value="#AAB8CF"/><Setter Property="Padding" Value="8"/><Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Foreground" Value="Black"/></Trigger></Style.Triggers></Style>
        <Style TargetType="DataGrid"><Setter Property="Background" Value="#191F2B"/><Setter Property="Foreground" Value="#E6EAF2"/><Setter Property="RowBackground" Value="#202735"/><Setter Property="AlternatingRowBackground" Value="#273143"/></Style>
        <Style TargetType="DataGridCell"><Setter Property="Foreground" Value="#E6EAF2"/><Setter Property="Background" Value="Transparent"/></Style>
        <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="#344158"/><Setter Property="Foreground" Value="White"/></Style>
      </ResourceDictionary>
      """);
    private Button ActionButton(string label, Func<Task> action)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) => { try { await action(); } catch (OperationCanceledException) { Log("Request canceled. The service may already have received the approved request."); } catch (Exception ex) { Error(label, ex); } };
        return button;
    }
    private void BuildChat()
    {
        chat.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        chat.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Clamp(settings.HistoryRatio, 0.5, 8), GridUnitType.Star) });
        chat.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
        chat.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 145 });
        var toolbar = new WrapPanel();
        toolbar.Children.Add(new TextBlock { Text = "Agent", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) });
        toolbar.Children.Add(agentSelector); toolbar.Children.Add(inspect);
        toolbar.Children.Add(ActionButton("New conversation", () =>
        {
            if (busy) throw new InvalidOperationException("Cancel or finish the current request first.");
            if (MessageBox.Show("Clear this computer's saved conversation?", "New conversation", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            { ClearProposal(); engine.Messages.Clear(); history.Clear(); store.SaveConversation(engine.Messages); }
            return Task.CompletedTask;
        }));
        chat.Children.Add(toolbar);
        transcript.ItemTemplate = (DataTemplate)XamlReader.Parse("""
          <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Border Margin="4" Padding="10" Background="#222D3C" CornerRadius="6"><StackPanel><TextBlock Text="{Binding Role}" FontWeight="SemiBold" Foreground="#70D8D0" Margin="0,0,0,6"/><TextBox Text="{Binding Content, Mode=OneWay}" IsReadOnly="True" TextWrapping="Wrap" BorderThickness="0" Background="Transparent" Foreground="#E6EAF2"/></StackPanel></Border></DataTemplate>
          """);
        ScrollViewer.SetHorizontalScrollBarVisibility(transcript, ScrollBarVisibility.Disabled);
        Grid.SetRow(transcript, 1); chat.Children.Add(transcript);
        var splitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brush("#44516A"), ResizeDirection = GridResizeDirection.Rows };
        splitter.DragCompleted += (_, _) => SaveSettings(); Grid.SetRow(splitter, 2); chat.Children.Add(splitter);
        var composer = new DockPanel();
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Run drawing task", RunDrawingTaskAsync));
        actions.Children.Add(send);
        actions.Children.Add(runProposal);
        actions.Children.Add(cancel);
        DockPanel.SetDock(actions, Dock.Bottom); composer.Children.Add(actions);
        composer.Children.Add(input);
        Grid.SetRow(composer, 3); chat.Children.Add(composer);
        tabs.Items.Add(new TabItem { Header = "Chat", Content = chat });
    }
    private void BuildAgents()
    {
        var grid = new DockPanel();
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Create", () => { editing = null; agentName.Text = "New agent (save to add to selector)"; markdown.Text = "# New agent\n\nDescribe this agent's instructions here.\n"; return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("Edit selected", () => { editing = agentSelector.SelectedItem as AgentProfile ?? throw new InvalidOperationException("Select an agent in Chat."); agentName.Text = editing.Name; markdown.Text = editing.Markdown; return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("Save", () =>
        {
            var saved = store.SaveAgent(editing?.Id, markdown.Text);
            if (editing is not null) agents.Remove(editing);
            agents.Add(saved); editing = saved; agentName.Text = saved.Name; agentSelector.SelectedItem = saved; Log("Agent saved locally."); return Task.CompletedTask;
        }));
        actions.Children.Add(ActionButton("Import .md", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Markdown agents (*.md)|*.md" };
            if (dialog.ShowDialog() == true) { var saved = store.SaveAgent(null, WorkspaceStore.ReadMarkdown(dialog.FileName)); agents.Add(saved); editing = saved; markdown.Text = saved.Markdown; agentName.Text = saved.Name; agentSelector.SelectedItem = saved; Log("Markdown imported as text; no code executed."); }
            return Task.CompletedTask;
        }));
        actions.Children.Add(ActionButton("Export .md", () =>
        {
            if (string.IsNullOrWhiteSpace(markdown.Text)) throw new InvalidOperationException("Create or edit an agent first.");
            var dialog = new SaveFileDialog { Filter = "Markdown agents (*.md)|*.md", FileName = "civilai-agent.md", DefaultExt = ".md", AddExtension = true };
            if (dialog.ShowDialog() == true) { File.WriteAllText(dialog.FileName, markdown.Text); Log("Agent Markdown exported."); }
            return Task.CompletedTask;
        }));
        DockPanel.SetDock(actions, Dock.Top); grid.Children.Add(actions);
        DockPanel.SetDock(agentName, Dock.Top); grid.Children.Add(agentName);
        var note = new TextBlock { Text = "Use # Agent name as the heading. Markdown supplies instructions only; scripts and tool commands are never executed.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
        DockPanel.SetDock(note, Dock.Bottom); grid.Children.Add(note); grid.Children.Add(markdown);
        tabs.Items.Add(new TabItem { Header = "Agents", Content = grid });
    }
    private void BuildSettings()
    {
        var stack = new StackPanel { Margin = new Thickness(10) };
        void Field(string label, UIElement control) { stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 8, 4, 0), TextWrapping = TextWrapping.Wrap }); stack.Children.Add(control); }
        providers.SelectedItem = new[] { "Offline", "OpenAI", "Anthropic", "Google", "Local" }.Contains(settings.Provider) ? settings.Provider : "Offline";
        providers.SelectionChanged += (_, _) => key.Clear();
        model.Text = settings.Model; endpoint.Text = settings.LocalEndpoint;
        Field("Provider (Offline by default)", providers); Field("Model identifier (from your provider)", model);
        Field("Local compatible base URL (include /v1/ if required; HTTPS or loopback HTTP)", endpoint);
        Field("API key — session only, never saved to disk or logs", key);
        var font = new TextBox { Text = FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture), Margin = new Thickness(4) };
        Field("Text size (11–20)", font);
        stack.Children.Add(ActionButton("Save non-secret settings", () =>
        {
            if (!double.TryParse(font.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var size) || !double.IsFinite(size) || size < 11 || size > 20) throw new ArgumentException("Text size must be between 11 and 20.");
            FontSize = size; if (SaveSettings()) Log("Non-secret settings saved. No connection test or request was sent."); return Task.CompletedTask;
        }));
        stack.Children.Add(new TextBlock { Text = $"History, agent Markdown and settings are saved in plain text on this computer:\n{store.Root}\n\nReview & send always previews the exact outbound JSON and destination. Only the current message and built-in copilot instructions are sent. Drawing snapshots, prior messages and agent files stay on this computer. You choose whether to run a proposed task, and Civil 3D asks before creating drawing objects.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 14, 6, 6) });
        tabs.Items.Add(new TabItem { Header = "Providers", Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
    }
    public void SetSnapshot(DrawingSnapshot? value)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetSnapshot(value)); return; }
        if (value is null)
        {
            drawingContext++;
            ClearProposal();
        }
    }
    private void ClearProposal()
    {
        proposedTask = null;
        runProposal.IsEnabled = false;
    }

    private async Task RunDrawingTaskAsync()
    {
        if (busy) return;
        var request = input.Text.Trim();
        var task = DrawingTaskRequest.Parse(request);
        if (task is null)
        {
            Log("Try “inspect the drawing”, “find steep triangles”, or “create an offset feature line from reference elevations”.");
            return;
        }

        ClearProposal();
        await ExecuteDrawingTaskAsync(task.Value);
    }

    private async Task ExecuteDrawingTaskAsync(DrawingTask task)
    {
        if (task == DrawingTask.Inspect)
        {
            await InspectAsync();
        }
        else
        {
            if (launchDrawingTask is null)
            {
                Log("This task requires Civil 3D.");
                return;
            }

            launchDrawingTask(task);
            Log("Follow the prompts in the Civil 3D command line.");
            if (task is DrawingTask.CreateTin or DrawingTask.OffsetLevels) return;
        }

        tabs.SelectedIndex = 3;
    }

    private async Task InspectAsync()
    {
        if (busy) throw new InvalidOperationException("Finish or cancel the current request first.");
        Log("Running local read-only drawing.inspect tool. No data will be sent.");
        var result = await tools.ExecuteAsync("drawing.inspect");
        if (result is null) { Log("Inspection returned no snapshot. See Drawing Inspector diagnostics."); return; }
        SetSnapshot(result);
        Append(new ChatMessage("local", $"Local inspection: {Path.GetFileName(result.Drawing)}\n{result.Items.Count(i => i.Kind == "TIN surface")} TIN surfaces; {result.Items.Count(i => i.Kind == "Feature line")} feature lines. Details are in Drawing Inspector. This snapshot has not been sent to a provider."));
    }
    private async Task SendAsync()
    {
        if (busy) return;
        if ((string?)providers.SelectedItem == "Offline") { Log("Offline: compose messages, manage agents and inspect drawings locally. Select a provider to send a message."); return; }
        var prompt = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Enter a message.");
        var requestContext = drawingContext;
        var config = new ProviderConfiguration(providers.SelectedItem as string ?? "Offline", model.Text.Trim(), endpoint.Text.Trim());
        var request = provider.Prepare(config, new[] { new ChatMessage("system", DrawingTaskProposal.Instructions), new ChatMessage("user", prompt) });
        if (Encoding.UTF8.GetByteCount(request.Body) > 1_048_576) throw new InvalidOperationException("Request exceeds 1 MiB. Shorten your message.");
        if (config.Provider != "Local" && string.IsNullOrWhiteSpace(key.Password)) throw new InvalidOperationException("Enter a session API key in Providers.");
        if (!(requestApproval?.Invoke(request, false) ?? ConfirmRequest(request, false))) { Log("Send canceled before network access."); return; }
        ClearProposal();
        busy = true; send.IsEnabled = false; inspect.IsEnabled = false; cancel.IsEnabled = true;
        pending = new CancellationTokenSource();
        var sessionKey = key.Password;
        try
        {
            Append(new ChatMessage("user", prompt)); input.Clear();
            Log($"Sending approved request to {request.Endpoint.Host}. Waiting for response…");
            var reply = await provider.SendAsync(request, sessionKey, pending.Token);
            if (disposed || pending.IsCancellationRequested) return;
            var proposal = DrawingTaskProposal.Parse(reply);
            if (proposal is null)
            {
                Log("The model returned an unsupported response. No task was proposed. Try rephrasing or use another model.");
                return;
            }

            var (action, message) = proposal.Value;
            if (action is DrawingTask task && requestContext == drawingContext)
            {
                proposedTask = task;
                runProposal.IsEnabled = true;
                Append(new ChatMessage("assistant", $"Proposed task: {DrawingTaskProposal.Label(task)}\n\n{message}"));
                Log("Review the proposed task, then choose Run proposed task.");
            }
            else
            {
                Append(new ChatMessage("assistant", message));
                Log(action is null ? "Response received." : "Drawing context changed. Send the request again to propose a task for this drawing.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // HTTP exceptions may embed response data; display a safe actionable summary only.
            Log("Provider request failed: " + (ex is System.Net.Http.HttpRequestException ? (ex is System.Net.Http.HttpRequestException http && http.StatusCode is not null ? $"HTTP {(int)http.StatusCode}: check model, credentials and service limits; retry manually." : "HTTP/network failure. Check endpoint, service availability and credentials; retry manually.") : ex.GetType().Name + ": check provider model and response format. Details are intentionally excluded to protect credentials and project data."));
        }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                pending?.Dispose(); pending = null; busy = false;
                send.IsEnabled = true; inspect.IsEnabled = true; cancel.IsEnabled = false;
            });
        }
    }
    private bool ConfirmRequest(PreparedRequest request, bool drawingIncluded)
    {
        var window = new Window { Title = "Approve this request", Width = 760, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Background, Foreground = Foreground, Resources = Resources };
        var root = new DockPanel { Margin = new Thickness(12) };
        var summary = new TextBlock { Text = $"Destination: {request.Endpoint}\nDrawing snapshot included: {drawingIncluded}\nThe exact JSON below contains the current message and built-in copilot instructions. Review it for project data before approving. Credentials are sent in headers and are not shown. Approval applies to this request only.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 12) };
        DockPanel.SetDock(summary, Dock.Top); root.Children.Add(summary);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var approve = new Button { Content = "Approve and send this request" }; approve.Click += (_, _) => window.DialogResult = true;
        var reject = new Button { Content = "Cancel", IsCancel = true }; actions.Children.Add(reject); actions.Children.Add(approve);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        root.Children.Add(new TextBox { Text = request.Body, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = root; return window.ShowDialog() == true;
    }
    private void Append(ChatMessage message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Append(message)); return; }
        if (disposed) return;
        engine.Messages.Add(message); history.Add(message); transcript.ScrollIntoView(message);
        try { store.SaveConversation(engine.Messages); } catch (Exception ex) { Error("Saving conversation", ex); }
    }
    private bool SaveSettings()
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(SaveSettings);
        if (disposed) return false;
        settings.Provider = providers.SelectedItem as string ?? "Offline";
        settings.Model = model.Text; settings.LocalEndpoint = endpoint.Text;
        settings.SelectedAgent = (agentSelector.SelectedItem as AgentProfile)?.Id ?? "";
        settings.SelectedTab = Math.Max(0, tabs.SelectedIndex); settings.FontSize = FontSize;
        var bottom = chat.RowDefinitions.Count > 3 ? chat.RowDefinitions[3].ActualHeight : 0;
        if (bottom > 0) settings.HistoryRatio = Math.Clamp(chat.RowDefinitions[1].ActualHeight / bottom, 0.5, 8);
        try { store.SaveSettings(settings); return true; }
        catch (Exception ex) { Error("Saving settings", ex); return false; }
    }
    private void Error(string stage, Exception error) => Log(stage + ": " + error.GetType().Name + ": " + error.Message);
    private void Log(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => Log(message)); return; }
        if (disposed) return;
        state.Text = message;
        log.AppendText($"{DateTimeOffset.Now:O} {message}\n");
        if (log.Text.Length > 24_000) log.Text = log.Text[^20_000..];
        log.ScrollToEnd();
    }
    public void Dispose()
    {
        SaveSettings(); disposed = true; pending?.Cancel(); (provider as IDisposable)?.Dispose(); key.Clear();
    }
}
