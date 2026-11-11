using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CivilAI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AgentChecks.Run();
        WorkspaceChecks.Run();
        var calls = 0;
        string? located = null;
        var panel = new InspectorPanel(() =>
        {
            calls++;
            return Task.FromException(new InvalidOperationException("Refresh probe failed"));
        }, key => { located = key; return Task.CompletedTask; });
        var controls = ((Grid)panel.Content).Children.Cast<UIElement>().ToArray();
        var status = controls.OfType<TextBlock>().Single();
        var diagnostics = controls.OfType<TextBox>().Single();
        var list = controls.OfType<ListBox>().Single();
        var detailTabs = controls.OfType<TabControl>().Single();
        var summary = (DataGrid)((TabItem)detailTabs.Items[0]).Content;
        var properties = (DataGrid)((TabItem)detailTabs.Items[1]).Content;
        var button = controls.OfType<Button>().Single();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Console.WriteLine($"PASS: {message}");
        }

        Check(status.Text.Contains("panel ready") && diagnostics.Text.Contains("Build"), "Panel initializes visible text and build identity without Autodesk APIs");
        panel.Show(new DrawingSnapshot("Empty.dwg", Array.Empty<DrawingItem>(), 0, 3, new[] { "Surface collection: 0" }));
        Check(status.Text.Contains("Empty.dwg") && status.Text.Contains("No native TIN"), "Empty drawings display their name and an explicit empty result");
        Check(properties.Items.Count == 5 && list.Items.Count == 0, "Drawing summary is visible without object selection");
        Check(diagnostics.Text.Contains("Scanned 3") && diagnostics.Text.Contains("Surface collection: 0"), "Completion diagnostics show scan counts");

        var first = new DrawingItem("TIN surface", "Ground", new[] { new PropertyValue("Name", "Ground") });
        var second = new DrawingItem("Feature line", "Edge", new[] { new PropertyValue("Name", "Edge") });
        panel.Show(new DrawingSnapshot("Site.dwg", new[] { first, second }, 1, 8, new[] { "Object read failed" }));
        Check(list.Items.Count == 2 && ReferenceEquals(properties.ItemsSource, first.Properties), "New inventory automatically selects and displays first object's properties");
        Check(detailTabs.SelectedIndex == 0 && summary.Items.Count > 0, "Surface selection opens summary");
        var limitFields = controls.OfType<WrapPanel>().Single().Children.OfType<TextBox>().ToArray();
        limitFields[0].Text = "5"; limitFields[1].Text = "2";
        Check(summary.Items.Cast<PropertyValue>().Any(p => p.Name == "Slope check" && p.Value.StartsWith("Invalid")), "Editing limits updates result and rejects reversed limits");
        limitFields[0].Text = "0";
        Check(summary.Items.Cast<PropertyValue>().Any(p => p.Name == "Slope check" && p.Value.StartsWith("Unavailable")), "Missing slopes never pass configured limits");
        list.SelectedIndex = 1;
        Check(ReferenceEquals(properties.ItemsSource, second.Properties), "Changing selection updates property rows");
        Check(diagnostics.Text.Contains("Object read failed"), "Partial inspection failures are visible");

        panel.BeginRefresh("Waiting for command context");
        Check(!button.IsEnabled && list.Items.Count == 0 && status.Text.Contains("Waiting"), "Pending refresh disables button and clears stale inventory");
        Check(summary.Items.Count == 0, "Refresh clears stale surface summary");
        panel.EndRefresh();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(calls == 0 && status.Text.Contains("Refresh clicked") && diagnostics.Text.Contains("Autodesk API has not been accessed yet"), "Click event writes diagnostics before invoking the API delegate");
        panel.Measure(new Size(440, 650));
        panel.Arrange(new Rect(0, 0, 440, 650));
        panel.UpdateLayout();
        Check(status.ActualHeight > 0 && diagnostics.ActualHeight > 0, "Output controls receive visible layout space");
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        Check(calls == 1 && status.Text.Contains("Refresh probe failed") && diagnostics.Text.Contains("InvalidOperationException"), "Button callback exceptions are caught and displayed");
        Check(diagnostics.Text.Contains("Refresh clicked"), "Exception output retains the click and stage trace");
        var location = new PropertyValue("Triangle #12", "Slope 170%; centre X=123456.123456, Y=987654.123456, Z=12.5");
        panel.Show(new DrawingSnapshot("Site.dwg", new[] { new DrawingItem("Triangle locations", "Ground", new[] { location }) }, 0, 1, Array.Empty<string>()));
        properties.SelectedItem = location;
        properties.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
        Check(located == "Triangle #12", "Double-click triangle finding invokes locate callback");
        Check(((DataGridTextColumn)properties.Columns[1]).ElementStyle.Setters.OfType<Setter>().Any(x => x.Property == TextBlock.TextWrappingProperty && Equals(x.Value, TextWrapping.Wrap)), "Coordinates wrap in property values");
        panel.InvalidateDrawing("No drawing is active.");
        Check(properties.Items.Count == 0 && status.Text.Contains("No drawing"), "Drawing context changes clear stale properties");
    }
}

