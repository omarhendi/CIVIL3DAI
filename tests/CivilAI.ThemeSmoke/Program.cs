using System.Reflection;
using System.Xml.Linq;

using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CivilAI.DarkTheme.xaml")!;
var doc = XDocument.Load(stream);
XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
var brushes = doc.Root!.Elements().Where(e => e.Name.LocalName == "SolidColorBrush").ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string)e.Attribute("Color")!);
var count = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); count++; }
double Luminance(string color)
{
    var rgb = new[] { 1, 3, 5 }.Select(i => Convert.ToInt32(color.Substring(i, 2), 16) / 255d)
        .Select(c => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)).ToArray();
    return rgb[0] * 0.2126 + rgb[1] * 0.7152 + rgb[2] * 0.0722;
}
foreach (var text in new[] { "TextBrush", "MutedTextBrush" })
foreach (var background in new[] { "BackgroundBrush", "SurfaceBrush", "HoverBrush", "SelectedBrush", "DisabledBrush" })
{
    var a = Luminance(brushes[text]); var b = Luminance(brushes[background]);
    var ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    Check(ratio >= 4.5, $"{text}/{background}: {ratio:F2}:1 >= WCAG AA 4.5:1");
}
var styles = doc.Root.Elements().Where(e => e.Name.LocalName == "Style" && e.Attribute(x + "Key") is null).ToDictionary(e => (string)e.Attribute("TargetType")!);
foreach (var type in new[] { "TextBlock", "Label", "TextBox", "PasswordBox", "Button", "ComboBox", "ComboBoxItem", "CheckBox", "TabControl", "TabItem", "DataGrid", "DataGridRow", "DataGridCell", "DataGridColumnHeader", "ListBox", "ToolTip" })
    Check(styles[type].Elements().Any(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "Foreground" && (string?)e.Attribute("Value") == "{DynamicResource TextBrush}"), type + " has an explicit light foreground");
Check(doc.Descendants().Where(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "Foreground")
    .All(e => (string?)e.Attribute("Value") is "{DynamicResource TextBrush}" or "{DynamicResource MutedTextBrush}"), "Every foreground setter uses the accessible text palette, including triggers");
foreach (var type in new[] { "Button", "ComboBox", "TabItem", "DataGridColumnHeader" })
    Check(styles[type].Descendants().Any(e => e.Name.LocalName == "Trigger" && (string?)e.Attribute("Property") == "IsMouseOver"), type + " explicitly handles hover");
foreach (var type in new[] { "Button", "ComboBox", "TextBox", "PasswordBox", "DataGridCell" })
    Check(styles[type].Descendants().Any(e => e.Name.LocalName == "Trigger" && ((string?)e.Attribute("Property"))?.StartsWith("IsKeyboardFocus") == true), type + " explicitly handles keyboard focus");
Check(!doc.Descendants().Attributes("Property").Any(a => a.Value == "Opacity"), "Disabled text is not dimmed by opacity");
foreach (var type in new[] { "Button", "ComboBox", "ComboBoxItem", "TabItem", "TextBox", "PasswordBox", "DataGridCell" })
    Check(styles[type].Descendants().Any(e => e.Name.LocalName == "Trigger" && (string?)e.Attribute("Property") == "IsEnabled" && (string?)e.Attribute("Value") == "False"), type + " explicitly handles disabled state");
Console.WriteLine($"{count} theme checks passed (palette and declarations; Windows tests validate WPF resolution).");
