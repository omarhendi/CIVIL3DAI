using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(CivilAI.Plugin))]
[assembly: CommandClass(typeof(CivilAI.Plugin))]

namespace CivilAI;

public sealed class Plugin : IExtensionApplication
{
    private static PaletteSet? palette;
    private static InspectorPanel? panel;

    public void Initialize()
    {
        Application.DocumentManager.DocumentActivated += DrawingChanged;
        Application.DocumentManager.DocumentToBeDestroyed += DrawingChanged;
    }

    public void Terminate()
    {
        Application.DocumentManager.DocumentActivated -= DrawingChanged;
        Application.DocumentManager.DocumentToBeDestroyed -= DrawingChanged;
        palette?.Dispose();
        palette = null;
        panel = null;
    }

    private static void DrawingChanged(object sender, DocumentCollectionEventArgs args) =>
        panel?.Clear("Drawing context changed. Refresh to inspect the active drawing.");

    [CommandMethod("CIVILAI", CommandFlags.Modal)]
    public void Open()
    {
        EnsurePanel();
        palette!.Visible = true;
        Refresh();
    }

    private static void EnsurePanel()
    {
        if (palette is not null) return;
        panel = new InspectorPanel(() =>
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document is null) { panel?.Clear("No drawing is active."); return; }
            // Queue a registered command so database reads run on AutoCAD's command thread.
            document.SendStringToExecute("CIVILAIREFRESH ", true, false, false);
        });
        palette = new PaletteSet("Civil AI 0.1", new Guid("3F2AD61B-7191-4586-9E85-1CF30AB5A6EF"))
        {
            Style = PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu,
            MinimumSize = new System.Drawing.Size(320, 400),
            Size = new System.Drawing.Size(440, 650),
            DockEnabled = DockSides.Left | DockSides.Right
        };
        palette.AddVisual("Drawing inspector", panel);
    }

    [CommandMethod("CIVILAIREFRESH", CommandFlags.Modal)]
    public void Refresh()
    {
        EnsurePanel();
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) { panel!.Clear("No drawing is active."); return; }
        try { panel!.Show(Inspection.Read(document)); }
        catch (System.Exception ex)
        {
            panel!.Clear("Inspection failed. See the command line for details.");
            document.Editor.WriteMessage($"\nCivil AI inspection failed: {ex.Message}\n");
        }
    }
}
