using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Reflection;

namespace CivilAI;

internal sealed class InspectorPanel : UserControl
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly TextBox diagnostics = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 110,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(8)
    };
    private readonly Button button = new() { Content = "Refresh active drawing", Margin = new Thickness(8), Padding = new Thickness(6) };
    private readonly ListBox objects = new() { DisplayMemberPath = nameof(DrawingItem.Label), Margin = new Thickness(8) };
    private readonly DataGrid properties = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Margin = new Thickness(8) };

    private readonly DataGrid summary = new() { Name = "SurfaceSummary", IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Margin = new Thickness(8) };
    private readonly TabControl details = new();
    private readonly Func<Task> refresh;

    public InspectorPanel(Func<Task> refresh)
    {
        this.refresh = refresh;
        Background = SystemColors.WindowBrush;
        Foreground = SystemColors.WindowTextBrush;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        summary.Columns.Add(new DataGridTextColumn { Header = "Surface summary", Binding = new System.Windows.Data.Binding("Name") });
        var valueStyle = new Style(typeof(TextBlock));
        valueStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        summary.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new System.Windows.Data.Binding("Value"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = valueStyle });
        details.Items.Add(new TabItem { Header = "Surface Summary", Content = summary });
        details.Items.Add(new TabItem { Header = "All Properties", Content = properties });
        var row = 0;
        button.Click += RefreshClicked;
        foreach (var control in new UIElement[] { button, status, diagnostics, objects, details })
        {
            Grid.SetRow(control, row++);
            root.Children.Add(control);
        }
        properties.Columns.Add(new DataGridTextColumn { Header = "Property", Binding = new System.Windows.Data.Binding("Name") });
        properties.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new System.Windows.Data.Binding("Value"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        objects.SelectionChanged += (_, _) =>
        {
            var selected = objects.SelectedItem as DrawingItem;
            properties.ItemsSource = selected?.Properties;
            summary.ItemsSource = selected is null ? null : SurfaceSummary.Create(selected);
            details.SelectedIndex = selected?.Kind == "TIN surface" ? 0 : 1;
        };
        Content = root;
        Clear($"Civil AI panel ready. Build {BuildIdentity}. Click Refresh to inspect the active drawing.");
    }

    private static string BuildIdentity => typeof(InspectorPanel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private async void RefreshClicked(object sender, RoutedEventArgs args)
    {
        try
        {
            // WPF-only acknowledgement: deliberately before invoking any Autodesk-facing delegate.
            Clear("Refresh clicked — WPF output is active.");
            Stage($"Refresh clicked — click handler entered. Build {BuildIdentity}. Autodesk API has not been accessed yet.");
            button.IsEnabled = false;
            // Let WPF render the acknowledgement before synchronous host/API work can start.
            await Dispatcher.Yield(DispatcherPriority.Background);
            await refresh();
        }
        catch (Exception ex) { ShowError("Refresh button", ex); }
        finally { EndRefresh(); }
    }

    public void Stage(string message) => Update(() =>
    {
        status.Text = message;
        diagnostics.AppendText($"{DateTimeOffset.Now:HH:mm:ss.fff} {message}\n");
        diagnostics.ScrollToEnd();
    });

    // All control updates are dispatched to the WPF control's owning thread.
    private void Update(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.Invoke(action);
    }

    public void Clear(string message) => Update(() =>
    {
        objects.ItemsSource = null;
        properties.ItemsSource = null;
        summary.ItemsSource = null;
        status.Text = message;
        diagnostics.Text = message + "\n";
    });

    public void InvalidateDrawing(string message) => Update(() =>
    {
        objects.ItemsSource = null;
        properties.ItemsSource = null;
        summary.ItemsSource = null;
        Stage(message);
    });

    public void BeginRefresh(string message) => Update(() =>
    {
        objects.ItemsSource = null;
        properties.ItemsSource = null;
        summary.ItemsSource = null;
        button.IsEnabled = false;
        Stage(message);
    });

    public void EndRefresh() => Update(() => button.IsEnabled = true);

    public void ShowError(string stage, Exception exception) => Update(() =>
    {
        objects.ItemsSource = null;
        properties.ItemsSource = null;
        summary.ItemsSource = null;
        Stage($"{stage} failed: {exception.GetBaseException().Message}");
        diagnostics.AppendText(exception + "\n");
        diagnostics.ScrollToEnd();
    });

    public void Show(DrawingSnapshot snapshot) => Update(() =>
    {
        status.Text = $"Drawing: {snapshot.Drawing}\n{snapshot.Items.Count(i => i.Kind == "TIN surface")} TIN surfaces; " +
              $"{snapshot.Items.Count(i => i.Kind == "Feature line")} feature lines.\n" +
              (snapshot.Items.Count == 0 ? "No native TIN surfaces or feature lines were found in the inspected objects.\n" : "Select an object to view its properties.\n") +
              "Read-only snapshot. Refresh after drawing changes.";
        diagnostics.AppendText($"Completed {DateTimeOffset.Now:O}\nScanned {snapshot.Scanned} unique objects; {snapshot.Unavailable} object reads failed.\n" +
                           string.Join("\n", snapshot.Diagnostics) + "\n");
        diagnostics.ScrollToEnd();
        objects.ItemsSource = null;
        properties.ItemsSource = null;
        summary.ItemsSource = null;
        objects.ItemsSource = snapshot.Items;
        if (snapshot.Items.Count > 0) objects.SelectedIndex = 0;
        else { details.SelectedIndex = 1; properties.ItemsSource = new[]
        {
            new PropertyValue("Drawing", snapshot.Drawing),
            new PropertyValue("TIN surfaces", "0"),
            new PropertyValue("Feature lines", "0"),
            new PropertyValue("Objects scanned", snapshot.Scanned.ToString()),
            new PropertyValue("Failed object reads", snapshot.Unavailable.ToString())
        }; }
    });
}
