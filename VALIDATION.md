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
- WPF interface, commands, read-only transactions, and inspection behavior were preserved. No additional features were added.

## Still required

- The new GitHub-hosted Windows workflow must complete after push; the local Linux build is not evidence of a completed GitHub run. Consult Actions for its actual result and downloadable artifact.
- Load the DLL in Civil 3D 2026 on Windows and perform the README acceptance checks: docking, native surfaces/feature lines, properties, drawing switching, refresh, and DBMOD preservation.

**Runtime compatibility has not been tested or established.** No Civil 3D process is available in this Linux environment.
