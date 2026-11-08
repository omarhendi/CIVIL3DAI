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

## 0.3.1 summary update

Selecting a TIN opens Surface Summary by default; All Properties retains the raw inspector. Slopes are displayed as rise/run × 100 percent, with five decimal places to preserve small slopes. Elevations and areas explicitly retain drawing units (and drawing units squared); no unit conversion or assumption of metres is made. Unavailable statistics remain unavailable and empty/inaccessible surface errors use a plain-language explanation.

### User-observed Civil 3D 2026 acceptance, 2026-11-08

On the original 0.3 build: one populated TIN's elevation extremes, plan/spatial area and slope statistics matched Civil 3D at displayed precision. DBMOD remained 29 across refresh. Switching to a blank drawing cleared prior results and reported zero surfaces/feature lines. Review & send displayed the offline block. An empty TIN returned unavailable elevation/terrain statistics without a crash. These observations do not establish network-level monitoring or universal drawing compatibility. Broken/stale references and switching while a refresh is queued remain untested.

The summary update needs a short in-host visual check after deployment. Keep the fallback DLL. Download the latest successful artifact on civilai-v0.3, close Civil 3D, then load the replacement DLL in a fresh session.

## 0.3.2 configurable slope checks

Enter minimum and maximum percentages in the inspector's Slope limits fields. Both start blank; no engineering limits are assumed. Use decimal points. Limits apply to the selected surface's whole-surface minimum and maximum, inclusively, and update immediately against the current detached snapshot. They remain in memory for the session and apply as you select other surfaces; restart clears them. Clear both fields to disable checks. Invalid inputs and unavailable/inconsistent statistics never produce a within-limits result. The check neither locates triangles nor classifies pavement/landscape areas or establishes compliance. Refresh after drawing changes.

Host acceptance: select a known TIN, enter a range enclosing its extrema and confirm Within configured limits. Lower the maximum below its observed maximum and confirm Outside limits. Reverse the limits and confirm Invalid limits. An empty TIN must say Unavailable with valid limits. Verify DBMOD is unchanged when editing limits. These new in-host checks remain pending; earlier acceptance observations above apply to earlier builds.

## 0.3.3 triangle locations

Run CIVILAIFINDSLOPES, select one TIN in the drawing, then enter minimum and maximum percentages. Results replace the inspector inventory with one Triangle locations report under All Properties. This is an explicit scan, separate from the session limit fields. Refresh restores the usual inventory. Visible triangles only (GetTriangles(false)); hole-boundary invisible triangles are excluded. Each finding has the scan ordinal, planar slope percent and centroid in WCS drawing units. Reports retain the first 100 findings, not the steepest 100. No markers or view changes are made. Centroids help locate triangles, but do not establish whether they are pavement, edges, landscape or errors.

The scan processes at most 100,000 triangles or five seconds between API calls. Native calls themselves cannot be interrupted. Partial scans and unusable triangles are reported; zero findings never proves compliance. Degenerate/near-vertical projected triangles are unusable, not zero-slope. API wrapper objects are disposed within the read transaction. Reference guards and the provider block remain in place.

Acceptance pending: on a disposable known plane, compare returned percent and WCS centroid with known geometry; verify limits, cancel each prompt, empty TIN, invalid references and unchanged DBMOD. Test EDGE02 locally with 0–100% to locate steep areas. Do not upload drawing data. User already verified the 0.3.2 within/outside/invalid/unavailable outcomes and unchanged DBMOD (1) while changing limits.

Autodesk 2026 API references:
- https://help.autodesk.com/cloudhelp/2026/ENU/Civil3D-API/files/html/7b0cc1ae-c036-49a5-5a91-38192e3bcc6d.htm
- https://help.autodesk.com/cloudhelp/2026/ENU/Civil3D-API/files/html/73616c27-259d-c2f6-dcbf-064c094bdf53.htm

## 0.3.4 temporary triangle locate

After CIVILAIFINDSLOPES, double-click a Triangle # row in All Properties. In the Model tab, the plugin revalidates the cached triangle vertices and reference status, switches temporarily to WCS top view, zooms to the triangle and outlines its three edges in red using transient graphics. Enter or Esc clears the transient edges and restores the original view. No entity is added to the database. View changes can affect DBMOD view-state flags; unchanged DBMOD is not guaranteed for this display action. Coordinates now wrap in All Properties.

