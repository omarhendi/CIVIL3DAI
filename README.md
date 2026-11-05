# Civil AI 0.1

Native C#/.NET 8, x64 WPF plugin for Autodesk Civil 3D 2026. This first milestone is a read-only drawing inspector. No AI services, keys, telemetry or external data transmission are implemented.

## Build without a local development installation

GitHub Actions builds on a hosted Windows runner; no Autodesk application or development software is needed on your workstation to obtain the DLL. Open the repository's **Actions → Build Civil AI**, select a successful run for `civilai-v0.1`, and download the `CivilAI-0.1-<commit>` artifact. Extract `CivilAI.dll` for testing inside Civil 3D 2026.

For a manual build on Windows or a Linux cloud machine with the .NET 8 SDK:

```powershell
dotnet restore src/CivilAI/CivilAI.csproj --source https://api.nuget.org/v3/index.json
dotnet build src/CivilAI/CivilAI.csproj -c Release --no-restore
```

The project targets `net8.0-windows`, WPF, and x64, with Windows cross-targeting enabled. It uses official Autodesk-owned NuGet packages pinned to exact versions: `AutoCAD.NET` **25.1.0** (including its Core and Model dependencies) and `Civil3D.NET` **13.8.280**, both for the **2026** products. The Civil references explicitly include `AeccDbMgd.dll` and `AecBaseMgd.dll` (the latter is spelled AecBaseMgd, not AeccBaseMgd).

AutoCAD package assets are compile-only. Civil package DLL paths are resolved through NuGet's generated `PkgCivil3D_NET` property and referenced with `Private=false`; package runtime assets are excluded. No machine-specific Autodesk paths or local props file are needed. Autodesk supplies the runtime assemblies inside Civil 3D; do not distribute them with the plugin. CI rejects any dependency DLL in output and uploads only `CivilAI.dll`.

## Load and use

1. Download the CI artifact or build the DLL, then open Civil 3D 2026 (plain AutoCAD is insufficient).
2. Add your build output directory to Civil 3D's trusted locations using its security settings; retain secure loading.
3. Run `NETLOAD` and select `src/CivilAI/bin/Release/net8.0-windows/CivilAI.dll`.
4. Run `CIVILAI`. Dock or float the palette. Select a surface or feature line to see scalar public properties.
5. Use **Refresh active drawing** after edits. The palette clears when the drawing context changes. The button first displays **Refresh clicked** using WPF alone and yields to the dispatcher for rendering before any Autodesk API call, then shows a waiting status, then uses `ExecuteInCommandContextAsync` to enter AutoCAD's command context. The active document is rechecked, explicitly locked, and inspected in a read transaction. `CIVILAIREFRESH` remains available as a direct command.

The inspector reads Civil's surface collection and native entities in local block definitions/layouts. It lists TIN surfaces (including TIN-derived types) and feature lines, deduplicated by ObjectId. External-reference block contents are excluded; data-reference objects exposed as native entities are included. This is an entity inventory, not an expanded list of block insert instances. Properties are scalar public API getters, including IDs and handles. Collections, geometry graphs, calculated statistics methods and linked object traversal are excluded. Getters unavailable for an object are marked accordingly.

All database objects are opened `ForRead` inside a read transaction; no write access, geometry creation, transaction commit, drawing save or drawing modification commands are used. Live Autodesk objects never reach the WPF UI. The property grid is read-only. Snapshots do not update automatically after geometry edits. Empty results still display the drawing name, zero counts, and drawing summary rows. The panel displays the assembly build identity. Its diagnostics box retains timestamped stages for click, document access, command context, document lock, transaction startup, Civil surface collection, block scan and output update, followed by completion time, scanned object count, failed reads and exception details (up to 100 messages). Property getter errors appear in the property grid and diagnostics. If refresh is waiting, finish or cancel the current AutoCAD command and check the panel again.

## Validation

The full plugin compiles in Linux cloud using official 2026 references. This does not validate Civil 3D behavior; in-host tests remain required. See `VALIDATION.md` for build warnings.

Before accepting a release, perform these checks in Civil 3D 2026:

- Download or build the plugin against the pinned official 2026 references; verify no Autodesk DLLs appear in output.
- In a drawing with a known TIN surface and both site-associated and siteless feature lines, verify inventory names, counts and property values against Civil 3D's UI.
- Test an empty drawing: verify its name, zero TIN/feature-line counts, drawing summary rows and completion timestamp. Test unavailable data references and xrefs: confirm diagnostics and the documented scope.
- Click Refresh with the palette focused and with another command pending: verify a waiting status, then completion when AutoCAD becomes available. Switch drawings before execution and confirm it asks for another refresh instead of inspecting stale context.
- Switch between two drawings, close the active drawing, and refresh; confirm stale data clears and the new drawing is inspected.
- Close/reopen and dock/undock the palette; verify repeated commands reuse it.
- Record DBMOD before inspection in an otherwise idle drawing; verify inspection and refresh do not change it. Close without saving and confirm no changes attributable to the plugin.

These in-host checks have not been run in the cloud environment. See `VALIDATION.md` for the actual build results.

## CI

On push, pull request, or manual dispatch, `.github/workflows/build.yml` restores official packages, builds Release on `windows-latest`, runs WPF refresh/binding smoke checks, rejects any DLL other than `CivilAI.dll` in output, and uploads only that file with `actions/upload-artifact`. No licensed self-hosted runner is needed for compilation. CI does not launch Civil 3D or establish runtime compatibility. The repository's Actions settings must permit workflows to run.

## Direction

Future milestones may introduce custom Markdown agents and landscape grading automation inspired by SketchPro. Neither agent execution nor write operations are part of 0.1.
