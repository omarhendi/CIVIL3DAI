# Civil AI 0.2 validation — 2026-11-08

## Baseline and scope

Started `civilai-v0.2` from the working `civilai-v0.1` commit `c9877be`. Stable 0.1 is preserved. The user previously confirmed 0.1 loaded in Civil 3D 2026 and detected a native TIN surface. That report is not evidence of 0.2 runtime compatibility.

The workspace adds no grading, drawing writes, automatic model tools or executable Markdown. Existing command-context scheduling, active-document checks, ForRead transactions, reference guards and surface/feature-line detection are reused. The only tool is a user-invoked read-only `drawing.inspect` returning detached structured data.

## Executed in Codex Cloud (Linux)

- Full Release build with SDK **8.0.427** and pinned official **AutoCAD.NET 25.1.0 / Civil3D.NET 13.8.280**: **succeeded, 0 errors, 2 warnings**.
- Both warnings are existing `MSB3277` reference conflicts, not suppressed:
  - `Microsoft.VisualBasic` 10.0.0.0 vs 10.1.0.0; MSBuild selected primary 10.0.0.0.
  - `System.Drawing` 4.0.0.0 vs 8.0.0.0; MSBuild selected primary 4.0.0.0.
- Release output DLL check: **CivilAI.dll only**. PDB/deps metadata remain local; the workflow uploads only the plugin DLL. No Autodesk binaries are bundled.
- `dotnet run --project tests/CivilAI.WorkspaceSmoke/CivilAI.WorkspaceSmoke.csproj -c Release`: **38 checks passed**. Coverage: Markdown create/edit/import-export round trips, size/path validation, settings/history persistence, registered read-only tools and denied write/script names, snapshot opt-in, four provider request formats and fake HTTP response/auth handling, no transmission during request preparation, endpoint validation, missing keys, HTTP-error-body redaction and cancellation. All HTTP responses are in-memory fakes; no external AI services were contacted.
- `dotnet run --project tests/CivilAI.PropertySmoke/CivilAI.PropertySmoke.csproj -c Release`: **7 checks passed** for reference-property guards, valid property preservation and statistics-failure isolation.
- `dotnet build tests/CivilAI.PanelSmoke/CivilAI.PanelSmoke.csproj -c Release`: **succeeded, 0 errors, 0 warnings**. This compiles actual WPF workspace, theme, editor, inspector and smoke sources; it does not execute them.
- Attempted `dotnet run --project tests/CivilAI.PanelSmoke/CivilAI.PanelSmoke.csproj -c Release --no-build`: **could not start** because `Microsoft.WindowsDesktop.App 8.0.0` has no installed runtime on Linux. This is not a passed UI test or a Civil 3D test.
- `git diff --check`: passed.

## Windows CI checks configured

On `windows-latest`, the workflow builds the plugin, executes both cross-platform smoke suites and the Windows WPF suite, rejects extra output DLLs, and uploads `CivilAI-0.2-<commit>` with **CivilAI.dll only**.

The WPF suite exercises actual workspace construction/theme loading, five tabs, default agent selection, create/edit/save actions, local tool invocation without provider sending, Offline mode, per-request approval rejection and acceptance with a fake provider, snapshot opt-in/reset, provider-key clearing, conversation reply display, UI settings persistence and layout. The existing inspector suite checks click-before-API ordering, output layout, empty results, property selection, pending state and error traces.

A configured workflow is not evidence of a completed GitHub run. Check the run result for the pushed commit before distributing the artifact. No live provider calls or project data transmission were performed during development.

## In-host checks still required

Run the complete acceptance checklist in README.md inside **Autodesk Civil 3D 2026 on Windows**. Specifically verify keyboard focus and docking, Markdown native file pickers, theme/readability at different palette sizes, settings/history restoration, queued refresh and drawing switching, TIN/feature-line inventory and unchanged DBMOD, request preview/cancel, and provider responses only with an authorized account/test payload.

The provider adapters are tested against representative fake HTTP contracts. Account/model availability, live API behavior, local-server compatibility and corporate proxy policies are not verified. Responses are non-streaming text. There is one saved conversation and no autonomous agent execution. Keys remain session-only; persisted files are plaintext, not encrypted storage.

**Do not claim 0.2 runtime compatibility until these Civil 3D tests have passed.**
