# Civil AI 0.1

Native C#/.NET 8, x64 WPF plugin for Autodesk Civil 3D 2026. This first milestone is a read-only drawing inspector. No AI services, keys, telemetry or external data transmission are implemented.

## Windows build

Install the .NET 8 SDK and a licensed Civil 3D 2026 installation. Use PowerShell from the repository root:

```powershell
dotnet restore src/CivilAI/CivilAI.csproj
dotnet build src/CivilAI/CivilAI.csproj -c Release --no-restore `
  -p:AutoCAD2026Dir="C:\Program Files\Autodesk\AutoCAD 2026" `
  -p:Civil3D2026Dir="C:\Program Files\Autodesk\AutoCAD 2026\C3D"
```

Paths default to the directories above. Override them on the command line or create an ignored `Autodesk.local.props` at the repository root:

```xml
<Project>
  <PropertyGroup>
    <AutoCAD2026Dir>D:\Autodesk\AutoCAD 2026</AutoCAD2026Dir>
    <Civil3D2026Dir>D:\Autodesk\AutoCAD 2026\C3D</Civil3D2026Dir>
  </PropertyGroup>
</Project>
```

Required references: AcCoreMgd.dll, AcDbMgd.dll, AcMgd.dll and AeccDbMgd.dll from the **2026** installation. Keep their installation dependencies available to the compiler. Autodesk assemblies are proprietary: do not commit, package or redistribute them. References use `Private=false`. Compilation fails explicitly when the required references are missing. Do not substitute stubs and treat that as a successful Autodesk build.

## Load and use

1. Build on Windows and open Civil 3D 2026 (plain AutoCAD is insufficient).
2. Add your build output directory to Civil 3D's trusted locations using its security settings; retain secure loading.
3. Run `NETLOAD` and select `src/CivilAI/bin/Release/net8.0-windows/CivilAI.dll`.
4. Run `CIVILAI`. Dock or float the palette. Select a surface or feature line to see scalar public properties.
5. Use **Refresh active drawing** after edits. The palette clears when the drawing context changes. Refresh is queued through the registered `CIVILAIREFRESH` command, so it waits for AutoCAD's command context.

The inspector reads Civil's surface collection and native entities in local block definitions/layouts. It lists TIN surfaces (including TIN-derived types) and feature lines, deduplicated by ObjectId. External-reference block contents are excluded; data-reference objects exposed as native entities are included. This is an entity inventory, not an expanded list of block insert instances. Properties are scalar public API getters, including IDs and handles. Collections, geometry graphs, calculated statistics methods and linked object traversal are excluded. Getters unavailable for an object are marked accordingly.

All database objects are opened `ForRead` inside a read transaction; no write access, geometry creation, transaction commit, drawing save or drawing modification commands are used. Live Autodesk objects never reach the WPF UI. The property grid is read-only. Snapshots do not update automatically after geometry edits.

## Validation

A real build requires Autodesk's proprietary references. A Linux SDK can evaluate the project and report missing references, but cannot validate Civil 3D behavior. The initial cloud checkout was empty; no existing application or tests were present.

Before accepting a release, perform these checks in Civil 3D 2026:

- Build successfully against installed 2026 assemblies; verify no Autodesk DLLs appear in output.
- In a drawing with a known TIN surface and both site-associated and siteless feature lines, verify inventory names, counts and property values against Civil 3D's UI.
- Test an empty drawing, unavailable data references, and a drawing with xrefs; confirm no crash and the documented scope.
- Switch between two drawings, close the active drawing, and refresh; confirm stale data clears and the new drawing is inspected.
- Close/reopen and dock/undock the palette; verify repeated commands reuse it.
- Record DBMOD before inspection in an otherwise idle drawing; verify inspection and refresh do not change it. Close without saving and confirm no changes attributable to the plugin.

These in-host checks have not been run in the cloud environment. See `VALIDATION.md` for the actual build results.

## CI

The GitHub Actions workflow always checks project configuration on a Windows hosted runner. That check is **not a plugin compilation**. A manual dispatch can additionally request an actual build on an organization-managed runner labelled `self-hosted`, `Windows`, `X64`, `civil3d-2026`, with licensed 2026 references in the default paths (or environment variables `AutoCAD2026Dir` and `Civil3D2026Dir`). It runs only for manual dispatch; do not expose a licensed runner to untrusted pull requests. No proprietary references or build artifacts are uploaded.

## Direction

Future milestones may introduce custom Markdown agents and landscape grading automation inspired by SketchPro. Neither agent execution nor write operations are part of 0.1.
