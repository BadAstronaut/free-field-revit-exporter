# Launch-readiness test: Snowdon Towers Sample Architectural (Revit 2025.4)

Model: copy of `Snowdon Towers Sample Architectural.rvt` (98 MB, 55 sheets, 18 levels, 40 schedules, 6 links) in `%LOCALAPPDATA%/FreeFieldExporter/work/`; the sample was never touched. No fixtures.

## Bundles
| What | Path |
|---|---|
| Default headless export (the launch bundle) | `C:\Users\rzlaz\AppData\Local\FreeFieldExporter\out\snowdon-20261121-063659\Snowdon.ffbundle` (39.6 MB; IFC 82 MB raw, PDFs 113 MB raw) |
| Same selection exported through the real dialog (UI Automation) | `C:\Users\rzlaz\AppData\Local\FreeFieldExporter\out\ui-20261121-064311\bundle\Snowdon.ffbundle` (identical counts) |

Both: validator **PASS** (0 FAIL). Headless: 201 PASS, 1 WARN (A106 "Green Roof Materials Legend" viewport has no indexed elements). Reports: `docs/reports/launch-snowdon-validator.md`, `docs/reports/launch-snowdon-ui-validator.md`.

## What is exported (default selection)
30 pages: **28 sheets** with a mapped floor plan + **2 unplaced floor-plan views** (`VIEW-Block_37_-_Parapet`, `VIEW-Green_Roof_Hardscape`: levels no sheet covers), 51 viewports, 5859 element records (26 717 index entries), 968 links, 40 schedules.
Only `FloorPlan` and `EngineeringPlan` viewports are mapped. Unplaced floor-plan views are checked by default only when no default-selected sheet covers their level (the other 36 working views such as "L1 Wall Base/Top", "L1 Export to Civil", "L1 - Scope Box" stay listed, unchecked, "level already on a sheet"). Compared with the first all-views run: 66 pages / 61 MB -> 30 pages / 39.6 MB.

### Excluded sheets (27 of 55), why
| Reason | Sheets |
|---|---|
| ceiling plans only (not mapped; `mapCeilingAndAreaPlans` off) | A108 A109 A110 A111 A112 |
| elevations only | A200 A201 A202 |
| sections only | A301 A302 A303 A304 A305 A306 A307 A405 A406 |
| details / drafting / legends / schedules-only / cover / renderings | A501 A502 A601 A602 G000 G001 |
| 3D views only | A901 A902 A903 A904 |

### Per-level coverage (18 IfcBuildingStorey = 18 Revit levels, names equal)
13 levels have a mapped plan: Parking (A100 G100 SD100), L1 - Block 35 (A101 G101 K101 SD101), L1 - Block 37 (A401 A404), L1 - Block 43 (C101), M1 (K101), L2 (A102 G102 SD102), L3 (A103 G103 SD103), L4 (A104 A402 A403 G104 SD104), L5 (A105 G105 SD105), R2 (A106), R3 (A107 G106 SD106), Block 37 - Parapet (VIEW), Green Roof Hardscape (VIEW).
No floor-plan view exists in the model for: Block 43 - Parapet, R1, Block 39 - Parapet, Parapet, Parapet 2.
Primary viewport per (sheet, level) = largest model area: A402/A403 (enlarged L4 cores) and A106 (R2 plan vs. "Green Roof Materials Legend", itself a FloorPlan-type view) keep the full plan as primary where one exists.

## Timings (headless default run, IFC served from the dev cache)
collect 34.6 s, IFC 0.1 s (cached; a cold IFC export takes ~159 s), IFC scan 0.4 s, temp sheets 3.3 s, PDF 78.9 s (30 pages), geometry/elements/outlines/calibration 68.3 s, schedules 7.7 s, bundle 6.7 s: **215 s total**, so ~6 min cold. Through the dialog, 174 s with a cold IFC after the first run warmed Revit. (The first all-sheets run took 9.5 min / 66 pages; an early version with a slow PDF parser took 16 min and was replaced.)

## Alignment checks
* Validator geometric check (IFC world vs. `pdfToModel`): 34/35 viewports PASS (the 35th has no elements); medians ~1e-11 m, worst element 0.32 m; det ratio 1.000.
* Independent PDF check (`tools/scripts/verify-bundle-pdf.mjs`, element outline edges sampled against the line segments drawn in each PDF): overall 77.5 % over 30 pages, 11 pages below 85 %: G106 62, A107 63, C101 64, VIEW-Green_Roof_Hardscape 65, A106 65, SD106 75, K101 77, A401 78, A403 79, A402 80, A102 84. Investigated with SVG overlays (outlines over PDF linework): geometry is aligned; the shortfall is categories whose plan graphic is not their 3D footprint: luminaires 42-49 %, plumbing fixtures/sweeps 7-50 %, walls ~64 % (joined wall faces are not drawn), furniture symbols. Columns, floors, doors, parking, curtain-wall mullions score 73-100 %.
* A real defect was found and fixed by this check: on one titleblock-less temporary page (`VIEW-Rooms_-_L1_Block_35`, in the first all-views run) Revit printed the sheet 101 pt off the position the API implies (page taller, content at the top). `PageCalibrator` now compares element outlines with the PDF linework and translates the page when (and only when) that fixes a low match (29 % -> 85 % there); unit-tested (`PageShiftTests`).

