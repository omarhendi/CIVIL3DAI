using System.Windows;
using System.Windows.Controls;

namespace CivilAI;

internal sealed class InspectorPanel : UserControl
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly ListBox objects = new() { DisplayMemberPath = nameof(DrawingItem.Label), Margin = new Thickness(8) };
    private readonly DataGrid properties = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, Margin = new Thickness(8) };

    public InspectorPanel(Action refresh)
    {
        var root = new DockPanel();
        var button = new Button { Content = "Refresh active drawing", Margin = new Thickness(8), Padding = new Thickness(6) };
        button.Click += (_, _) => refresh();
        DockPanel.SetDock(button, Dock.Top);
        root.Children.Add(button);
        DockPanel.SetDock(status, Dock.Top);
        root.Children.Add(status);
        objects.Height = 180;
        DockPanel.SetDock(objects, Dock.Top);
        root.Children.Add(objects);
        properties.Columns.Add(new DataGridTextColumn { Header = "Property", Binding = new System.Windows.Data.Binding("Name") });
        properties.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new System.Windows.Data.Binding("Value"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        objects.SelectionChanged += (_, _) => properties.ItemsSource = (objects.SelectedItem as DrawingItem)?.Properties;
        root.Children.Add(properties);
        Content = root;
        Clear("Run CIVILAI or refresh to inspect the active drawing.");
    }

    public void Clear(string message)
    {
        objects.ItemsSource = null;
        properties.ItemsSource = null;
        status.Text = message;
    }

    public void Show(DrawingSnapshot snapshot)
    {
        Clear($"{snapshot.Drawing}\n{snapshot.Items.Count(i => i.Kind == "TIN surface")} TIN surfaces; " +
              $"{snapshot.Items.Count(i => i.Kind == "Feature line")} feature lines. " +
              $"{snapshot.Unavailable} objects unavailable. Read-only snapshot; refresh after changes.");
        objects.ItemsSource = snapshot.Items;
        if (snapshot.Items.Count > 0) objects.SelectedIndex = 0;
    }
}