Refreshing, changing/closing the active drawing, or starting another scan invalidates stored locations. Changed/hidden/deleted triangles are rejected at locate time. Paper-space use is rejected. Only one locate may run at a time. The native view and transient APIs require in-host acceptance; cloud tests do not prove graphics behavior.

Acceptance pending: scan EDGE02, double-click each finding, verify red edges and location; Enter and Esc must clear the outline and restore the previous view. Repeat with a rotated view; switch drawings or edit a surface before locating and verify stale locations are refused. Confirm no new database entities appear. Earlier triangle scan was user-tested with 532 triangles, two findings (130.92862% and 170.26293%), and unchanged DBMOD.

Transient API: https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_GraphicsInterface_TransientManager_AddTransient_Drawable_TransientDrawingMode_int_IntegerCollection.html

## 0.3.5 pavement/floor area assessment

Deployment change: keep BOTH CivilAI.dll and NetTopologySuite.dll from the artifact in the same folder; NETLOAD CivilAI.dll only, in a fresh Civil 3D session. NetTopologySuite 2.6.0 is a local BSD-3-Clause geometry library; it makes no external requests. Autodesk DLLs remain host-provided. Preserve the previous plugin folder for rollback.

CIVILAIAREA selects one TIN, a closed straight-segment 2D/3D polyline boundary, optional straight-segment 2D/3D polyline breaklines, and minimum/maximum slope percentages. Enter at breakline selection assesses the whole boundary. Results contain only whole-area and closed breakline subarea summaries, never per-triangle findings. Selected breaklines are projected to WCS XY and clipped/noded with the boundary. Interior closed loops define separate areas, not exclusion holes. Open/dangling segments do not subdivide unless they complete a closed region. There is no implicit gap snapping. Curves, feature lines and automatic extraction of surface definition breaklines are not supported in this first area version; unsupported selections are rejected. No drawing geometry is modified or generated.

Visible TIN triangles are clipped exactly in plan to each region. Covered plan area weights the mean slope and outside-limit percentages. Uncovered area is explicit. This measures steepest triangle-plane slope internally, not directional crossfall/longitudinal grade or drainage. It does not classify kerbs or prescribe standards. Missing data, overlapping coverage or scan limits cannot establish a whole-area pass. Limits: 100 breaklines, 10,000 vertices per line, 100 subareas, 100,000 triangles, 15 seconds between native calls during triangulation; native calls and boundary topology operations cannot be interrupted. Time/geometry errors discard assessment output.

Area highlighting is not included in this initial area report; the previous triangle locator remains a separate detail tool. Native Civil 3D acceptance is pending: use a disposable rectangular known plane first; test no breaklines, a boundary-to-boundary breakline, a dangling line, a closed internal loop, partial coverage and invalid boundary. Confirm totals agree with known areas and DBMOD remains unchanged. Keep company drawings local.

## 0.3.6 feature-line inputs

CIVILAIAREA now accepts FeatureLine boundaries (must be Closed) and open/closed FeatureLine subdivision breaklines. It reads PI points and segment bulges ForRead, guards reference status and checks reconstructed plan length against Civil 3D Length2D. Feature lines are not converted or edited. Elevations are ignored for the XY boundary; the selected TIN still supplies all assessment slopes.

Circular arcs are sampled with an explicit maximum chord-deviation prompt, default 0.001 DRAWING UNITS (not assumed metres). The result records that tolerance; curved areas are approximate. Limits remain 10,000 points per sampled input. Smoothed/unsupported geometry or inconsistent API segment interpretation is rejected via length comparison. Existing 2D/3D polyline support remains straight-segment only. Error reasons now appear in the panel as well as the command line.

Cloud geometry checks cover semicircular arc length, clockwise/counterclockwise direction, chord-deviation bound, closed-ring handling and invalid bulges. FeatureLine API runtime acceptance remains pending: first use a disposable closed straight feature line, then a known curved feature line; compare area against Civil 3D at successively tighter chord deviations. Test an open boundary and stale references, and confirm unchanged DBMOD. Closed-region subdivision remains boundary/breakline-based only, never triangle-level.

Official API: https://help.autodesk.com/cloudhelp/2026/ENU/Civil3D-API/files/html/350d01d2-18e4-ab26-5152-3484863e1fbd.htm and https://help.autodesk.com/cloudhelp/2026/ENU/Civil3D-API/files/html/65080753-4e61-fceb-9176-1402a1b262a7.htm
