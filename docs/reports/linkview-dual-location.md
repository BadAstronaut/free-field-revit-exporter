# LinkView dual location (2D + 3D) - report

Worktree: repos/free-field-bundle, branch `architect/linkview-dual-location` (from 46a75bc).
Commits: b901cb5 (data model, thumbs, display, resolver), 35021df (LinkView creation + open, shell route).
Nothing pushed, merged or deployed.

## What LinkView stored before
LinkView had no creation path of its own. Its right pane is the shared 3D surface, whose tap-to-create
(`TapCreateTask3D`) stored only `location.bim` (elements, worldPoint, cameraPose) + one 3D snapshot
`location.thumb`. The left (PDF) pane had no task creation and no 2D task pins at all.
`TaskLocation` already allowed `bim` and `pdf` together; the gap was a second thumb and an origin marker.

## New fields (all inside `location`, optional, additive)
- `location.thumb2d { storagePath }` - the plan snapshot. `location.thumb` stays the 3D snapshot.
- `location.origin: 'linkview'` - creation marker (plain string so a later origin never breaks an old client).
- Storage path of the plan snapshot: `projects/{pid}/tasks/{taskId}/location-thumb-2d.jpg`
  (`locationThumb2dPath` in @ff/tasks), same prefix as the 3D one.
- Outbox payload `locationThumb2d` (blob ref) and draft field `locationThumb2d`; `outboxBlobKeys` lists it.
- Drafts' location schema now also keeps `thumb`, `thumb2d`, `origin` so a restored LinkView draft still sends both sides.
- Helpers in @ff/tasks: `LINKVIEW_ORIGIN`, `normalizeLocationThumb2dPath`, `isLinkviewLocation` (origin + bim + pdf),
  `isDualThumbLocation`.
- Quick-access intent kind `"linkview"` carrying `documents` and `models` halves (either null when its target is gone).
- Creation seam `setTaskLocationEnricher` (snapshot.ts): LinkView registers one while mounted; no-op elsewhere.

Display rule: side by side only when `origin==='linkview'` with both sides AND both thumbs. A LinkView task missing a
snapshot shows the single thumb (still opens LinkView). Models/Documents tasks are untouched.

## Rules
No change. `validTaskLocation()` only checks `location.pdf.pdfVersionId` (LinkView stamps it) and `location` is
otherwise an unshaped map. The storage rule `/projects/{pid}/tasks/{taskId}/{photoId}` already admits
`location-thumb-2d.jpg`. Added emulator tests for both (dual-location doc create/read; plan-snapshot upload): 57 pass.

