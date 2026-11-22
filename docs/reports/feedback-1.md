# Feedback round 1: five items

Branch `architect/ffbundle-import` (free-field-bundle worktree), dev server `http://localhost:5173/` (demo auth, local only), running.
Screenshots: `C:/Users/rzlaz/AppData/Local/Temp/claude/eb7d94fc88555600/9eb0d2c6-ee9f-4574-8500-5e9b0e3ab70f/scratchpad/e2e-browser/feedback-1/` (`feedback-1/` below).
Browser profile with all test buildings: `.../e2e-browser/profile-launch` (buildings STRUCTURAL_CO (launch), Snowdon (launch), Snowdon (versions)).

Each bug was reproduced in the browser first; failing test first where the logic is testable.

| # | Item | Result | Commit |
|---|---|---|---|
| 1 | LinkView split to overlap camera | FIXED | `f155c1c` |
| 2 | Documents drawing data in the toolbar | DONE | `9a7b145` |
| 3 | Model properties after a module round trip | FIXED | `970d758` |
| 4 | Re-import versions, not duplicates | DONE | `2c8c7f0` |
| 5 | Settings PDF panel buttons | DONE | `f47992b` |

## 1. LinkView: split, click a nav point, switch to overlap (`f155c1c`)

Reproduced: after placing a nav point in the PDF, overlap kept the split view's first-person camera (1.8 m up, horizontal), so the plan plane was seen at a grazing angle. `feedback-1/1-before-overlap.png`.
Cause: nothing re-framed the camera on entering overlap. Fix: `overlapTopDownPose` (pure, in `linkviewNav.ts`) puts the camera straight above the page centre at the storey floor height, high enough to fit the whole page (perspective, fov inherited), applied once per entry into overlap (re-armed when leaving it). After: `feedback-1/1-after-overlap.png` (whole page, top-down).
Tests: three pure-pose tests (looks straight down, bigger page is higher, fov handling) and the transition test in `LinkviewApp.test.tsx` (split to overlap sets a top-down pose; overlap to split still re-seeds).

## 2. Documents: Properties / Elements / Schedules in the floating toolbar (`9a7b145`)

Before: a right-hand panel with tabs and CSV/XLSX buttons (`feedback-1/2-before-properties.png`, `2-before-elements.png`).
Now (Documents module; the panel lived there, not in Models):
- The toolbar (document icon, outline icon) gets Properties, Elements and, only when the sheet has schedules, Schedules. They appear only for PDFs with drawing data. `feedback-1/2-after-toolbar.png`.
- Each opens the same frosted panel as Outline (same container, header and close). Tapping an element on the drawing opens Properties. `2-after-properties.png`.
- Elements: categories with counts (Muros 161, Montantes de muro cortina 122, ... Puertas 33). Clicking a category selects and outline-highlights ALL its instances on the PDF (Puertas: 33 outlines, Muros: 161; again clears; another switches). Clicking an element shows its properties. `2-after-elements.png`, `2-after-category-selected.png`, `2-after-category-walls.png`.
- Schedules: list of the sheet's schedules; open one for the table, a row highlights its element. `2-after-schedules-list.png`, `2-after-schedule-open.png`.
- CSV / XLSX buttons are gone from the UI (0 found in the browser); `tableExport.ts`, `xlsx.ts` and their tests are untouched.
- Implementation: the tap layer is headless now and writes a small store (`sheetDataStore`); the panel bodies and the highlight read it. Tests: store/category tests, panel tests, toolbar tests in `DocumentsApp.test.tsx` (buttons only with data, Schedules only with schedules, same slot, panel closes when the PDF has no data).

## 3. Model properties after a module round trip (`970d758`)

