# FreeField Exporter — Controlling Spec (v1)

Owner: lead orchestrator. Every agent reads this first. Deviations need a note in `docs/DECISIONS.md`.

## 1. Goal
A Revit add-in that exports **one bundle** (`<project>.ffbundle`, a zip) containing IFC model(s) + one single-page
PDF per sheet + a manifest, such that FreeField imports it and **every plan PDF is mapped (calibrated) and
scaled to its IFC storey automatically**, with no manual 2-point calibration.

Gold standard (see `../aec-exporter-research`):
- Speckle Drawings video (`ANALYSIS.md`, `frames/`): drawings are data. Every graphic on a sheet resolves to a model
  element with its properties; sheets link to each other via section/elevation/callout heads; schedules are
  queries over elements ("doors on this drawing") and export to XLSX.
- Dalux plugin (`dalux-re/ANALYSIS.md`): one-button upload flow with level mapping, property mapping, file/discipline
  mapping, IFC config, progress, atomic commit.

**"PDF has BIM properties"** means: for every element visible in a plan viewport we record its region on the PDF page
(bbox, plus outline when cheap) and its identity (IFC GlobalId + Revit UniqueId) and parameters. This is shipped
(a) in the bundle sidecar data FreeField reads, and (b) embedded inside each PDF as an attached file
`freefield-sheet.json` + XMP/Info metadata, so a PDF taken out of the bundle stays self-describing.

## 2. Repos and boundaries
- `freefield-exporter` (this repo, new): Revit add-in, core lib, schema, validator, docs. Speckle/Dalux may be named
  in research docs here.
- `free-field` (existing): the bundle **importer**. HARD RULE: no third-party platform/vendor names in that repo
  (no Speckle, Dalux, and also no Revit/Autodesk — FreeField code never names them). Call it a "FreeField bundle"
  from "an authoring tool". Follow its AGENTS.md (branch `architect/ffbundle-import`, `git add <exact files>`,
  commit trailer `Co-Authored-By: Ricardo Zepeda <rzlazo@gmail.com>`). Do not touch `apps/settings/{package.json,
  tsconfig.json,vite.config.ts}` — write a request in `docs/integration/` instead.
- `pdftorevit-agent`: Revit MCP harness. Already installed for Revit 2027 at
  `%APPDATA%\Autodesk\Revit\Addins\2027\` (HTTP on 127.0.0.1:7892, token in `revit-mcp-token.txt` next to the dll).
  Used for **testing/verification only** (read model state, cross-check manifest). Don't modify the installed addin.

## 3. Layout of this repo
```
schema/ffbundle.schema.json        JSON Schema (draft 2020-12) for manifest.json — source of truth
schema/ffbundle.d.ts               TS types mirroring the schema (FreeField copies this)
src/FreeField.Exporter.Core/       net8.0, NO Revit refs: manifest model, 2D affine math, bundle writer,
                                   PDF metadata/attachment embedding (PDFsharp, MIT)
src/FreeField.Exporter.Revit/      Revit add-in. Primary: Revit 2025 / net8.0-windows (user directive). Switchable via a
                                   RevitVersion property: 2026 → net8.0-windows, 2027 → net10.0-windows; reference
                                   RevitAPI/RevitAPIUI from "C:\Program Files\Autodesk\Revit <ver>" (Private=false)
