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
        panel = new InspectorPanel(RequestRefreshAsync);
        palette = new PaletteSet("Civil AI 0.1", new Guid("3F2AD61B-7191-4586-9E85-1CF30AB5A6EF"))
        {
            Style = PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu,
            MinimumSize = new System.Drawing.Size(320, 400),
            Size = new System.Drawing.Size(440, 650),
            DockEnabled = DockSides.Left | DockSides.Right
        };
        palette.AddVisual("Drawing inspector", panel);
    }

    private static bool refreshing;

    private static async Task RequestRefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            var requested = Application.DocumentManager.MdiActiveDocument;
            if (requested is null) { panel!.Clear("No drawing is active. Open a drawing and refresh."); return; }
            panel!.BeginRefresh($"Waiting for AutoCAD command context: {requested.Name}");
            // Modeless palette callbacks must explicitly enter AutoCAD's command context.
            // Catch failures inside the callback: never let an exception escape into AutoCAD.
            await Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                try
                {
                    if (Application.DocumentManager.MdiActiveDocument != requested)
                        panel!.Clear("The active drawing changed before inspection started. Refresh again.");
                    else
                        InspectActiveDrawing();
                }
                catch (System.Exception ex) { panel!.ShowError("Refresh callback", ex); }
                return Task.CompletedTask;
            }, null);
        }
        catch (System.Exception ex) { panel!.ShowError("Scheduling refresh", ex); }
        finally
        {
            refreshing = false;
            panel?.EndRefresh();
        }
    }

    [CommandMethod("CIVILAIREFRESH", CommandFlags.Modal)]
    public void Refresh()
    {
        EnsurePanel();
        InspectActiveDrawing();
    }

    private static void InspectActiveDrawing()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null) { panel!.Clear("No drawing is active. Open a drawing and refresh."); return; }
        try
        {
            panel!.BeginRefresh($"Inspecting {document.Name}");
            DrawingSnapshot snapshot;
            using (document.LockDocument())
                snapshot = Inspection.Read(document);
            panel.Show(snapshot);
        }
        catch (System.Exception ex)
        {
            panel!.ShowError("Drawing inspection", ex);
            document.Editor.WriteMessage($"\nCivil AI inspection failed: {ex}\n");
        }
        finally { panel?.EndRefresh(); }
    }
}
