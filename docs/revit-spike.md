# Revit spike findings (Revit 2025.4, STRUCTURAL_CO.rvt work copy)

Environment: Revit 2025 (.NET 8), add-in built with `-p:RevitVersion=2025` (2026 = net8.0-windows, 2027 = net10.0-windows).
Test model: copy of `STRUCTURAL_CO.rvt` in `%LOCALAPPDATA%/FreeFieldExporter/work/` (original untouched). Opened without upgrade prompt
(only an "unresolved references" TaskDialog for the unloaded MECHANICAL.rvt link, auto-dismissed by `DialogGuard`, logged).
The model has ONE sheet (S-101: a section + a 3D view) and 6 level-bound plan views, none placed on sheets, no schedules.

## Sheet -> PDF page mapping (proven)
`PDFExportOptions { Combine=false, ZoomType=Zoom, ZoomPercentage=100, PaperFormat=Default, PaperPlacement=Center, vector }`,
`Document.Export(dir, [sheetId], opts)` outside any transaction (a TransactionGroup may be open). The output file name ignores
`FileName` (Revit names it "Sheet-<number> <name>.pdf"); export one sheet per call into an empty temp dir and take the single file.

* Page size = sheet size (A3 titleblock: 1190.52 x 841.8 pt; `ViewSheet.Outline` = titleblock bbox = 1.378 x 0.974 ft).
* **page_x = W/2 + (sx - cx) * 864 ; page_y = H/2 - (sy - cy) * 864**, with (cx,cy) the centre of `ViewSheet.Outline`,
  864 = 72 pt * 12 in per ft, W/H the PDF page size (origin top-left, y down). Equivalent to outline-min -> bottom-left for titleblock sheets.
* Evidence (`tools/scripts/check-page-map.mjs`, pdf.js vertices): grid endpoints pushed through
  `View.GetModelToProjectionTransforms()[0]` -> `Viewport.GetProjectionToSheetTransform()` -> formula land on PDF path vertices within
  0.05-0.13 pt (all 10 in-page endpoints, plan 1:96). A viewport rotated 90 deg clockwise with an active crop (sheet FF-201) matches the same way
  (median 0.07 pt): `GetProjectionToSheetTransform` already contains the rotation, model->projection contains view orientation, scale (1/96) and crop translation.
* A sheet WITHOUT titleblock is sized to its content: the PDF page is larger than `Outline` but still centred on it (symmetric margin), so the same formula holds.
* Independent whole-bundle check (`tools/scripts/verify-bundle-pdf.mjs`): bbox corners of indexed elements coincide (<=1.5 pt) with PDF vector vertices for 48-100 % of corners per page
  (rest are elements whose plan symbol is not their bbox: beams, tags, halftoned links).

## Views not on sheets
Direct view export (`Export(dir,[viewId])`) works but the page is sized to the drawn ink bbox (not derivable from the API) -> grid voting (`tools/scripts/calib-view.mjs`) is needed.
Decision: put such a plan view on a TEMPORARY sheet (no titleblock => sheet sized to the viewport) inside a `TransactionGroup` that is rolled back after the PDFs and element data are collected; then the sheet mapping above applies exactly.
The fixture sheets (below) are the only persistent changes and only exist in the work copy; nothing is saved by the exporter.

## IFC
`IFCExportOptions`: `FileVersion=IFC4RV` (or `IFC2x3CV2`), `ExportBaseQuantities=true`, `WallAndColumnSplitting=false`, `FilterViewId=InvalidElementId`,
`AddOption("SitePlacement","Internal")`, `AddOption("StoreIFCGUID","true")`, `AddOption("ExportInternalRevitPropertySets","false")`.
`Document.Export(folder,name,IFCExportOptions)` must run inside a `Transaction` (we `RollBack` it). Exporter: Autodesk Revit 25.4.10.45 IFC 25.4.10.45, schema `IFC4`.
* The file's length unit is IfcConversionBasedUnit FOOT (0.3048 m): raw IFC numbers are feet; web-ifc applies the unit, so world coords are metres.
* Verified (`tools/scripts/ifc-bbox.mjs`, web-ifc COORDINATE_TO_ORIGIN:false): column/footing/floor bbox == Revit internal bbox * 0.3048 to 1e-4 m (beam within 0.02 m, join trims). Not shifted. (Elements are placed with baked geometry; placement origin is not the position.)
* Storeys: `IfcBuildingStorey.Name` == `Level.Name` for all 5 levels; elevations in file feet == `Level.Elevation`.
* GlobalId: `ExporterIFCUtils.CreateAlternateGUID(element)` equals the stored `IfcGUID` parameter and the IFC GlobalId (all sampled elements found in the IFC). After rollback the parameter is gone, so we use the alternate GUID (or a user-set IfcGUID param) and keep only elements whose GUID occurs in the IFC text.
* `RevitAPIIFC.dll` must be referenced for `ExporterIFCUtils`.

## Automation gotchas
* Revit only pumps `ExternalEvent`/`Idling` when its message loop wakes. The job poller therefore posts a WM_MOUSEMOVE to the main window and a Dispatcher callback from a timer thread; jobs are picked up in ~2 s even when Revit is unfocused.
* `FREEFIELD_EXPORT_JOB` jobs run on first Idling; `OpenAndActivateDocument(ModelPath)` is used (active UIDocument).
* Dev loop without restarting Revit: `tools/scripts/dev-build.ps1` + `dev-job.ps1` (worker is loaded from a fresh AssemblyLoadContext per job; see `App.cs`).
* Revit shows "Security - Unsigned add-in" on the first load of every new FreeField.Exporter.Revit.dll (it is keyed on the dll). Needs `tools/signing/sign-addins.ps1 -Setup` once (user action) or "Always load" once; a headless launch blocks on it.
* Revit 2025 UI is Spanish here; category names are localized (`Category.Name`), `bic` carries the invariant BuiltInCategory.

## Fixtures (reproducible)
Job option `createFixtureSheets=true fixtureCount=2`: for the unplaced level-bound plan views ordered by name, creates sheets FF-101 ("101 FIRST FLOOR") and FF-102 ("201 SECOND FLOOR") with the first titleblock type (A3 metric), view centred. The other 4 plan views
(301 THIRD FLOOR, 401 FOURTH FLOOR, CO-101 FIRST FLOOR, T.O. FOOTING) become `VIEW-*` pages. Plus the original sheet S-101 (section + 3D). `Spike/SpikeRunner` step `rotvp` creates the rotated+cropped fixture FF-201 used for the rotation proof (spike copy only).
