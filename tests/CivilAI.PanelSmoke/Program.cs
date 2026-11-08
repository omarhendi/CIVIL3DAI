using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CivilAI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        WorkspaceChecks.Run();
        var calls = 0;
        var panel = new InspectorPanel(() =>
        {
            calls++;
            return Task.FromException(new InvalidOperationException("Refresh probe failed"));
        });
        var controls = ((Grid)panel.Content).Children.Cast<UIElement>().ToArray();
        var status = controls.OfType<TextBlock>().Single();
        var diagnostics = controls.OfType<TextBox>().Single();
        var list = controls.OfType<ListBox>().Single();
        var properties = controls.OfType<DataGrid>().Single();
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
        list.SelectedIndex = 1;
        Check(ReferenceEquals(properties.ItemsSource, second.Properties), "Changing selection updates property rows");
        Check(diagnostics.Text.Contains("Object read failed"), "Partial inspection failures are visible");

        panel.BeginRefresh("Waiting for command context");
        Check(!button.IsEnabled && list.Items.Count == 0 && status.Text.Contains("Waiting"), "Pending refresh disables button and clears stale inventory");
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
        panel.InvalidateDrawing("No drawing is active.");
        Check(properties.Items.Count == 0 && status.Text.Contains("No drawing"), "Drawing context changes clear stale properties");
    }
}
