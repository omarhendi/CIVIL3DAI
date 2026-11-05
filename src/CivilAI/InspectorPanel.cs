using System.Windows;
using System.Windows.Controls;

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

    public InspectorPanel(Func<Task> refresh)
    {
        Background = SystemColors.WindowBrush;
        Foreground = SystemColors.WindowTextBrush;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        var row = 0;
        button.Click += async (_, _) =>
        {
            try { await refresh(); }
            catch (Exception ex) { ShowError("Refresh button", ex); }
        };
        foreach (var control in new UIElement[] { button, status, diagnostics, objects, properties })
        {
            Grid.SetRow(control, row++);
            root.Children.Add(control);
        }
        properties.Columns.Add(new DataGridTextColumn { Header = "Property", Binding = new System.Windows.Data.Binding("Name") });
        properties.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new System.Windows.Data.Binding("Value"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        objects.SelectionChanged += (_, _) => properties.ItemsSource = (objects.SelectedItem as DrawingItem)?.Properties;
        Content = root;
        Clear("Run CIVILAI or refresh to inspect the active drawing.");
    }

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
        status.Text = message;
        diagnostics.Text = message;
    });

    public void BeginRefresh(string message) => Update(() =>
    {
        Clear(message);
        button.IsEnabled = false;
        diagnostics.Text = $"Refresh requested at {DateTimeOffset.Now:O}\n{message}";
    });

    public void EndRefresh() => Update(() => button.IsEnabled = true);

    public void ShowError(string stage, Exception exception) => Update(() =>
    {
        Clear($"{stage} failed: {exception.GetBaseException().Message}");
        diagnostics.Text = $"{DateTimeOffset.Now:O}\n{stage}\n{exception}";
    });

    public void Show(DrawingSnapshot snapshot) => Update(() =>
    {
        Clear($"Drawing: {snapshot.Drawing}\n{snapshot.Items.Count(i => i.Kind == "TIN surface")} TIN surfaces; " +
              $"{snapshot.Items.Count(i => i.Kind == "Feature line")} feature lines.\n" +
              (snapshot.Items.Count == 0 ? "No native TIN surfaces or feature lines were found in the inspected objects.\n" : "Select an object to view its properties.\n") +
              "Read-only snapshot. Refresh after drawing changes.");
        diagnostics.Text = $"Completed {DateTimeOffset.Now:O}\nScanned {snapshot.Scanned} unique objects; {snapshot.Unavailable} object reads failed.\n" +
                           string.Join("\n", snapshot.Diagnostics);
        objects.ItemsSource = snapshot.Items;
        if (snapshot.Items.Count > 0) objects.SelectedIndex = 0;
        else properties.ItemsSource = new[]
        {
            new PropertyValue("Drawing", snapshot.Drawing),
            new PropertyValue("TIN surfaces", "0"),
            new PropertyValue("Feature lines", "0"),
            new PropertyValue("Objects scanned", snapshot.Scanned.ToString()),
            new PropertyValue("Failed object reads", snapshot.Unavailable.ToString())
        };
    });
}
