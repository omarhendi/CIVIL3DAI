# Cloud-build validation — 2026-11-05

## Executed

- Built the complete updated project in Codex Cloud (Linux) with .NET SDK 8.0.427, Windows targeting enabled, and the official NuGet source `https://api.nuget.org/v3/index.json`.
- Command: `dotnet build src/CivilAI/CivilAI.csproj -c Release --source https://api.nuget.org/v3/index.json`.
- Result: **build succeeded, 0 errors, 2 warnings**. Both warnings are MSB3277 assembly version conflicts:
  - `Microsoft.VisualBasic`: 10.0.0.0 vs 10.1.0.0; MSBuild selected primary 10.0.0.0.
  - `System.Drawing`: 4.0.0.0 vs 8.0.0.0; MSBuild selected primary 4.0.0.0.
- Warnings are not suppressed. They arise during reference resolution between framework references and Autodesk API dependencies; successful compilation does not establish their runtime impact.
- Restored versions: AutoCAD.NET, AutoCAD.NET.Core, AutoCAD.NET.Model all 25.1.0; Civil3D.NET 13.8.280. No third-party Autodesk DLL packages or stubs were used.
- CS0104 resolved with the explicit AutoCAD `DBObject` alias. CS0012 resolved by the explicit `AecBaseMgd` reference from Civil3D.NET.
- Release output contains `CivilAI.dll`, `CivilAI.pdb`, and `CivilAI.deps.json`. No Autodesk DLLs or other dependency DLLs are copied. The workflow artifact includes **only CivilAI.dll**.
- Refresh fix: replaced command-text queuing from the modeless WPF button with `ExecuteInCommandContextAsync`; rechecks the requested active document, locks it, and uses an open/close transaction with `ForRead` opens. Civil context comes from `CivilDocument.GetCivilDocument(document.Database)`.
- Panel updates dispatch to the owning WPF thread. Empty results show drawing summary rows. Scheduling, inspection, object, block and property errors appear visibly. Object/property diagnostics are capped at 100 messages plus a truncation notice.
- Built the corrected plugin: **0 errors, the same 2 MSB3277 warnings** described above. Confirmed the output still contains only the plugin DLL, PDB and deps.json, with no Autodesk DLLs.
- Added Windows WPF smoke checks covering empty results, inventory selection/property updates, scan diagnostics, pending state, callback exceptions, and clearing stale properties. The smoke executable compiles on Linux; execution requires the Windows Desktop runtime and is registered in the Windows CI job. It was not executed locally.
- No AI integration or drawing write operations were added.

## Still required

- The new GitHub-hosted Windows workflow must complete after push; the local Linux build is not evidence of a completed GitHub run. Consult Actions for its actual result and downloadable artifact.
- Load the DLL in Civil 3D 2026 on Windows and perform the README acceptance checks: docking, native surfaces/feature lines, properties, drawing switching, refresh, and DBMOD preservation.

The user confirmed the previous build loaded in Civil 3D 2026 and opened the palette, but reported that Refresh showed no visible results. This Linux investigation identified an unacknowledged command-text queue and hidden diagnostics, but cannot reproduce the in-host failure or establish its exact cause. **The corrected refresh path still requires a Civil 3D 2026 retest.** No Civil 3D process is available here.
