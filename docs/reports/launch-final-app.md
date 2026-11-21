# FreeField final launch check: STRUCTURAL_CO and Snowdon (launch bundles)

Branch `architect/ffbundle-import`, settings dev server `http://localhost:5173/` (demo auth, local only), Edge via playwright-core.
Bundles (from the installed add-in, run `launch-20261121-074941`): `STRUCTURAL_CO_launch.ffbundle` (0.5 MB, 6 pages) and `Snowdon_launch.ffbundle` (39.7 MB, 30 pages).
Screenshots: `C:/Users/rzlaz/AppData/Local/Temp/claude/eb7d94fc88555600/9eb0d2c6-ee9f-4574-8500-5e9b0e3ab70f/scratchpad/e2e-browser/launch-final/` (`launch-final/` below). Browser profile with both buildings: `.../e2e-browser/profile-launch`.

## Result per step

| Step | Result |
|---|---|
| 1 Fresh import of both bundles in two new buildings | PASS |
| 1 Snowdon re-import is idempotent | PASS |
| 2 STRUCTURAL_CO: calibrations, LinkView 101 and 201, Documents, XLSX | PASS |
| 2 Opt-in data e2e on both bundles | PASS |
| 3 Snowdon regression: LinkView L1 - Block 35 and L4, door tap, not-in-bundle hint, Planting Schedule | PASS |
| 4 "handler is not a function" | NOT reproduced (see below); a different intermittent viewer error found and fixed |
| 5 End state | both buildings imported in `profile-launch`; server running |

## 1. Import from scratch (Settings, "Import bundle")

New buildings "STRUCTURAL_CO (launch)" and "Snowdon (launch)", created through the "+" tab and the Import bundle button, as a user would.
- STRUCTURAL_CO: Models 1 of 1, Sheets 6 imported, **Calibrations 6 created**, Time: model 1.0 s, PDFs 1.7 s, calibration 0.2 s. `launch-final/structural-import-summary.png`
- Snowdon: Models 1 of 1, Sheets 30 imported, **Calibrations 31 created**, Time: model 24.4 s, PDFs 13.4 s, calibration 1.7 s. `launch-final/snowdon-import-summary.png`
- Snowdon re-import (building selected, same file): Sheets 0 imported / **30 unchanged**, Calibrations 0 created / 31 updated in place. Database afterwards: 30 PDF files, 30 PDF versions, 31 calibrations, 1 IFC file, 1 IFC version (no new versions). `launch-final/snowdon-reimport-import-summary.png`. Note: the unchanged model is still read and hashed before it is recognised as a duplicate (about 18 s for the 40 MB IFC); the result is correct, the wait is not.

## 2. STRUCTURAL_CO (the user's model)

Data e2e (`FF_E2E_BUNDLE`, real web-ifc): 1.1 s import, `b = [8.66214, 0.1524, 7.05017]`, **6 calibrations, one per page**: 101 FIRST FLOOR 2 (VIEW-101 and VIEW-CO-101), 201 SECOND FLOOR 1, 301 THIRD FLOOR 1, 401 FOURTH FLOOR 1, T.O. FOOTING 1. 380 plan elements compared: **380/380 model positions inside their drawn bbox, median centre error 0.000 m**, 100% within 0.5 m. Re-import idempotent. (Log: `launch-final/e2e-structural.log`.)

LinkView (overlap, camera clamped top-down, PDF at 45%): the plan linework sits on the model slab edge, beams and grid.
- 101 FIRST FLOOR: `launch-final/lv-s-101-b.png` (also `lv-s-101.png`)
- 201 SECOND FLOOR: `launch-final/lv-s-201.png`

Documents, VIEW-101_FIRST_FLOOR (77 elements):
- Tap an element: Armazón estructural / Concrete-Rectangular Beam 12" x 18", level, source id, GlobalId, parameters, oriented-outline highlight (1 polygon, no bbox rect). `launch-final/s-doc-properties.png`
- Link: the one link is a section to S-101, which is not in this bundle (`targetIncluded:false`). Tapping the section head shows "Sheet S-101 is not in the bundle." and does not navigate. `launch-final/s-doc-link-hint.png`
- Elements table: Armazón estructural 30, Cimentación estructural 21, Pilares estructurales 20, Bordes de losa 6. `launch-final/s-doc-elements.png`. XLSX for the first category: `Armazón_estructural-schedule-VIEW-101_FIRST_FLOOR.xlsx`, 31 rows including header; CSV 30 rows. No schedule tab (the bundle has none on this sheet).

