# FreeField launch test: Snowdon bundle

Branch `architect/ffbundle-import` (worktree `free-field-bundle`), settings dev server with demo auth (local only, no Firebase).
Bundle: `snowdon-20261121-063659/Snowdon.ffbundle` (read-only copy used): 41.6 MB zip, 1 IFC (18 storeys, IFC length unit feet), 30 sheets, 31 primary plan viewports, 13 distinct storeys.
Screenshots: `C:/Users/rzlaz/AppData/Local/Temp/claude/eb7d94fc88555600/9eb0d2c6-ee9f-4574-8500-5e9b0e3ab70f/scratchpad/e2e-browser/snowdon/` (called `snowdon/` below). Scripts `s1-import.mjs`, `s2-lv.mjs`, `s3-docs.mjs` in the same folder.

## Summary

| Step | Result |
|---|---|
| 1 Data e2e (`FF_E2E_BUNDLE`) | PASS |
| 2a Import in the browser | PASS (29 s end to end, UI never blocked for long) |
| 2b LinkView overlay, 3+ levels | PASS after a bug fix (feet elevations) |
| 2c Documents A101 | PASS after two fixes (link hit priority, not-in-bundle links) |
| 2d Door Schedule (unplaced) | PASS (nothing breaks), known limitation |

## 1. Data-level e2e (real web-ifc + fake-indexeddb)

- Import wall clock 22.8 to 26.0 s in Node. 30 sheets imported, **31 calibrations created**, 0 warnings. `b = [3.63537, -4.12483, -3.18453]`, stored on the building (`metadata.bundleBaseShift`).
- One calibration per (PDF, storey) (31 distinct pairs). Calibrations per storey: Parking 3, L1 - Block 35 4, L1 - Block 37 2, L1 - Block 43 1, L2 3, L3 3, L4 5, L5 3, R2 1, R3 3, M1 1, Green Roof Hardscape 1, Block 37 - Parapet 1. K101 has two (L1 - Block 35, M1). C101 has a real 26 degree rotation (0.454 rad); everything else is axis-aligned.
- FreeField-frame geometry: 25,069 plan elements compared (GlobalId to fragment position).
  - **Model position inside the element's drawn bbox (+0.5 m): 25,069 / 25,069 (100%).**
  - Centre-to-centre error: median 0.000 m, 88.9% within 0.5 m. The 11% above 0.5 m are elements whose PDF bbox is clipped by the viewport crop or projected (long walls and beams: worst 19.5 m on A107, a roof element), which is why the test now asserts bbox containment rather than centre equality.
- Re-import is idempotent: 0 new IFC/PDF versions, 0 new calibrations (31 updated in place).
- Test change: `e2e.realBundle.test.ts` no longer assumes 6 plans, asserts one calibration per (pdf, storey), reports worst error and bbox containment.

## 2a. Browser import (new building "Snowdon", empty profile)

Wall clock from choosing the file (from `snowdon/s1-log.txt`):

| Phase | Time |
|---|---|
| Parse (read zip + manifest) | about 0.5 s |
| IFC to fragments (web-ifc in worker, 40 MB model) | 17.0 s |
| 30 PDFs stored (parse + blobs) | 10.1 s |
| Base shift + 31 calibrations | 1.1 s |
| Total | 29 s |

- The UI shows "Reading bundle...", "Importing model... N%", "Sheet i of 30 - A100", then the summary: Models 1 of 1, Sheets 30 imported, Calibrations 31 created, plus a new line "Time: model 17.0 s, PDFs 10.1 s, calibration 1.1 s" (added during this run).
- Responsiveness measured with requestAnimationFrame gaps during the import: worst 1.4 s (twice), 24 gaps over 100 ms. The model conversion runs in a worker; the stalls are the PDF page measuring / Dexie writes. Nothing blocking beyond about 1.4 s. The model step only shows "0%" before jumping to the sheet step (the converter reports little granularity). Minor.
- Screenshots: `snowdon/a-progress-0.png` ... `a-progress-5.png`, `snowdon/a-import-summary.png`.

## 2b. LinkView overlay (overlap mode, camera clamped top-down, PDF at 40%)

How the user picks between several PDFs on one level: the "Mapped PDFs" panel (toolbar PDF icon) lists every mapped PDF with its storey on the right (L1 - Block 35 has A101, G101, K101, SD101, A401...; L4 has A104, G104, SD104, A402, A403). Clicking a row switches the overlay; a PDF mapped to two storeys (K101: L1 - Block 35 and M1) gets the existing storey chooser. Screenshot: `snowdon/b-lv-A101-nav.png`.

