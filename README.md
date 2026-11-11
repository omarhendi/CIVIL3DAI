# Civil AI 0.3

Civil AI for Civil 3D 2026 can ask a configured language model to interpret a request and propose one of its supported drawing tasks.

1. In **Providers**, choose OpenAI, Anthropic, Google, Groq, or a compatible local endpoint. Enter the provider's model ID and any required session API key.
2. In **Chat**, write your request and click **Send**. Civil AI responds or starts the matching drawing workflow. Follow any Civil 3D prompts for inputs and confirmation before creating objects. Drawing reports appear in Chat.

Groq defaults to `openai/gpt-oss-20b` and uses a Groq session API key. The base URL is built in.

Try “Carry the road levels onto a line just beyond the curb.” The offset workflow asks for the reference edge, elevation source, distance and side. The model receives your current message and up to four recent exchanges from this session. Drawing snapshots and agent files stay local. In Offline mode, **Send** also recognizes a small set of drawing task phrases.

For prior milestones see [surface inspection and deployment](MILESTONE-0.3.md). The documentation below describes the preserved v0.2 baseline.

# Civil AI 0.2 — Civil 3D agent workspace

Native x64 WPF/.NET 8 plugin for Autodesk Civil 3D 2026. Branch `civilai-v0.2` develops the workspace; stable `civilai-v0.1` is preserved. Grading and drawing modifications are **not implemented**.

## Get the DLL without installing development software

