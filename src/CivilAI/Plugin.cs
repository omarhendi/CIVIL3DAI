using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using CivilAI.Workspace;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(CivilAI.Plugin))]
[assembly: CommandClass(typeof(CivilAI.Plugin))]

namespace CivilAI;

public sealed partial class Plugin : IExtensionApplication
{
    private static PaletteSet? palette;
    private static InspectorPanel? panel;
    private static WorkspaceControl? workspace;
    private static DrawingSnapshot? lastSnapshot;
    private static (Document Document, DrawingTask Task, double Meters, long QueuedAt)? pendingDistance;

    public void Initialize()
    {
        Application.DocumentManager.DocumentActivated += DrawingChanged;
        Application.DocumentManager.DocumentToBeDestroyed += DrawingChanged;
    }

    public void Terminate()
    {
        Application.DocumentManager.DocumentActivated -= DrawingChanged;
        Application.DocumentManager.DocumentToBeDestroyed -= DrawingChanged;
        workspace?.Dispose();
        workspace = null;
        pendingDistance = null;
        ClearTriangleLocations();
        lastSnapshot = null;
        palette?.Dispose();
        palette = null;
        panel = null;
    }

    private static void DrawingChanged(object sender, DocumentCollectionEventArgs args)
    {
        pendingDistance = null;
        ClearTriangleLocations();
        lastSnapshot = null;
        workspace?.SetSnapshot(null);
        panel?.InvalidateDrawing("Drawing context changed.");
    }

    [CommandMethod("CIVILAI", CommandFlags.Modal)]
    public void Open()
    {
        EnsurePanel();
        palette!.Visible = true;
    }

    private static void LaunchDrawingTask(DrawingTask task, double? distanceMeters)
    {
        var document = Application.DocumentManager.MdiActiveDocument
            ?? throw new InvalidOperationException("Open a drawing first.");
        if (!document.Editor.IsQuiescent)
            throw new InvalidOperationException("Finish the active Civil 3D command first.");

        var command = task switch
        {
            DrawingTask.FindSlopes => "CIVILAIFINDSLOPES ",
            DrawingTask.AssessArea => "CIVILAIAREA ",
            DrawingTask.CreateTin => "CIVILAICREATETIN ",
            DrawingTask.AddTinBoundary => "CIVILAIBOUNDARY ",
            DrawingTask.SpotElevations => "CIVILAISPOTELEVATIONS ",
            DrawingTask.OffsetLevels => "CIVILAIOFFSETLEVELS ",
            _ => throw new ArgumentOutOfRangeException(nameof(task))
        };
        pendingDistance = distanceMeters is double meters ? (document, task, meters, Environment.TickCount64) : null;
        try { document.SendStringToExecute(command, true, false, true); }
        catch { pendingDistance = null; throw; }
    }

    private static double? ConsumeTaskDistance(Document document, DrawingTask task)
    {
        var pending = pendingDistance;
        pendingDistance = null;
        return pending is { } value && value.Document == document && value.Task == task &&
            Environment.TickCount64 - value.QueuedAt < 10000 ? value.Meters : null;
    }

    private static void EnsurePanel()
    {
        if (palette is not null) return;
        panel = new InspectorPanel(RequestRefreshAsync, LocateTriangleAsync);
        workspace = new WorkspaceControl(panel, async () =>
        {
            ClearTriangleLocations();
            lastSnapshot = null;
            workspace?.SetSnapshot(null);
            await RequestRefreshAsync();
            return lastSnapshot;
        }, launchDrawingTask: LaunchDrawingTask);
        palette = new PaletteSet("Civil AI 0.3", new Guid("B7A3A2CA-10C8-4575-BC7D-CFAE9601D313"))
        {
            Style = PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu,
            MinimumSize = new System.Drawing.Size(440, 600),
            Size = new System.Drawing.Size(640, 800),
            KeepFocus = true,
            DockEnabled = DockSides.Left | DockSides.Right
        };
        palette.AddVisual("Agent workspace", workspace);
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
        ClearTriangleLocations();
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
            lastSnapshot = snapshot;
            workspace?.SetSnapshot(snapshot);
            panel.Show(snapshot);
        }
        catch (System.Exception ex)
        {
            ClearTriangleLocations();
        lastSnapshot = null;
            workspace?.SetSnapshot(null);
            panel!.ShowError("Drawing inspection", ex);
            try { document?.Editor.WriteMessage($"\nCivil AI inspection failed: {ex}\n"); }
            catch (System.Exception loggingError) { panel.Stage($"Command history logging also failed: {loggingError}"); }
        }
        finally { panel?.EndRefresh(); }
    }
}
