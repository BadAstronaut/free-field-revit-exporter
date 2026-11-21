# Decisions

Append-only log. Format: `## YYYY-MM-DD — title` + context/decision/consequence.

## 2026-11-20 — IFC placement = Revit internal origin
Manifest transforms target IFC world coords. Exporting relative to internal origin keeps Revit internal coords == IFC coords (ft→m) and avoids survey/shared-coordinate ambiguity. Revisit if FreeField needs geo-referencing.

## 2026-11-20 — pdfToModel is a general affine + 2 control points
PDF y-down vs model y-up implies a reflection, which a similarity (scale+rotation) cannot express. The importer fits its own similarity from the control points after converting model XY to its world convention.

## 2026-11-20 — Primary test target: Revit 2025 + STRUCTURAL_CO.rvt
User directive. Add-in defaults to RevitVersion=2025 (net8.0-windows). Revit MCP harness gets built/installed for 2025 for verification. Plan viewport types widened to FloorPlan/EngineeringPlan/CeilingPlan/AreaPlan for the structural model.

## 2026-11-20 — PDFsharp pinned to 6.2.4 (MIT)
PDFsharp 6.5.0 is AGPL-3.0 and enforces a license key at runtime (`PdfSharpLicenseException` on `new PdfDocument()`). 6.2.4 is the last MIT release; Core pins `PDFsharp` 6.2.4. Do not bump to 6.5+ without a licensing decision.

## 2026-11-20 — BIM properties read from PDF attachment, not a DB table
FreeField reads `freefield-sheet.json` embedded in each PDF via pdf.js getAttachments at view time. Avoids a Dexie v12 migration, Firestore 1MB doc limits and sync changes; data travels with the PDF blob.

## 2026-11-20 — Model space = IFC as authored (Z-up); FreeField world = (X+bx, Z+by, -Y+bz)
Verified by ff-analyst. Importer must support rotated calibrations in the overlay (currently axis-aligned only) and fix LinkView floorZ by adding by.

## 2026-11-20 — Plan views not on sheets use a temporary no-titleblock sheet
Direct view PDF export sizes the page to the ink bbox (not derivable). The exporter puts such views on a temporary sheet (TransactionGroup, rolled back) so the proven sheet->page formula applies. Synthetic page number `VIEW-<sanitized view name>`, sheet id `view:<view UniqueId>`.

## 2026-11-20 — IFC file units
Revit writes IFC in the project length unit (FOOT for imperial projects). Coordinates remain exactly Revit internal * 0.3048 m after the unit conversion that web-ifc applies; no API change needed.

## 2026-11-21 — Export and map only floor plans (user directive)
FreeField maps plan PDFs to IFC levels, so a bundle contains only sheets with at least one FloorPlan/EngineeringPlan viewport (+ unplaced floor-plan views). Cover/section/elevation/detail/legend/3D sheets are excluded by default (`includeUnmappedSheets` opt-in). Ceiling/area plans are not mapped unless `mapCeilingAndAreaPlans`. Links to excluded sheets carry `targetIncluded:false`. Primary viewport per (sheet, storey) = largest model-area crop, so dependent/enlarged partial plans do not win.

## 2026-11-21 — Floor-plan-only mapping and default selection
FreeField maps plan PDFs to IFC levels, so only `FloorPlan` and `EngineeringPlan` viewports are mapped (`pdfToModel`, `storeyName`); `CeilingPlan`/`AreaPlan` only with `mapCeilingAndAreaPlans`. Default export = sheets with at least one mapped plan whose level is an `IfcBuildingStorey` + unplaced floor-plan views (`VIEW-*`). Everything else is excluded unless `includeUnmappedSheets` (or explicitly ticked in the dialog). Links to excluded sheets keep `targetSheetNumber` and carry `targetIncluded:false`. `manifest.exportOptions.includeUnmappedSheets` records the choice; the validator fails any exported sheet without a mapped viewport when it is false. The pure selection logic is `Core.Selection.SheetSelectionModel` (shared by the dialog and headless jobs).

## 2026-11-21 — Primary viewport = largest model area; element box from solid geometry; page calibration safety net
Primary viewport per (sheet, level) is the one covering the largest model area (viewport box area x scale^2), so enlarged/dependent partial plans never steal it. Element boxes are the XY extent of the element's solid geometry (same extent as the IFC geometry), falling back to the Revit bounding box. Revit does not always print a titleblock-less temporary sheet where `ViewSheet.Outline` says (seen once: page taller, content at the top); `PageCalibrator` compares expected element outlines with the line segments drawn in the PDF and translates every page-space value of that page if (and only if) a pure translation fixes a low match.