Open [GitHub Actions](https://github.com/omarhendi/CIVIL3DAI/actions), select a successful **Build Civil AI** run for `civilai-v0.2`, and download **CivilAI-0.2-&lt;commit&gt;**. The artifact contains only `CivilAI.dll`. Autodesk DLLs are not copied or uploaded.

The workflow builds on `windows-latest`, runs cross-platform workspace/property checks and Windows WPF smoke checks, then uploads the plugin. It does not launch Civil 3D.

To compile on a Windows or Linux development/cloud machine with .NET 8:

```powershell
dotnet restore src/CivilAI/CivilAI.csproj --source https://api.nuget.org/v3/index.json
dotnet build src/CivilAI/CivilAI.csproj -c Release --no-restore
```

References remain official Autodesk-owned NuGet packages, pinned exactly to `AutoCAD.NET` 25.1.0 and `Civil3D.NET` 13.8.280 (2026). The project targets `net8.0-windows`, WPF and x64 with Windows cross-targeting enabled. AutoCAD assets are compile-only; explicit `AeccDbMgd` and `AecBaseMgd` references are not copied locally. No Autodesk installation, API keys or licensed self-hosted build runner is needed to compile. Runtime testing does require Civil 3D 2026 on Windows.

## Workspace

Run `CIVILAI` after NETLOAD. The dockable palette contains:

- **Chat**: persistent conversation, agent selector, natural-language composer, resizable history/composer split, local inspection, per-request snapshot opt-in, request review, cancellation and a new-conversation action.
- **Agents**: create, edit, save, import and export Markdown. Use `# Agent name` as a heading. Select an agent in Chat, then **Edit selected**. Imports receive a new local ID. Files are limited to 1 MiB. Markdown/code fences remain inert text, never scripts, XAML or executable tools.
- **Providers**: Offline, OpenAI, Anthropic, Google and local OpenAI-compatible configuration; model identifier; local base endpoint; session-only API key; readable text size. Saving settings does not test a connection or transmit data.
- **Drawing Inspector**: the working native TIN surface and feature-line inventory with name, handle, type, area/elevation information, reference guards, property-level failure isolation, empty-drawing output and stage diagnostics. `CIVILAIREFRESH` remains available.
- **Diagnostics**: bounded session log of workspace actions and safe errors. Provider bodies and keys are never logged.

The embedded dark-theme dictionary explicitly colors labels, informational TextBlocks, inputs, dropdowns, selections and disabled controls; theme regression checks enforce at least 4.5:1 text contrast for its palette. The dark workspace uses a draggable chat splitter and a resizable dockable palette. Font size, selected tab, selected agent, provider/model/base endpoint and chat split persist. AutoCAD manages palette docking/size using a separate 0.2 palette GUID.

## Privacy and storage

**No request is sent on startup, agent selection, configuration changes or local inspection.** Offline is the initial default. Every **Review & send** opens a confirmation dialog showing the exact destination and JSON body (agent instructions, user prompt and conversation). Approval is for that request only. Canceling sends nothing.

A drawing snapshot is excluded by default. To include it, first **Inspect drawing locally**, explicitly check **Include the locally inspected drawing snapshot**, and approve the displayed JSON. The snapshot contains drawing name/path, scalar properties and diagnostics, so review it carefully. Drawing switches invalidate the snapshot and clear the opt-in. Snapshots are not live: inspect again after edits. Earlier assistant/user messages may themselves contain project details; the full outbound conversation is always in the preview.

Keys stay in the PasswordBox/session memory, are cleared when changing providers, and are never written to settings or logs. Enter a key each Civil 3D session. No hardcoded keys, credential discovery, telemetry or automatic retries exist. Cloud providers require your own account/model access. Local endpoints use the OpenAI chat-completions format; configure a base such as `http://localhost:11434/v1/`. Only loopback HTTP or HTTPS is permitted; embedded credentials/query/fragment URLs are rejected. Redirects are disabled. Requests have a 120-second total timeout and a 4 MiB response limit. Cancellation cannot retract data already received by a service.

Local history, non-secret settings and agent files are stored in **plain text** under `%LOCALAPPDATA%\CivilAI\0.2`. This is not encrypted storage. **New conversation** clears saved conversation history; agent files and settings remain. Imported/exported files are chosen through Windows dialogs. No conversation data is saved inside the drawing.

## Architecture and tool boundary

- `Workspace/WorkspaceControl.cs`: WPF workspace, agent editor, settings, explicit request approval and local tool actions.
- `Workspace/WorkspaceState.cs`: local Markdown/settings/history persistence and allowlisted read-only tool registry.
- `Workspace/Providers.cs`: `IAiProvider`, provider-specific request/response adapters and conversation composition.
- `Plugin.cs`: AutoCAD command-context scheduling, active-document recheck and document locking.
- `Inspection.cs` / `PropertyInspection.cs`: ForRead transactions and detached snapshots with guarded Civil getters.

The only registered tool is **`drawing.inspect`**, invoked explicitly by the user. Its structured snapshot can be included in an approved AI request. Unknown tool names, scripts and write commands are rejected. Model replies are displayed as text; **there is no autonomous tool loop, code execution or drawing modification path**. Agent Markdown cannot register tools. Any future write tool must add a separate explicit user-approval boundary; request approval in 0.2 does not authorize drawing changes.

The inspector scans Civil surface IDs and local block records, deduplicates native TIN surfaces and feature lines, excludes xref block contents, and never expands block insert instances. Reference-only properties (`IsReferencedSourceExisting`, `IsReferenceValid`, `IsReferenceStale`, `IsReferenceSubObject`, `IsPartialReferenceObject`) are skipped unless `IsReferenceObject` is confirmed true. Area/elevation statistics may be unavailable on empty surfaces. No rebuild, save, geometry creation or grading APIs are invoked.

## Exact Civil 3D 2026 acceptance steps

1. Download the successful 0.2 artifact, extract it, and **restart Civil 3D** to avoid an old DLL remaining loaded. Add its folder to trusted locations while keeping secure loading enabled. NETLOAD `CivilAI.dll`, then run `CIVILAI`. Confirm **Civil AI 0.2** and the five tabs. Do not load 0.1 and 0.2 simultaneously in the same process (they share command names).
2. Resize/dock/float the palette, drag the chat splitter, change text size in Providers, select a tab/agent, close and reopen Civil 3D. Confirm settings and history persist. Confirm no requests occur during these actions.
3. In Agents, **Create**, enter `# Test reviewer` and instructions, **Save**, then select it in Chat. **Edit selected**, change the text and save. Export `.md`, import it, and confirm its contents/name in the editor and selector. Put a code fence in an agent and verify it is shown/sent only as text; nothing executes.
4. With Offline selected, enter a prompt and click **Review & send**: it must report offline without transmitting. Click **Inspect drawing locally** in an empty drawing: verify a local history entry, zero counts and drawing-summary rows in Drawing Inspector.
5. Inspect a drawing with known native TIN surfaces and site/siteless feature lines. Compare counts/names/properties against Civil 3D. Test a data-reference TIN and an empty TIN; confirm reference guards and statistics/error rows. Record DBMOD before/after inspection in an otherwise idle drawing; it must remain unchanged.
6. Start another AutoCAD command, then refresh from the palette. Confirm waiting/command-context stages. Finish/cancel that command. Switch/close drawings while a refresh is queued; confirm stale snapshots clear and another refresh is requested rather than inspecting a closed document.
7. If your organization authorizes a live test, configure an approved provider/model and enter a **session key**. Compose a harmless message. Click Review & send, inspect the JSON/destination, then **Cancel**: no request should be sent and no user turn added. Repeat and explicitly approve. Confirm the reply appears and the conversation persists. Test each provider separately; a valid model and account are required. Changing providers must clear the key.
8. After local inspection, leave snapshot inclusion unchecked and verify the preview contains no automatically attached drawing snapshot. Then opt in and confirm the preview shows the exact snapshot. Approve only if authorized to disclose that data. Switch drawings and verify the checkbox resets.
9. Test cancellation, invalid model/key, an unavailable endpoint and Offline mode. Verify actionable diagnostics without keys or response bodies. Provider errors must not change the drawing. Verify no generated response can execute a tool or code.
10. Restart without supplying a key: history/settings should restore, keys should be blank, and nothing should transmit automatically. Use New conversation to clear local history when finished.

## Validation and limitations

See `VALIDATION.md` for actual results. Cloud compilation does not establish runtime compatibility. Live provider calls, Windows WPF execution and Civil 3D 0.2 acceptance are distinct checks; do not treat a Linux build as proof of them.

0.2 uses non-streaming text responses and a single saved conversation, without automatic history summarization or autonomous model tool calls. Provider requests are capped at 1 MiB; begin a new conversation if it grows too large. Markdown is edited and sent as plain text, not rendered as HTML. Native file pickers and docking must be verified in Civil 3D. No grading modifications are included.