## Behaviour
- Create: a 3D tap in LinkView with a calibrated sheet open also stores the plan pin (world point inverted through the
  open sheet's calibration; page 1, canonical calibId, pdfVersionId), the plan snapshot, origin. A tap that falls off the
  page, or no sheet open, gives an ordinary 3D task.
- Display: table location cell (two compact thumbs), list row, board card, task detail card (50/50, badge "2D + 3D"),
  compose preview, task print (plan before 3D).
- Open: either image (tooltip "Open in LinkView"), and the detail card, route to /linkview. Building is switched first;
  the sheet opens at the pin (160% zoom, pin highlighted, highlight restarted once the pin is in view); the 3D camera is
  restored after LinkView's own first-person camera seed and the element is selected. Version fallback ("Pinned on v1 ...
  Showing the latest (v2)") and missing-target notices reuse the Models/Documents resolvers; a gone sheet still opens the 3D half.
- LinkView now renders 2D task pins on the open sheet (needed for the highlight).
- The building auto-select no longer replaces a sheet opened by a task.

## Files changed
ff-firestore: src/schema/task.ts (+ tests schema-task, rules-tasks, rules-storage)
ff-tasks: action.ts, photos.ts, repository.ts, demoTaskSource.ts, taskBlobs.ts, drafts.ts, merge.ts (+ __tests__/linkviewThumbs.test.ts)
ff-tasks-react: snapshot.ts, composeModel.ts, draftRestore.ts, TaskComposeSheet.tsx, TapCreateTask3D.tsx, LocationThumb.tsx,
 tableColumns.ts, TasksListPanel.tsx, TaskBoardPanel.tsx, TaskDetailView.tsx, TaskDetailHost.tsx, printDocument.ts,
 taskOpenStore.ts, taskTarget.ts (+ __tests__/linkviewDual.test.tsx, linkviewDualSurfaces.test.tsx)
apps/linkview: LinkviewApp.tsx, LinkviewSplitCanvas.tsx, linkviewTaskLocation.ts (new), useLinkviewTaskOpen.ts (new)
 (+ 2 new test files; LinkviewApp.test.tsx mocks the 2D pin layer)
apps/settings: src/shell/TaskOpenBridge.tsx (+ case in __tests__/shell/taskOpenBridge.test.tsx)

## BCF-overlapping files
None touched: TaskToolbar.tsx, TasksApp.tsx, exportTasks.ts, index.ts, bcfBrowser.ts, bcfExport.ts, memberEmails.ts and
ff-tasks/src/bcf are unchanged (new exports ride the existing `export *` lines in snapshot.ts and merge.ts).
So there are no hunks to merge by hand. `exportTasks.ts` (HTML export) still prints the single `location.thumb` (the 3D one) for
LinkView tasks; it does not break. printDocument.ts (task print) shows both snapshots.

## BCF compatibility (read-only check of butler/bcf-export)
bcfExport.ts reads `location.thumb.storagePath` (snapshot), `location.bim.cameraPose` + `bim.elements` (viewpoint and
components), `location.pdf` (sheet pin note) and `location.text`. It ignores unknown keys, and no existing field changed
meaning. A LinkView task exports sensibly: 3D snapshot (`thumb`, the right image for a BCF viewpoint), camera, components,
and the "Sheet ... page N at x %, y %" note. `thumb2d` and `origin` are simply not exported (could be added as a second
image/comment later).

## Tests
- ff-tasks 130 pass (15 files); ff-tasks-react 527 (40 files); ff-firestore schema-*.test.ts 20; emulator rules-tasks + rules-storage 57;
  apps/linkview 118 (9 files); apps/documents 212 (23 files); ff-viewer-react 518 (34 files); settings taskOpenBridge 4.
- tsc clean for ff-tasks, ff-tasks-react, apps/linkview. apps/settings tsc has one pre-existing error (MappedStoreysSection.test.tsx).
- apps/settings' vitest config cannot load under Node 20.19 here (imports a .ts workspace file). I ran the bridge test through a
  throwaway config outside the repo (4 pass). Other settings suites and apps/models suites were not run.

## Screenshots (e2e-browser/linkview-dual/)
a2-marker.png, a3-compose-both-thumbs.png (create); b1-table.png, b2-lv-row.png (T014 LinkView vs T011 Models vs T012/T010 Documents, unchanged);
c1-detail.png (detail card with both images, "Open in LinkView"); d2-linkview-3d-result.png (click 3D image),
d3-plan-image-click.png (click plan image; pin T014 highlighted, plan 160%, camera + element restored); e1-other-building-tasks.png +
e2-cross-building-result.png (opened from the other building).
Scripts: a-create.mjs, b-table.mjs, d-open.mjs, open-wait.mjs, e-cross-building.mjs.

## Not verified
- The camera "restored" is judged visually against the creation view (same framing and the railing selected); no numeric pose compare.
- Cross-building: the screenshot shows the launch building's sheet and model after starting in the other building; the pin highlight
  had already expired in that capture (it was seen in d3). A cold sheet render is slow here (software GL, 35-70 s).
- Overlap mode, a sheet calibrated on several storeys, a multi-page sheet, mobile layout, real Firebase (cloud upload of
  location-thumb-2d.jpg is covered by emulator tests and the executor unit test, not by a live run).
- Tasks created in LinkView stay "Cross-building" in the compose building chip (LinkView does not pass a building to the creator, as before).
- BCF branch: read only, not built or run with a LinkView task.
