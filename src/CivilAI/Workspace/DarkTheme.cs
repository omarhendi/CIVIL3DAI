using System.Windows;
using System.Windows.Markup;

namespace CivilAI.Workspace;

internal static class DarkTheme
{
    public static ResourceDictionary Create()
    {
        using var stream = typeof(DarkTheme).Assembly.GetManifestResourceStream("CivilAI.DarkTheme.xaml")
            ?? throw new InvalidOperationException("Embedded dark theme is missing.");
        return (ResourceDictionary)XamlReader.Load(stream);
    }
}
