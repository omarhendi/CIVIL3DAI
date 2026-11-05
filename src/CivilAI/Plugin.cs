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
        panel?.InvalidateDrawing("Drawing context changed. Refresh to inspect the active drawing.");

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
        if (refreshing) { panel!.Stage("A refresh is already pending; finish the active AutoCAD command and wait."); return; }
        refreshing = true;
        try
        {
            panel!.Stage("Accessing AutoCAD DocumentManager.MdiActiveDocument.");
            var requested = Application.DocumentManager.MdiActiveDocument;
            if (requested is null) { panel!.InvalidateDrawing("No drawing is active. Open a drawing and refresh."); return; }
            panel!.BeginRefresh($"Waiting for AutoCAD command context: {requested.Name}");
            // Modeless palette callbacks must explicitly enter AutoCAD's command context.
            // Catch failures inside the callback: never let an exception escape into AutoCAD.
            await Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                try
                {
                    panel!.Stage("Entered AutoCAD command context; rechecking active document.");
                    if (Application.DocumentManager.MdiActiveDocument != requested)
                        panel!.InvalidateDrawing("The active drawing changed before inspection started. Refresh again.");
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
        Document? document = null;
        try
        {
            panel!.Stage("Accessing active document for inspection.");
            document = Application.DocumentManager.MdiActiveDocument;
            if (document is null) { panel.InvalidateDrawing("No drawing is active. Open a drawing and refresh."); return; }
            panel.BeginRefresh($"Inspecting {document.Name}");
            DrawingSnapshot snapshot;
            panel.Stage("Acquiring active document lock.");
            using (document.LockDocument())
            {
                panel.Stage("Document lock acquired; starting read-only inspection.");
                snapshot = Inspection.Read(document, panel.Stage);
            }
            panel.Stage("Read transaction and document lock released; updating WPF results.");
            panel.Show(snapshot);
        }
        catch (System.Exception ex)
        {
            panel!.ShowError("Drawing inspection", ex);
            try { document?.Editor.WriteMessage($"\nCivil AI inspection failed: {ex}\n"); }
            catch (System.Exception loggingError) { panel.Stage($"Command history logging also failed: {loggingError}"); }
        }
        finally { panel?.EndRefresh(); }
    }
}