Reproduced (picks of two different elements, then Models to Documents to Models): after the first round trip the panel stayed on the old element or showed nothing (`feedback-1/3-before-trip1.png`; result per attempt: first load OK, round trips 1 and 2 stale).
Cause: the shared IfcViewer is re-mounted on a module round trip (instrumented: new instance each time), which swaps the imperative handle behind the SAME ref. The properties panel subscribed to picks once per ref object, so after the round trip its callback sat on the disposed viewer and the live viewer had no subscriber. Fix: the subscription loop now follows the live handle (re-subscribes when the handle changes). Test: `PropertiesDrawerRemount.test.tsx` (failed before).
Verified in the browser across 3 round trips: first load, trip 1, trip 2, trip 3 all OK (`feedback-1/3-after-trip1..3.png`).
Not the "handler is not a function" cause: that error is separate (see below).

## 4. Re-import versions existing items (`2c8c7f0`)

What the code did: PDFs already versioned when the same file name came back with different bytes, and the IFC likewise, but a re-export renames things (the two Snowdon bundles call the model `snowdon` vs `snowdon_launch`), so the model became a second IFC file and renamed sheets became duplicates.
Now: the model (matched by model id, else by position when the bundle has the same number of models) and each sheet (by sheet id, else number) of an earlier import become a new version of the existing file, whatever the tool called the file this time. Identical bytes stay unchanged. Calibrations are updated in place and apply to the latest versions (LinkView and Documents use the latest). What each item became is kept in `building.metadata.bundleIndex` (no migration); the existing manual-versioning paths (`importPdfFile`, `ingestIfc`) are reused.
Summary wording: "Models: N new, N new versions, N unchanged" and "Sheets: N new, N new versions, N unchanged, N skipped, N failed".
Browser (building "Snowdon (versions)"): import the launch bundle (Models 1 new; Sheets 30 new; 31 calibrations), then the earlier Snowdon export: **Models: 0 new, 1 new version; Sheets: 0 new, 30 new versions; Calibrations 0 created, 31 updated** (`feedback-1/4-A-import-summary.png`, `4-B-import-summary.png`). Database: 1 IFC file with 2 versions, 30 PDF files with 60 versions. The Settings list shows "v2" on every PDF and on the IFC (`4-versions-settings.png`); LinkView overlay on v2 (`4-versions-linkview-A104.png`).
Tests: `versioning.test.ts` (re-export with renamed model key and sheet title: v2 for the model and the changed sheet, identical sheet unchanged, one calibration moved, a third identical import changes nothing) and the opt-in e2e with both real Snowdon bundles (`FF_E2E_BUNDLE_2`): model new version, 0 new sheets, 30 new versions, 31 calibrations updated, 0 created.

## 5. Settings PDF panel (`f47992b`)

"Import bundle" is now a package icon button (iconoir `Package`, tooltip "Import bundle") right of the PDF "+". The PDF "+" was a bare glyph; it is now the boxed 30x30 style of the IFC "+" (the prevailing style), and the bundle button uses it too. Before `feedback-1/5-before.png` (earlier summary shot with the text button), after `5-after.png`. Test: `BundleImportTrigger.test.tsx`.

## "handler is not a function"

Reproduced once more in this round (LinkView on Snowdon). The stack is inside the fragments worker (`ConnectionHandlers.run` in a blob worker, `MessagePort.onInput`): a worker message for a handler that no longer exists after a model was disposed, the same class as the demo seed's `Fragments: Model not found`. It comes from library code, is not tied to the bundle, picks or the new work, and did not affect any flow; not fixed.

## Tests and typecheck

settings 406 passed + 1 skipped (same 8 suites fail to load: `@thatopen/components-front`); documents 91; linkview 107; ff-viewer-tools 196; ff-viewer-react 518; `@ff/data` 72. Typecheck clean for settings, documents, linkview, ff-viewer-react and ff-viewer-tools (apart from the old mock errors).

## How to see it

Server `http://localhost:5173/`, switch "View as" to Admin. In the prepared profile pick "Snowdon (versions)" (v2 everywhere), "Snowdon (launch)" or "STRUCTURAL_CO (launch)". Documents: open a sheet (Snowdon A101), use the new Properties / Elements / Schedules buttons next to Outline. LinkView: overlap now opens top-down. Settings: the package icon next to the PDF "+".