## 3. Snowdon quick regression (Snowdon (launch))

- LinkView L1 - Block 35 (A101): `launch-final/lv-sd-L1B35-A101.png`. L4 (A104): `launch-final/lv-sd-L4-A104.png`. Both aligned (walls, slabs, parking stalls).
- Documents A101: door tap to Puertas / Door-Passage-Single-Flush with outline highlight; not-in-bundle head to "Sheet A201 is not in the bundle."; navigable head to A404 and Back to A101; chips + CSV 686 rows; Puertas XLSX 34 rows; **Planting Schedule tab 15 rows, XLSX 16 rows**. Screenshots `launch-final/sd-c1-door-properties.png`, `sd-c3-not-in-bundle-hint.png`, `sd-c2-link-target.png`, `sd-c4-elements-table.png`, `sd-c5-planting-schedule.png`.

## 4. Intermittent LinkView page error

Ran the LinkView flow (open, pick PDF, overlap, top-down) 5 more times on Snowdon with stack capture, plus the 2 regression runs and the STRUCTURAL_CO runs: **`pageerror: handler is not a function` did not reproduce** (0 in about 12 runs; it had shown twice in the earliest runs). Not fixed, nothing to fix without a stack.

One other intermittent error did show up (run 4 of 5): `[IfcViewer] failed to load snowdon_launch.ifc TypeError: Cannot add property baseCoordinates, object is not extensible`. A load that outlives its viewer (unmount / building switch) assigned `baseCoordinates` on the frozen disposed fragments core. Fixed failing-test-first (`setBaseCoordinates` guard; the test failed before). The only other console message is the demo seed's `Fragments: Model not found`.

## Bugs fixed during this check (commits, none pushed)

- `6c7025c` Documents: long section lines to missing sheets show the hint (a section line is about 2% of the page and was ignored; only page-sized boxes to missing sheets are ignored now).
- `17f12ac` Viewer: a load that outlives its viewer no longer throws on the disposed core.
- Earlier in the same branch: `2b48d8b`, `85423a4`, `934522e`, `1837528` (see launch-snowdon-app.md).

Tests: ff-viewer-react 518 passed, documents 84 passed, linkview 104 passed (settings unchanged: 401 passed, same 8 pre-existing load failures plus one timing-flaky MappingView test).

## How to see it

The prepared state lives in the automation browser profile; **your own browser starts empty** (the data is local IndexedDB), so either use the same steps with your own browser or the one-time import below.

1. Open `http://localhost:5173/` (demo auth). In the left rail bottom switch "View as" to **Admin**.
2. **Settings** (gear icon, left rail). The building tabs at the top show "Demo App", "STRUCTURAL_CO (launch)" and "Snowdon (launch)" in the prepared profile. In your own browser: click "+" to create a building, then in the PDF sidebar press **Import bundle** and choose the `.ffbundle` (files: `...\FreeFieldExporter\out\launch-20261121-074941\structural\bundle\STRUCTURAL_CO_launch.ffbundle` and `...\snowdon\bundle\Snowdon_launch.ffbundle`; Snowdon takes about 40 s).
3. **LinkView** (chain icon): pick the building, open the toolbar PDF icon (Mapped PDFs). Each row shows its storey on the right; pick a row (for Snowdon, L4 has A104, A402, A403, G104, SD104). Click the overlap button (two squares, right of the divider) and drag down to look straight down; the PDF opacity slider is at the bottom.
4. **Documents** (page icon): pick the building, open the PDF list (top-left button), choose a sheet (STRUCTURAL_CO: VIEW-101_FIRST_FLOOR; Snowdon: A101). Tap an element for properties and highlight; the right panel has Properties, Elements (category chips, Download CSV / XLSX) and, on Snowdon A101, the Planting Schedule tab. Tapping a section/elevation head opens the target sheet with "Back to ..."; heads whose sheet is not in the bundle show "Sheet X is not in the bundle."

Known limitations: bundle-level schedules that are not placed on a sheet (e.g. Snowdon's Door Schedule) are not shown; the unchanged-model re-import wait noted above.
