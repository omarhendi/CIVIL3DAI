# Civil AI 0.3 milestone 1

Based on v0.2 commit e768bee. Stable branch civilai-v0.2 remains the fallback.

TIN surface results appear in Drawing Inspector using Inspect drawing locally or CIVILAIREFRESH. Uses Autodesk GetGeneralProperties and GetTerrainProperties, with independent getter failure handling and reference guards. Reports elevation minimum/maximum/mean, plan/spatial area and raw API minimum/maximum/mean grade or slope. No assumed percent conversion or independently calculated triangle weighting. Numeric checks diagnose nonfinite values, negative area/slope, inconsistent ranges and zero plan area. These are data diagnostics, not grading compliance decisions. Failed groups do not stop remaining groups/surfaces. No rebuild/synchronize/write transaction is used. Statistics reflect current stored surface state; refresh after edits.

Provider sends are blocked in this milestone even if a provider or snapshot is selected. No company drawings or snapshots are required for CI. Settings/history use a separate LocalAppData/CivilAI/0.3 directory. Existing provider configuration UI is retained but sending is disabled.

## Deployment, one step at a time
1. Download CivilAI-0.3-<commit> from a successful Build Civil AI GitHub Actions run on civilai-v0.3. Keep the old 0.2 DLL in a separate folder.
2. Close Civil 3D completely. Extract the new DLL to a dedicated trusted folder; retain secure loading. Do not load both versions in one process.
3. Start Civil 3D 2026, NETLOAD the new DLL, then CIVILAI. Confirm version 0.3.
4. First test a disposable local drawing containing a known flat TIN and a planar sloped TIN. Inspect locally and compare elevations, areas and raw slope statistics with Civil 3D's surface statistics. Confirm a flat surface has zero slope. Confirm slope-unit interpretation with a known 10% plane before interpreting raw values as percentages.
5. Check empty surfaces and an empty drawing. Unavailable values must be explicit rather than zero. Test stale/broken data references locally; no synchronization should occur.
6. Record DBMOD before/after inspection in an otherwise idle drawing: it must not change. Test switching/closing drawings while refresh is queued. Try Review & send with a configured provider: it must report the offline block with no request.
7. Only after these pass, inspect a company drawing locally. Do not upload it, screenshots, or snapshots. Runtime acceptance requires Civil 3D; cloud compilation cannot establish it.

Rollback: close Civil 3D, restart, and NETLOAD the retained 0.2 DLL. Its branch and settings remain unchanged.

Official API reference: https://help.autodesk.com/cloudhelp/2026/ENU/Civil3D-API/files/html/13d8bd3a-56c1-85f9-03dd-8750b123dd0a.htm