Overlays (plan drawing registered over the model, linework on walls, slabs, parking stalls):
- Parking: `snowdon/b-lv-Parking-A100.png`
- L1 - Block 35: `snowdon/b-lv-L1B35-A101.png` (model parking stall lines coincide with the plan)
- L4: `snowdon/b-lv-L4-A104-fixed.png` (before the fix: `snowdon/b-lv-L4-A104.png`, no plane at all) and the second L4 PDF `snowdon/b-lv-L4-A402.png`
- R2: `snowdon/b-lv-R2-A106.png`

Caveat: in overlap mode LinkView clips everything above the level, so on upper floors only what is below the floor is visible; alignment there is judged on slabs and walls under the plan. The numeric proof is step 1.

**Bug found and fixed:** the plan (and the clip plane) was missing on every level except the first PDF. Cause: storey elevations are stored raw in IFC file units, and LinkView only recognised millimetres, so a model in feet put the plane at 3.28x the real height (L4: 32 m instead of 9.8 m). Fix: feet vs metres is decided against the model centroid height (the floor height waits for it). Commit below. Test: `apps/linkview/__tests__/linkviewNav.test.ts`.

## 2c. Documents, sheet A101 (686 elements, 20 navigable + 14 not-included small heads on the page)

- Tabs: Properties, Elements (686), Planting Schedule.
- Door tap: Puertas, Door-Passage-Single-Flush : 36" x 84", level L1 - Block 35, source element id, GlobalId and the parameter list; highlight is the oriented outline polygon (1 polygon, 0 bbox rects). `snowdon/c1-door-properties.png`
- Navigable link: tapping an elevation head to A404 opens "A404 - Residential Lobby" with "Back to A101"; Back returns to A101 (also A401, K101). `snowdon/c2-before-link.png`, `snowdon/c2-link-target.png`
- Link to a sheet not in the bundle (`targetIncluded:false`): tapping the head shows "Sheet A201 is not in the bundle." in the panel, no navigation, no Back, no error. `snowdon/c3-not-in-bundle-hint.png`
- Elements table: chips Muros 161, Montantes de muro cortina 122, Mobiliario 78, Paneles 64, Equipos especializados 33, Puertas 33. CSV: 686 rows x 67 columns (`snowdon/c4-elements-A101.csv`). XLSX for the Puertas chip: `Puertas-schedule-A101.xlsx`, 34 rows including header (33 doors). `snowdon/c4-elements-table.png`, `snowdon/c4b-elements-doors-columns.png`
- Embedded schedule tab "Planting Schedule": 15 rows, click a row highlights the element, XLSX download `schedule-Planting_Schedule-A101.xlsx` with 16 rows including header. `snowdon/c5-planting-schedule.png`

**Bugs found and fixed (both failing-test-first):**
1. 7 page-sized `view` links with `targetIncluded:false` on A101 swallowed every tap (links always beat elements). Now only tiny reference heads (<= 1% of the page) beat elements; larger links apply to empty space and only when navigable.
2. Callout regions (about 2.5% of the page, enlarged-plan areas) also beat the elements under them, so tapping a door inside the K101 callout navigated to K101. Fixed by the same rule (regions lose to elements).
3. Links to sheets not in the bundle were not marked `targetIncluded` by the parser and gave either a dead click or "not in this building". Now parsed, non-navigable and explained.

## 2d. Door Schedule (unplaced)

`data/schedules/door_schedule.json` is not placed on a sheet, so FreeField does not read it. Import, LinkView and Documents all run without errors or warnings about it. Known limitation: bundle-level schedules not embedded in a sheet PDF are not reachable in the app (also `data/elements.json` and `data/sheets/*.json` are not imported; the PDF attachment is the source of truth). A store plus a building-level panel is the follow-up (no DB migration done).

## Console

Only the demo seed's `Fragments: Model not found: <uuid>` error appears (pre-existing, the seeded demo models). One intermittent `pageerror: handler is not a function` appeared in 2 of the first LinkView runs; it did not reproduce in 4 later runs with stack capture, and is not tied to the bundle (not investigated further).

## Commits (branch architect/ffbundle-import, none pushed)

- `2b48d8b` Import report timings; real-bundle e2e handles several PDFs per storey and clipped elements
- `85423a4` LinkView: read storey elevations stored in feet
- `934522e` Documents: links to sheets outside the bundle are non-navigable with a hint
- `1837528` Documents hit test: only tiny reference heads beat elements

## Tests

settings 401 passed + 1 skipped (the same 8 suites still fail to load; `MappingView > a double click writes only once` is a timing-sensitive test that failed in 2 of 5 runs under load and is unrelated to this work), documents 83 passed, linkview 104 passed. The opt-in e2e passes on this bundle.

## Remaining issues

- Unplaced schedules (Door Schedule) are not reachable.
- Model import progress shows only "0%" then jumps (converter granularity).
- Overlap mode hides everything above the level, so upper-floor alignment is only visible on what is below the plan.
- Intermittent `handler is not a function` page error, not reproduced.
- Frame stalls up to 1.4 s during the PDF storing phase of the import.
