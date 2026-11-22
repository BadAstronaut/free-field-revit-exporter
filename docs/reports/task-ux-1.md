# Task UX 1: quick access, building info, sheets from Documents

Branch `architect/task-quick-access` in the free-field-bundle worktree (stacked on `architect/documents-measure-toolbar`, itself stacked on `architect/viewer-keepalive-remount`). Three commits, no push, no merge.

| Item | Commit |
| --- | --- |
| 2. Building info not filled | 64ec7e8 |
| 1. Quick access from the location image | 714c527 |
| 3. Attach plans from Documents | f8e660b |

Screenshots: `e2e-browser/task-ux/` in the session scratchpad. Dev server: http://localhost:5173/ (demo auth), still running.

## Item 2: building info empty

Findings (reproduced in the browser with the demo seed and created tasks):
- Cause 1: the compose sheet spread `initial` after the context building, so a restored draft (building null) wiped the building the creation context supplied.
- Cause 2: nothing read the effective building for display. The Tasks table, scope, filters and detail used only the stamped `buildingId`, so tasks made before stamping showed "-" even when pinned in a model or sheet (seed tasks T001/T003/T006/T007).
- Entry points: Models, LinkView (same surface), Documents and the Tasks module all pass the active building to compose. Browser check: one task created from Models, Documents and Tasks, all stored the Snowdon (launch) building.

Fix: compose keeps the context building and otherwise stamps the building that owns the pinned model/PDF (`resolveCreateBuildingId`). Table rows, scope and detail resolve `effectiveTaskBuildingId`, so name, filter and grouping work for old tasks. Tasks with no building and no location stay "-".

Backfill: deliberately not written. The read-side fallback needs no migration; a write would bump `updatedAt`, add an activity entry per task and could be rejected by rules for non-owners.

Tests (failing first): context building survives draft restore, location fallback, `withEffectiveBuilding`, `taskBuildingLabel`, `resolveCreateBuildingId`.

## Item 1: quick access from the location image

- Clicking the location thumb (table, list, board) or the detail's location card opens where the task was made. The detail card has an "Open in Models / Open in Documents" label; thumbs get a pointer cursor, hover ring and tooltip. Board cards now show the thumb.
- 3D: Models with the stored camera restored and the element(s) selected. The camera pose (`location.bim.cameraPose`) and plan version (`location.pdf.pdfVersionId`) were already stored at creation, so no new storage was needed; old tasks without a pose just select the element.
- Plan: Documents on that sheet and page, zoomed to the pin (160%), pin highlighted (blue ring, about 10 s). Added `PdfViewer.focusUv` and `PdfOverlayHandle.focusUv`.
- The shell `TaskOpenBridge` switches building first, then routes. A deleted model/sheet shows a notice; a superseded or deleted pinned version opens the latest version of the same sheet with a notice ("Pinned on v1, which has been superseded. Showing the latest (v2) of ..."). Verified across buildings in the browser.
- Keep-alive: Models consumes the intent on the persistent viewer (waits for the model to be loaded, never reloads it).
- Pieces: `taskOpenStore`, pure `resolveTaskTarget`, `useModelsTaskOpen`, `useDocumentsTaskOpen`.
- Tests: resolver (11), thumb UI (5), detail label, Models hook (5), Documents hook (4), shell bridge (3).

## Item 3: attach plans from Documents

- "Attach from project" in the compose sheet and the task detail: picker over the building's sheets, grouped by folder path, with search, `v2/2` badges like the Documents list, and multi-select.
- Stored as references: `sheetRefs: [{ pdfFileId, pdfVersionId, versionNumber?, name, page? }]` (max 20), never a copy.
- Chips show the version: "v1 (newer: v2)" when superseded, "removed" when the sheet is gone. Click opens the chosen version in Documents (a deleted version falls back to the latest with a notice). Removable; read-only on terminal tasks.
- Offline / sync: references ride `createDoc` and `patch` through the outbox, the optimistic merge, drafts, and the edit diff/activity text ("attached sheet X"). The outbox schema is unchanged (opaque JSON). `TaskSheetRefSchema` is additive and optional; old docs and drafts parse unchanged.
- Firestore: proposal only, in `docs/integration/task-sheet-refs-rules.md` (allowlist `sheetRefs`, size <= 20). The UI is gated by the extended-fields capability (always on in demo), so cloud is unaffected until the rules land.
- Tests: field model, merge overlay, create DTO round trip, schema, pure catalog/picker logic (15), picker/chips/detail/compose UI.

## Verification

ff-tasks 120, ff-tasks-react 506, ff-firestore schema 11, ff-viewer 68, ff-viewer-react 518, apps/documents 95; new Models and settings tests pass. tsc is clean for touched packages apart from existing test-file type errors. Unchanged known issue: 6 apps/models suites and some settings suites do not load because `@thatopen/components-front` cannot be resolved, which is why the new Models/settings tests mock the viewer-tools barrel.

## Caveats

- The picker attaches the latest version (the badge shows when it later becomes superseded); choosing an older version in the picker is not offered.
- Zoom-to-pin waits out an in-flight page render, so on cold loads it can land about a second after the pin highlights.
- Notices are in-memory and dismissed after 12 s.