tests/FreeField.Exporter.Core.Tests/  xUnit
tools/bundle-validator/            Node 20 + TS: schema validation + geometric validation against IFC via web-ifc
tools/scripts/                     build/install/run-e2e PowerShell
docs/                              SPEC, DECISIONS, integration notes, test reports
```

## 4. Bundle format `freefield.bundle/1`
```
manifest.json
models/<modelId>.ifc
sheets/<sheetNumber>.pdf                 single page each (FreeField "plan" kind requires pageCount==1)
data/elements.json                       { [ifcGlobalId]: ElementRecord }   (one dictionary for the bundle)
data/sheets/<sheetNumber>.json           SheetElementIndex: visible elements + links for that sheet
data/schedules/<slug>.json               DataTable per exported Revit schedule
```
File names: sanitize sheet numbers (`[^A-Za-z0-9._-]` → `_`), keep original in manifest.

### manifest.json (normative shape — schema file must match)
```jsonc
{
  "schema": "freefield.bundle/1",
  "generator": { "name": "FreeField Exporter", "version": "0.1.0", "host": "Revit", "hostVersion": "2027" },
  "exportedAt": "ISO-8601",
  "project": { "name": "", "number": "", "sourceFile": "Snowdon Towers Sample Architectural.rvt" },
  "units": { "length": "m", "pdf": "pt" },
  "coordinates": {
    "modelSpace": "ifc",            // transforms target IFC world coords (metres, Z-up) exactly as written in the IFC
    "ifcPlacement": "internal",     // IFC exported relative to Revit internal origin (decided; see DECISIONS)
    "trueNorthRad": 0.0
  },
  "models": [{
    "id": "arch", "file": "models/arch.ifc", "name": "", "discipline": "architecture",
    "storeys": [{ "name": "Level 3", "elevation": 7.2, "levelUniqueId": "", "ifcGlobalId": "" }]
  }],
  "sheets": [{
    "id": "<sheet UniqueId>", "number": "A103", "name": "Third Floor Plan",
    "file": "sheets/A103.pdf", "data": "data/sheets/A103.json",
    "page": { "widthPt": 2592, "heightPt": 1728 },
    "viewports": [{
      "id": "<viewport UniqueId>", "viewId": "<view UniqueId>", "viewName": "", "viewType": "FloorPlan",
      "scale": 96, "modelId": "arch", "storeyName": "Level 3",     // MUST equal IfcBuildingStorey.Name in the IFC
      "primary": true,                                              // one primary viewport per (sheet, storey)
      "pdfRegion": [x0, y0, x1, y1],                                // viewport crop on the page, pt, top-left y-down
      "pdfToModel": [a, b, c, d, e, f],                             // modelX = a*px + b*py + c ; modelY = d*px + e*py + f
      "controlPoints": [ { "pdfX":0,"pdfY":0,"modelX":0,"modelY":0 }, { ... } ]   // 2 far-apart points, exact under pdfToModel
    }]
  }],
  "schedules": [{ "id": "", "name": "Door Schedule", "category": "Doors", "file": "data/schedules/door-schedule.json" }],
  "elementsFile": "data/elements.json",
  "stats": { "elements": 0, "sheets": 0, "viewports": 0, "links": 0 }
}
```
**Coordinate contract (most important rule in this project):**
- PDF space: points, origin top-left of the page, y down (pdf.js convention FreeField uses).
- Model space: IFC world coordinates in metres **as authored (Z-up)**: modelX = IFC X, modelY = IFC Y, i.e. Revit internal
  coords x 0.3048 with internal-origin placement. NOTE: web-ifc emits Y-up vertices as (X, Z, -Y), so tools comparing
  against web-ifc geometry must use modelX = vx, modelY = -vz (amended 2026-11-20, see docs/freefield-integration.md).
- `pdfToModel` is a general 2×3 affine (it contains a reflection, because PDF y-down vs model y-up). Non-plan
  viewports (sections/elevations/drafting) get `pdfToModel: null` and no `storeyName`.
- FreeField world (verified): worldX = modelX + bx, worldZ = -modelY + bz, worldY = modelZ + by, where b = the coordinate
  shift stored in the building's base-model .frag (`getCoordinates()`). The y negation removes the reflection, so the
  calibration is a proper similarity.
- BIM properties in FreeField are read from the PDF's embedded `freefield-sheet.json` (pdf.js getAttachments) — no new
  DB table. The embedded JSON must therefore be self-sufficient: SheetElementIndex + referenced ElementRecords + sheet
  id/number + link targets (sheet number AND id).
- FreeField converts model XY → its world X/Z with its own convention and fits its similarity transform from the two
  `controlPoints` (it already has `computeSimilarityTransform`). The exporter never emits FreeField world coords.

### data/elements.json — ElementRecord
```jsonc
{ "uid": "<Revit UniqueId>", "eid": 123456, "gid": "<IFC GlobalId>", "category": "Doors", "bic": "OST_Doors",
  "family": "", "type": "", "level": "Level 3", "modelId": "arch",
  "params": { "<name>": { "v": <raw value>, "d": "<display string>", "u": "<unit or null>", "t": <isType bool>,
              "k": "<BuiltInParameter name | shared GUID | name>" } } }
