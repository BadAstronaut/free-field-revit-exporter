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
