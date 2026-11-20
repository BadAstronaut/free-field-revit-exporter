# Core library and validator

## FreeField.Exporter.Core (net8.0 + net10.0, no host-application references)

Namespaces (all in `src/FreeField.Exporter.Core`):

| Namespace | Types |
|---|---|
| `FreeField.Exporter.Core.Geometry` | `Point2`, `Affine2D` (x' = A*x + B*y + C, y' = D*x + E*y + F; `Identity`, `Apply`, `Then` (this first, then next), `Invert`, `Determinant`, `ToArray`/`FromArray`, `Scale`, `Translate`, `Rotate`, `FromPointPairs`), `BBox2` (`Transform`, `Union`, `ToArray`, `Center`) |
| `FreeField.Exporter.Core.Model` | POCOs for every file in the bundle: `BundleManifest`, `GeneratorInfo`, `ProjectInfo`, `UnitsInfo`, `CoordinatesInfo`, `ModelEntry`, `StoreyEntry`, `SheetEntry`, `PageSize`, `ViewportEntry`, `ControlPoint`, `ScheduleEntry`, `BundleStats`, `ElementRecord`, `ParamValue`, `SheetElementIndex`, `SheetElement`, `SheetLink`, `DataTable`, `DataColumn`, `DataRow`, plus `EmbeddedSheetPayload` (the JSON embedded in each PDF) |
| `FreeField.Exporter.Core` | `BundleJson`, `BundleNaming`, `ViewportMath`, `PdfTools`, `PdfPageInfo`, `BundleWriter`, `BundleReader` |

Notes:

- JSON names are exactly those of SPEC section 4 (explicit `[JsonPropertyName]`). `ViewportEntry.PdfToModel` is always written, as `null` for non-plan viewports. `ParamValue.V` is `object?` (string, double, long or bool; reading maps numbers to `long` when integral, else `double`).
- `PdfTools.GetPageInfo` reports page 1 as a viewer shows it: the CropBox (clipped to the MediaBox) if present, otherwise the MediaBox, with width/height swapped for `/Rotate` 90 or 270.
- `PdfTools.EmbedJson` adds an EmbeddedFiles attachment (and an `/AF` entry), replaces an attachment of the same name, and sets Info entries (`Title`, `Subject`, `Keywords`, `Author`, `Creator` map to standard fields; any other key becomes a custom `/Info` entry). It rewrites the file in place via a temp file.
- `BundleWriter.Write` assigns `ModelEntry.File`, `SheetEntry.File/Data`, `ScheduleEntry.File`, fills `Page` from the PDF when it is zero, recomputes `Stats`, embeds `freefield-sheet.json` into every sheet PDF copy (the source PDFs are never modified), and writes the zip to a temp file that is moved into place. Files (IFC, PDF) are streamed into the archive.
- PDFsharp is pinned to **6.2.4 (MIT)**. 6.5+ is AGPL with a runtime license check (see DECISIONS.md).
- The shared contract for FreeField is `schema/ffbundle.schema.json` plus `schema/ffbundle.d.ts` (no vendor names in either). The schema file also exposes `$defs` for `ElementsFile`, `SheetElementIndex`, `DataTable` and `EmbeddedSheetPayload`.

Tests: `dotnet test tests/FreeField.Exporter.Core.Tests`.

## tools/bundle-validator

Node 20 + TypeScript.

```
cd tools/bundle-validator
npm install
npm run build
node dist/cli.js <bundle.ffbundle> [--report out.md] [--json out.json] [-v]
     [--tolerance 0.5] [--fraction 0.9] [--median 0.25] [--skip-geometry] [--schema file]
npm test            # builds, generates synthetic fixtures (IFC + PDF + manifest) and runs pass/fail cases
```

Exit code 0 = pass, 1 = fail, 2 = usage. Checks:

1. Manifest and every data file validate against `schema/ffbundle.schema.json`; `elements.json` keys equal each record's `gid`; stats are consistent.
2. Every referenced file exists in the zip.
3. Each sheet PDF has exactly one page and its size matches `page` within 0.5 pt (same CropBox/rotation rule as `PdfTools`); the embedded `freefield-sheet.json` must be readable through pdf.js `getAttachments()` (as FreeField reads it), validate as `EmbeddedSheetPayload` (`schema, sheetId, sheetNumber, sheetName, viewports, elements, elementRecords, links`), match the sheet, cover its indexed elements with `elementRecords`, and have links with both `targetSheetId` and `targetSheetNumber`.
4. Each plan viewport: `controlPoints` agree with `pdfToModel` (< 1e-6 m), the model exists, and `storeyName` is an `IfcBuildingStorey.Name` in that IFC.
5. Geometric check per plan viewport: for indexed elements of that viewport whose `gid` exists in the IFC and which have triangle geometry, the PDF bbox centre mapped through `pdfToModel` is compared with the world XY bbox centre of the IFC geometry. Pass = at least 90% within 0.5 m and median < 0.25 m. The report also prints `det ratio = |det(pdfToModel)| / (scale * 0.0254 / 72)^2` (expected about 1; more than 2% off is a warning).

Implementation notes:

- The IFC is extracted to a temp file and opened with `OpenModelFromCallback` (random-access reads), `COORDINATE_TO_ORIGIN: false`; meshes are streamed only for the elements that are needed and discarded.
- web-ifc (pinned to 0.0.80) applies the IFC length unit (geometry comes back in metres, verified with a millimetre fixture) but returns a **Y-up frame**: (X, Y, Z) in web-ifc = (X, Z, -Y) in IFC. The validator converts back, so the contract "model space = IFC world coordinates, Z-up, metres" holds: modelX = webifcX, modelY = -webifcZ. A consumer that works directly in web-ifc coordinates must apply the same conversion before using `pdfToModel`.
- `src/fixture/make-fixture.ts` shows the contract end to end with a hand-written IFC4 (walls/slab, rotated placement, building offset) and a matching PDF.