```
Keyed by GlobalId (FreeField joins IFC ↔ PDF on GlobalId). Instance + type params; skip empty strings; cap
string length 2k.

### data/sheets/<n>.json — SheetElementIndex
```jsonc
{ "sheetId": "", "elements": [ { "gid": "", "vp": "<viewport id>", "bbox": [x0,y0,x1,y1], "outline": [[x,y],...]? } ],
  "links": [ { "kind": "section|elevation|callout|detail|view", "bbox": [..], "targetSheetId": "", "targetSheetNumber": "",
               "targetViewId": "" } ] }
```

### data/schedules/<slug>.json — DataTable (Speckle-style; columns bind to parameters, rows bind to elements)
```jsonc
{ "name": "", "category": "", "columns": [ { "heading": "", "paramKey": "", "fieldType": "instance|type" } ],
  "rows": [ { "gid": "", "uid": "", "cells": ["", ...] } ] }
```

## 5. Revit add-in behaviour
Ribbon tab "FreeField" → "Export Bundle". WPF dialog (Dalux-like, keep it lean):
1. Sheets picker. Default = ONLY sheets with at least one FloorPlan/EngineeringPlan viewport mapped to an IFC storey, plus unplaced floor-plan views; other sheets (cover, sections, details, legends…) are excluded unless `includeUnmappedSheets` (user directive 2026-11-21: FreeField maps floor plans to IFC levels; nothing else belongs in the bundle by default).
2. Level ↔ storey preview (read-only in v1: storey name = level name).
3. Options: include schedules, include element outlines, IFC options (IFC4 RV / IFC2x3 CV2.0; default IFC4),
   output folder.
4. Export with progress → writes `.ffbundle`, opens folder.

Pipeline: collect sheets/viewports → IFC export (inside a transaction that is rolled back if needed; "store IFC GUID"
so GlobalIds can be read; placement = internal origin) → PDF export per sheet (native `Document.Export(…, PDFExportOptions)`,
not inside a transaction, one file per sheet, 100% zoom, vector) → compute per viewport `pdfToModel` via
`View.GetModelToProjectionTransforms()` + `Viewport.GetProjectionToSheetTransform()` + sheet→page mapping (derive
page mapping from exported page size vs titleblock/sheet extents — verify empirically) → visible elements per plan
viewport with bbox projected to page → links from section/elevation/callout markers → schedules → embed per-sheet JSON
into each PDF → zip.

Headless automation (for tests): if env `FREEFIELD_EXPORT_JOB` points to a job JSON
`{ "rvt": "...", "output": "...", "sheets": ["A103", ...] | null, "exitWhenDone": true, "resultFile": "..." }`,
the add-in opens the document (or uses the opened one) on `ApplicationInitialized`/Idling, runs the export with
defaults, writes `resultFile` (status, timings, warnings, bundle path) and closes Revit if asked.

## 6. Acceptance (Definition of Done)
1. `dotnet test` green for Core (affine math round-trips, reflection handling, manifest serialization matches schema).
2. **Primary test target (user directive 2026-11-20): Revit 2025 + `STRUCTURAL_CO.rvt`** (user-supplied, workspace root;
   always work on a copy). Exports a bundle with every sheet that hosts plan viewports (FloorPlan / EngineeringPlan /
   CeilingPlan / AreaPlan). Snowdon Towers (Revit 2027 sample) is a secondary/optional target.
3. `tools/bundle-validator` passes: schema-valid; every file referenced exists; each PDF is single-page and size matches
   `page`; every viewport `storeyName` exists in its IFC; **geometric check**: for every plan viewport, ≥90% of
   indexed elements whose GlobalId exists in the IFC have their IFC placement/bbox centre (XY) inside their PDF bbox
   mapped through `pdfToModel` (tolerance 0.5 m), and median error < 0.25 m. Report written to `docs/reports/`.
4. Cross-check with Revit MCP: sheet count/numbers and level names match live model.
5. FreeField: importing the bundle creates the building's IFC version, one PDF per sheet, and a calibration per
   primary plan viewport, with no wizard. Vitest coverage for the importer; browser check that a plan overlays
   correctly in LinkView and clicking a PDF region shows element properties.