## Drawn-in-view filter
An element is indexed in a viewport only if it is drawn there: not hidden in the view or by category/filter, inside the plan view range (view depth .. top clip), and with non-empty view-specific geometry. Default run: 46 viewports; dropped 471 (out of view range) + 3 176 (empty view geometry) + 0 hidden. Element boxes come from the solid geometry (same extent as the IFC geometry; this removed the Parking Space mismatch the validator flagged: parking-space Revit boxes were 1.35 m off the IFC geometry). Hidden-by-filter drops were 0 on this model (the view filters in Snowdon do not hide indexed elements).

## Door Schedule
`Door Schedule` is placed on sheet **A601**, which is excluded (legends only), so it is exported as bundle-level data (`data/schedules/door_schedule.json`): **132 rows, 132 bound to door elements** (gid + uid), grouped header rows dropped. Binding: cells equal the element's displayed values; fields Revit cannot compute through the API (e.g. "From Room: Number") are ignored when matching. `WV_Door Schedule`: 25/39 rows bound. All 40 schedules exported; 2 788 of 3 055 rows bound (unbound ones are analytic/area schedules without per-element rows). Schedules placed on exported sheets are embedded in those PDFs (A100 Parking Schedule, A101 Planting Schedule, A106 Planting Schedule).

## Links
968 links, 70 navigable (target sheet exported) and 898 with `targetIncluded:false` (target sheet exists but was not exported; number kept). Kinds: view 633 (camera/3D markers to A901...), section 140, elevation 158, callout 34, detail 3. Spot checks: A100/A101/A102/A103/A104 -> A901 (excluded 3D sheet, non-navigable).

## Edge cases
* Multiple plan viewports per sheet: A106 (R2 + legend-like FloorPlan), A402/A403 (enlarged L4 cores), K101 (L1 + M1); each viewport gets its own `pdfToModel`; primary per (sheet, level) = largest model area.
* Dependent views / enlarged partial plans: ordinary viewports; they can never steal primary (model-area rule, tested).
* Rotated views: handled by the chain (proved on a rotated+cropped fixture earlier, 0.07 pt).
* Views larger than the titleblock: placed sheets are printed by Revit as authored (clipped by the titleblock only if the author did so); unplaced views use a titleblock-less temporary sheet sized to the view, so no clipping.
* Legends, drafting views, schedules on sheets: no elements indexed; legend viewports without a projection transform (the 7 "Life Safety Legend" viewports, G100-G106) are skipped with a warning (the only 7 warnings in the run).
* Ceiling / area plans: not mapped by default (`pdfToModel` null, no storey); option `mapCeilingAndAreaPlans`.
* Linked models (6 Snowdon discipline links, unresolved in this copy): not exported (the IFC contains only this document); the "unresolved references" dialog is auto-dismissed and logged.

## UI (real dialog, UI Automation)
Revit ribbon items do not expose Invoke and mouse clicks are unreliable across the multi-monitor DPI setup, so the dialog is opened through the dev job channel (`step=showdialog`: same `ExportDialog` class, same `RunExport` as the Export button); the ribbon command itself runs the older staged DLL until Revit is restarted with the new build. Scenario (`tools/scripts/ui-scenario.ps1`): open, screenshot, expand "Other sheets (not mapped)", tick A108 (summary 28 -> 29 sheets, est. 34.4 -> 35.1 MB), untick (back to 28), Levels panel, Export, result panel after 174 s: "30 pages (2 from views), 51 viewports, 5859 elements, 968 links, 40 schedules, 7 warnings".
Screenshots (`C:\Users\rzlaz\AppData\Local\FreeFieldExporter\out\ui-20261121-064311\screens\`, copies in the session scratchpad `launch-screens`): `01-dialog-default.png`, `02-other-sheets-expanded.png`, `03-non-mappable-ticked.png`, `04-levels-panel.png`, `05-result-panel.png`.

## Known limitations
* "Green Roof Materials Legend" is a FloorPlan-type view in the model, so it appears in A106's plan column and is mapped (it has no elements).
* The size estimate in the dialog (34.4 MB) undershoots the real 39.6 MB.
* Link noise: 633 "view" links (camera markers) mostly to excluded 3D sheets.
* The ribbon button was not clicked physically (see UI); the add-in is still unsigned (Revit prompts on every new build).
* Edge-cover check is a heuristic (symbolic plan graphics differ from 3D footprints); the validator geometric check is the authority for coordinates.
* The dialog remembers the last selection per document in `%LOCALAPPDATA%/FreeFieldExporter/prefs.json`; a stale remembered selection overrides new defaults (delete the file to reset).
