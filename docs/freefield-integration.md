# FreeField integration analysis (importer side)

Author: ff-analyst. Read-only analysis of `free-field` @ main (clean tree, `pnpm install --frozen-lockfile` done).
Paths below are relative to `free-field/` unless stated. Scratch scripts: `C:/Users/rzlaz/AppData/Local/Temp/claude/eb7d94fc88555600/9eb0d2c6-ee9f-4574-8500-5e9b0e3ab70f/scratchpad/ff-analyst/{mk.ifc,t.mjs}`.

## 0. Headline findings

1. **SPEC §4 needs an amendment.** web-ifc does NOT emit Z-up even with `COORDINATE_TO_ORIGIN:false`: it emits **Y-up (X, Z, -Y)**. And FreeField's importer runs with `COORDINATE_TO_ORIGIN:true`, so the stored fragment is additionally shifted by a per-model, data-dependent `coordinates` vector. The manifest's `modelX/modelY` (IFC world, Z-up, metres) is still the right thing to ship; the **importer** must convert.
2. **Formula (verified empirically):** `worldX = X + bx`, `worldZ = -Y + bz`, `worldY(up) = Z + by`, where `b` = the fragments `getCoordinates()` of the building's *base model* (first IFC file in the building's file list). `b` is only knowable after the IFC is converted. No other offset/rotation/scale.
3. FreeField's calibration is a pure rotation+uniform-scale similarity (no reflection); PDF y-down -> world +Z. The (X,-Y) swap makes the Revit `pdfToModel` reflection disappear, so it is representable. The overlay (`useFloorPlanOverlay`) only uses the **axis-aligned bounds** of the transformed page, so a calibration with rotation not a multiple of 0 is rendered wrong (see 1.5).
4. The importer should live in `apps/settings/src/bundle/` (Butler-owned; `jszip` is already a dependency of `apps/settings` and nowhere else; no package.json change).
5. IFC properties are NOT in Dexie; they live inside the `.frag` and are fetched by `localId == IFC expressId`. A GlobalId->expressId join is available from the frag (`getLocalIdsByGuids`).
6. Demo mode (`VITE_FF_DEMO_AUTH=true`) boots with zero Firebase; the emulator is irrelevant in that mode.
7. Test baseline: `@ff/data` 72/72 pass. `settings` suite cannot even start under Node 20 via `pnpm test` (see 8); with `NODE_OPTIONS=--import tsx` 359 tests pass, 8 suites fail to load (`@thatopen/components-front` unresolved) - environment issue, not caused by us.

## 1. World coordinate convention

### 1.1 Import path (evidence)
- `packages/ff-data/src/ifc/convertIfc.ts:121-128` creates `new FRAGS.IfcImporter()`, calls `setFragmentsWasmPath` (`webIfcWasm.ts:85-88`, only sets wasm path), `addAllAttributes/Relations`, `parseOnce = true`, then `importer.process({bytes, raw:true})` (`:140-158`). Nothing overrides `importer.webIfcSettings`.
- `node_modules/@thatopen/fragments/dist/Importers/IfcImporter/index.mjs:25-27`: default `webIfcSettings = { COORDINATE_TO_ORIGIN: true }`. So FreeField converts WITH origin shift. (`ifcMetadata.ts:417` also opens with `true`, but only for metadata.)
- Shift is stored, not baked away: `.../src/geometry/ifc-file-reader.mjs:132-137` reads `GetCoordinationMatrix`, `decompose()` (`:648-664`, rounds to 1e-5) and `geometry/index.mjs:438-449` writes it as `Meshes.coordinates` (position + x/y directions). Read back by `getCoordinates()` -> `[x,y,z,xx,xy,xz,yx,yy,yz]` (`FragmentsModels/src/virtual-model/virtual-helpers/coordinates-helper.mjs`).
- The local patch `patches/@thatopen__fragments@3.6.0.patch` only touches mobile memory / thread balancing (mesh-manager, view-manager, threads-data). It does not touch coordinates.
- Viewer alignment: `packages/ff-viewer-react/src/IfcViewer.tsx:3082-3090` sets `fragments.core.settings.autoCoordinate = false`; `:4097-4118` `resolveBase` sets `core.baseCoordinates` = coords of the current base, else of the first file (in `buildingFiles` order) that resolves; `:3977-3992` then does `model.object.position += base - coords_i`. No rotation/scale is applied to `model.object` anywhere (grep `object.rotation|scale` empty). Floor-plan plane and wizard picks live in this same three.js world.
- Wizard picks: `apps/settings/src/calibration/MappingView.tsx:650-656` stores `{x: point.x, z: point.z}` of the raycast world point; saved as `pointPairs {pdfX,pdfY,worldX,worldZ}` + `computeSimilarityTransform` (`:381-382`, `packages/ff-data/src/calibration/similarity.ts:68-123`).

### 1.2 Empirical verification
Authored `mk.ifc` (IFC4, SI metres): storey "Level 1" at z=3, two IfcWall (4x0.2x3, centred profile) at storey-local (1000.5, 2000.25, 0) and (1010, 2005, 0) => IFC world centres (1000.5,2000.25,[3..6]) and (1010,2005,[3..6]). Ran: raw web-ifc (both flags) and the exact FreeField importer config, then `FRAGS.SingleThreadedFragmentsModel` (`getCoordinates`, `getPositions`).

```
web-ifc COORDINATE_TO_ORIGIN=false bbox min [998.5, 3, -2005.1] max [1012, 6, -2000.15]   => (X, Z, -Y)
web-ifc COORDINATE_TO_ORIGIN=true  bbox min [0,-3,-4.95]       max [13.5,0,0]  coordMatrix t=[-998.5,-6,2000.15]
importer.webIfcSettings {"COORDINATE_TO_ORIGIN":true}
fragments getCoordinates [-998.5, -6, 2000.15, 1,0,0, 0,1,0]
getPositions(W1,W2) -> {2,-1.5,-0.1}, {11.5,-1.5,-4.85}       (local ids 60, 80 == IFC expressIds)
```
Check: W1 centre (X=1000.5, Z=4.5, -Y=-2000.25) + coords(-998.5,-6,2000.15) = (2, -1.5, -0.1). Matches. Same for W2: (1010,4.5,-2005)+c = (11.5,-1.5,-4.85). Matches.

### 1.3 Resulting formula
Fragment-local (= viewer world for the base model) = `(X + cx, Z + cy, -Y + cz)`.
For model i in a federated building: `pos_i = base - c_i` => world = `(X, Z, -Y) + c_i + base - c_i = (X + bx, Z + by, -Y + bz)`. So **all models of a building share the same shift `b`**, and federated models line up in IFC coordinates (assumes they share one IFC coordinate system, which Revit "internal origin" export guarantees). Derived by code reading for multi-model; single-model verified numerically.

CalibrationTransformParams inputs:
```
worldX = modelX + bx
worldZ = -modelY + bz
controlPoint pair -> computeSimilarityTransform({x:pdfX,y:pdfY}, {x:pdfX2,y:pdfY2}, {x:wX1,z:wZ1}, {x:wX2,z:wZ2})
```
`b = (bx, by, bz)` = `getCoordinates()[0..2]` of the building's base model (see 1.4). For the sample `sample.ifc` the importer gives `[14.29, 0, 7.20]` (differs from a standalone web-ifc open, `[13.30,1.22,8.42]`, so **never recompute with your own web-ifc; read it from the produced .frag**).

Sign sanity: PDF y-down == south == -IFC Y == +world Z, so an unrotated north-up plan has `rotation = 0`, `scale = metres/pt`. The `pdfToModel` reflection (det<0 in pdf->(X,Y)) becomes det>0 in pdf->(X,-Y). If det stays <0 after the swap the page is mirrored; the importer should reject/flag it. Overlay orientation confirmed: `IfcViewer.tsx:7727-7731` rotateX(-90deg) puts texture top at minZ.

### 1.4 Reading `b` in the importer
Cheapest and exactly consistent: after `runIfcImport`, parse the produced frag with the flatbuffer schema or `SingleThreadedFragmentsModel(modelId, fragBytes, true).getCoordinates()` (works in Node and in the browser, no worker; verified above). Choose the base model like the viewer: the building's IFC files in `db.ifcFiles` list order (`listIfcFilesForBuilding`/`useIfcFiles`: first with a version) - if a bundle has several models, import them in manifest order into an empty building so file 0 is the base. Persisting `b` per building would make calibrations robust against a later change of file order or re-import (FreeField today does not do this; calibrations silently drift if the base changes). Recommended: store it in the bundle record (see 9).

### 1.5 Caveats the exporter must respect
- **Rotation**: `addFloorPlanPlane` takes only `{minX,maxX,minZ,maxZ}` (`useFloorPlanOverlay.ts:230-258`, `IfcViewer.tsx:7683-7731`); texture is stretched to the AABB. Any calibration with `rotation` not ~0 renders wrong (90/180/270 too). Plan views must be oriented to Project North with no rotation; importer should warn when `|rotation|>~0.5deg` (still save it; picking/regions work, overlay does not).
- **Uniform scale only**: `computeSimilarityTransform` derives scale from the chord. Anisotropic `pdfToModel` (non-uniform) is not representable; importer validates `|sx-sy|/s < 1e-3`.
- **Vertical offset**: `floorZ` in LinkView = storey `Elevation * unitScale` (`apps/linkview/src/linkviewNav.ts:481-509`, `LinkviewApp.tsx:241-249`), i.e. unshifted IFC elevation, while geometry sits at `Z + by`. When `by != 0` the plane floats `by` metres off the model. This is a pre-existing FreeField behaviour (by is 0 for `sample.ifc`, -6 in my synthetic file). It does not affect XZ calibration. Flag to the Architect; a fix is `floorZ + by`.
- Units: frag geometry is metres (importer applies unit scale); `IfcBuildingStorey.Elevation` is in file units (`inferElevationUnitScale`). Export metres IFC (SPEC already does).

## 2. Storeys
- Extracted in `packages/ff-data/src/ifc/ifcMetadata.ts:97-112`: `readIfcString(line.Name.value) || "Storey <expressId>"`; sorted by elevation; elevation = `IfcBuildingStorey.Elevation` or 0 (`numericOrZero`).
- Only normalisation: `disambiguateStoreyNames` (`storeyNames.ts:15-37`): duplicate names get `" (2)"`, `" (3)"`... (also re-applied in `appendIfcVersion.ts:84`). Unique index `&[ifcVersionId+name]`. No trim, no case folding, no unicode normalisation. web-ifc decodes IFC escapes (`\X2\...`) so non-ASCII names come out as real Unicode; compare with ordinal equality after decoding.
- Match key everywhere is exact `StoreyRecord.name === CalibrationRecord.storeyName` (`useMappedPdfsForBuilding.ts:81-98`; `resolveStoreyFloorZ` `linkviewNav.ts:492`). Risks: duplicate Revit level names across models in one building (second becomes "Level 1 (2)" within ONE file only; different files may each have "Level 1" - the importer must pick which); empty/unnamed levels get `Storey <id>`; trailing spaces preserved.
- Importer rule: look up `db.storeys` (by buildingId + ifcVersionId) for the exact manifest `storeyName`; if absent, record a warning and skip that calibration (do not guess).

## 3. Programmatic entry points
IFC (pure, framework-agnostic): `ingestIfc({file, target:{kind:"byName", buildingId}, dedupe:true|false, onProgress, signal, importHooks})` in `packages/ff-data/src/pipeline/ingestIfc.ts:107-195`. Flow: per-file lock -> `runIfcImport(file, PIPELINE_IFC_IMPORT_OPTIONS, ...)` (`ifcImportClient.ts`; uses a Web Worker, falls back to in-thread under Node/vitest, `importHooks` test seam) -> builds frag Blob -> `appendIfcVersion` (`db/appendIfcVersion.ts`): one Dexie rw tx writing `ifcFiles`, `fragBlobs`, `ifcVersions`, `storeys`, `propertyCatalog`, remaps `extractedStoreys`/`buildingLevelSelection`. Returns `{ifcFileId, versionId, versionNumber, storeyCount}`. The hook `useIfcPipeline` is a thin React wrapper (`loadIfc(file, buildingId)`). `File` is needed (use `new File([bytes], name)`).
PDF: logic is **inside the hook** `apps/settings/src/hooks/usePdfPipeline.ts:174-221` (`loadPdf(file, buildingId)`): `parsePdfFile` (pdf.js `loadPDF`, page size, SHA-256 head hash) -> one Dexie tx: `pdfFiles` (find by `[buildingId+name]`, else add), `putPdfBlob`, `pdfVersions` (latest flag). `assertVersionKeepsMapping` rejects multi-page replace of a mapped plan. Not exported as a pure function; the importer (also in apps/settings) can call the hook from its component, or we extract `parsePdfFile/insertLatestVersion` into a sibling pure module (same workspace, Butler-owned).
Buildings/projects: `useProjectId()`/`useRequiredProjectId()` (`@ff/auth`) give the project; buildings via `createBuilding({projectId,name})` / `findBuildingByName` (`db/tables/buildings.ts`), `useBuildings` hook in settings. IFC/PDF rows are keyed by `buildingId` only; `calibrations` carry `projectId`. Never write the legacy "default-project" literal.
Calibration write: `createCalibration({projectId, pdfFileId, storeyName, pointPairs:[a,b], transformParams, confidence})` / `getCalibrationForPdfAndStorey` + `updateCalibration` (`db/tables/calibrations.ts:42-110`); wizard wraps lookup+write in one tx on `db.calibrations` (`MappingView.tsx:384-405`). Upsert by (pdfFileId, storeyName).
Sync: no explicit call. `packages/ff-sync/src/hooks.ts:68-70` installs Dexie `creating/updating/deleting` hooks that enqueue outbox rows (`doc-upsert`, `blob-upload`) when a sync engine is running (`engine.start`); tables listed in `ff-sync/src/tables.ts` (buildings, ifcFiles, ifcVersions, storeys, extractedStoreys, pdfFiles, pdfVersions, calibrations, folders, viewpoints). Writing through Dexie is sufficient; a new table needs a sync config + Firestore schema (see 5/9).
Order for an importer: (1) pick/create building (2) for each model: `ingestIfc` (3) read frag coordinates -> `b` (4) for each sheet: PDF import -> `pdfFileId` (5) per primary plan viewport: resolve storey, compute calibration, upsert (6) store sheet regions/elements (7) optionally set `buildingLevelSelection`/`extractedStoreys` (see 6).
Pure vs hook: pure = `ingestIfc`, `appendIfcVersion`, table CRUD, `computeSimilarityTransform`, `applyCalibration`. Hook-only today = PDF loading, `useBuildings`, project selection.

## 4. Placement, ownership, jszip
- Ownership (`AGENTS.md` rule 4, `ORCHESTRATION-RULES.md`): Butler owns `apps/settings/`, `apps/documents/`; Architect owns `apps/linkview`, `apps/models`, `packages/ff-types`, `packages/ff-viewer-react`; `ff-data` is shared. Package.json/lockfile/tsconfig are Architect-only.
- `jszip ^3.10.1` is declared only in `apps/settings/package.json:39` (used by `apps/settings/src/seed/seedExport.ts:20`). Therefore the bundle importer goes in **`apps/settings/src/bundle/`** and can `import JSZip from "jszip"` with no config change. Putting it in `packages/ff-data` would need a package.json edit (Architect). Pure math (control points -> CalibrationTransformParams) can go to `packages/ff-data/src/calibration/` if the Architect agrees, but is not needed.
- UI entry: `apps/settings/src/components/PdfSidebarBlock.tsx` / `IfcSidebarBlock.tsx` (+ `UploadButton.tsx`): add an "Import bundle" button beside the PDF upload that opens a file picker (`.ffbundle,.zip`) and a progress/summary dialog (new component `BundleImportButton.tsx`). Vitest suites exist for PdfSidebarBlock (`__tests__/components/PdfSidebarBlock.test.tsx`).
- Types: `ffbundle.d.ts` is copied into `apps/settings/src/bundle/ffbundleTypes.ts` (not into ff-types, Architect-owned).

## 5. Element properties and regions
- Today: properties are not persisted in Dexie. `propertyCatalog` stores only `(fileId, pset, name)` schema pairs (`ifcMetadata.ts:289-380`). Actual values live inside the `.frag` (importer `addAllAttributes/addAllRelations`, `convertIfc.ts:128-132`) and are read on demand in the viewer: `IfcViewer.tsx:7037-7330` `model.getItemsData([localId], {relations:{IsDefinedBy:...}})`, flattened by `packages/ff-viewer-react/src/flattenItemData.ts` (`getPropertyGroups`, `flattenItemData`). **localId == IFC expressId** (verified: GUID lookup returned 60/80 = express ids).
- GlobalId -> expressId: `SingleThreadedFragmentsModel.getLocalIdsByGuids(guids)` (or in-viewer `model.getLocalIdsByGuids`); in the importer, one call per bundle model after conversion. Elements missing from the IFC come back `null` (skip, count as warning).
- Two property sources then exist: IFC psets (via frag, already shown in Models) and the bundle's `elements.json` (host parameters, richer). The Documents click UI should show the bundle record first (`params` with `d`/`u`) and offer the IFC psets by expressId when needed.
- Storage for regions/elements: no table exists. Needs Dexie `version(12)` (`ff-data/src/db/database.ts` currently ends at `version(11)`, :238) with e.g. `sheetRegions` (`id`, `pdfVersionId`, `[pdfVersionId]`, rows `{gid, vp, bbox, outline?}` or one row per sheet holding the array) and `bundleElements` (`[buildingId+gid]`, JSON record). `ff-data` is "shared, with care"; Dexie bump is additive. For cross-device sync it also needs a `TableConfig` in `ff-sync/src/tables.ts` + a `@ff/firestore` schema + rules (Architect/shared, defer to phase 2; v1 is local-only like `propertyCatalog`). Zero-schema interim: store the per-sheet index JSON as an extra `pdfBlobs` entry `${blobKey}__regions` (not synced either).
- Click hook locations: Documents viewer `apps/documents/src/DocumentsViewerPanel.tsx:113-127` already passes `onTap(coords)` to `<PdfOverlay>` (`packages/ff-viewer-react/src/pdf/PdfOverlay.tsx:72-88`; coords are PDF page points, y-down = bundle bbox space). Add a hit-test (smallest bbox containing the point) and a small properties popover; Butler-owned. LinkView (`apps/linkview/src/LinkviewApp.tsx:943`, left pane `<PdfOverlay onPdfClick>`) is Architect-owned and currently routes clicks to nav; a read-only region hit-test could share a hook exported from `apps/settings/src/bundle/` or `@ff/data`. Sheet-to-sheet links reuse the same hit-test: link bbox -> `pdfFiles` by sheet number -> `setActivePdfVersionId` (viewer store).

## 6. LinkView overlay requirements
- `useMappedPdfsForBuilding` / `queryMappedPdfsForBuilding` (`apps/linkview/src/hooks/useMappedPdfsForBuilding.ts:66-125`): PDFs of the building with >=1 calibration whose `storeyName` equals a `db.storeys` name of that building, latest version `pageCount <= 1`. Orphaned storeyName => hidden. So the importer must: single-page PDFs, building-scoped `pdfFiles`, exact storey names.
- `LinkviewApp.tsx:215-262` chooses the calibration (chosen storey, else active storey, else first by elevation), computes `floorZ`, passes `transformParams` to `useFloorPlanOverlay` (`packages/ff-viewer-tools/src/hooks/useFloorPlanOverlay.ts`): renders page 1 at 2x, computes world AABB via `applyCalibration` on the 4 corners, `addFloorPlanPlane`.
- Nothing else is required for the plan to show: no manual level selection. Optional niceties: `extractedStoreys` / `buildingLevelSelection` (Get Levels flow, `useExtractedStoreys.ts`, `useLevelSelection.ts`) only drive Models-level filtering, not the overlay. Keep `appendIfcVersion`'s own bookkeeping; do not write them in v1.
- PDF names: `pdfFiles.name` is the user-visible name; use `"<number> - <sheet name>.pdf"`.

## 7. Running locally (browser check)
- Env files (user supplied, gitignored; values not reproduced here): `apps/settings/.env.local` defines `VITE_FIREBASE_USE_EMULATOR=true`, `VITE_FF_DEMO_AUTH=true`, the `VITE_FIREBASE_*` web config, `VITE_EXPECTED_FIREBASE_PROJECT_ID`, `VITE_SENTRY_DSN`; `apps/linkview/.env.local` has `VITE_FIREBASE_USE_EMULATOR=true`. `apps/settings/.env.production` must never be used.
- With `VITE_FF_DEMO_AUTH=true` (`packages/ff-auth/src/AuthProvider.tsx:227-233`, `demo.ts`) Firebase is not initialised, no tenant check, no sign-in; role toggle "View as Viewer|Admin" at the bottom of the left rail (admin needed for upload/Settings); role in `localStorage['ff-demo-role']`. Data is local Dexie (IndexedDB) under the legacy project id (`DEMO_PROJECT_ID`); first boot injects the seed from `apps/settings/public/seed/` (`seed/demoSeed.ts`). Importer writes to the same Dexie, so no backend is needed to verify import + overlay. **Minimum env: `VITE_FF_DEMO_AUTH=true` only** (verified: dev server boots and serves HTTP 200 with it passed on the command line, no .env needed; Sentry DSN absent => local-only telemetry).
- Commands (from `free-field/`):
  - `pnpm install --frozen-lockfile` (done)
  - Dev server: `cd apps/settings && pnpm dev` (runs `setup` copying web-ifc/pdfjs assets, then Vite at http://localhost:5173; `.env.local` is picked up automatically; or `VITE_FF_DEMO_AUTH=true pnpm dev`). Note `pnpm dev` uses `tsx/esm` loader, which is why it works on Node 20 while `vitest` does not (section 8).
  - Emulator (only if demo auth is turned off / for `@ff/firestore` and sync tests): ports in `firebase.json` auth 9099, firestore 8080, functions 5001, storage 9199; `firebase-tools@15.22.4` is a devDependency of `packages/ff-firestore` (not on PATH): `cd packages/ff-firestore && pnpm exec firebase emulators:start --only auth,firestore,functions,storage --project demo-freefield` (needs Java; `demo-` project id never touches real Firebase). Sync-only emulator config: `firebase.sync-emulator.json`. Not needed for the bundle importer in demo mode.
  - Guardrails: no deploy, no `.env.production`, emulator/demo only.
- The built-in browser can then open `http://localhost:5173/` (redirects to `/models`), switch to Admin, use Settings > PDF sidebar. I did not drive a browser; only HTTP 200 and clean Vite start were confirmed.

## 8. Test baseline (Node v20.19.1, Windows)
- `pnpm --filter @ff/data test`: **12 files, 72 tests, all pass** (includes real web-ifc conversion tests).
- `pnpm --filter settings test`: **fails at startup** (`ERR_UNKNOWN_FILE_EXTENSION .ts` for `packages/ff-tooling/vite.config.base.ts` - vite config imports a .ts natively; needs Node >= 22.6 type stripping or a loader). Workaround that works: `cd apps/settings && NODE_OPTIONS="--import tsx" pnpm exec vitest run --no-file-parallelism` -> **55 files: 47 pass, 8 fail to load; 359 tests pass, 0 test failures**. The 8 failing suites (AppShell, importErrors, PdfSidebarBlock, combinedSwitcher, demoAuth, mainShell.cloud, signOut, MappedStoreysSection) all error with `Failed to resolve import "@thatopen/components-front" from packages/ff-viewer-react/src/IfcViewer.tsx` (that dependency is not resolvable in this install; AGENTS.md claims these pass, so it is local-environment drift - needs Architect/lockfile check). `PdfSidebarBlock` is the suite relevant to the new button.
- `@ff/firestore` needs the emulator (not run). `pnpm typecheck` not run.

## 9. Importer design proposal ("FreeField bundle")

### 9.1 Contract amendments (this repo, SPEC §4)
- State that `modelX/modelY` = IFC world Z-up metres (as authored in the IFC, i.e. `IfcLocalPlacement` chain composed, **not** web-ifc output; web-ifc emits (X,Z,-Y)). The exporter's validator in `tools/bundle-validator` must therefore compare against `(vertex.x, -vertex.z)` of web-ifc `COORDINATE_TO_ORIGIN:false` output, or read placements directly.
- Require `det(pdf->model 2x2) < 0` for upright pages (reflection) and uniform scale; plan viewports oriented to Project North (rotation ~ 0) until FreeField's overlay supports rotation.

### 9.2 New files (all in `free-field/apps/settings/`, Butler-owned; branch `architect/ffbundle-import` per SPEC)
- `src/bundle/ffbundleTypes.ts` - copy of `schema/ffbundle.d.ts`.
- `src/bundle/readBundle.ts` - `readBundle(file: File|Uint8Array): Promise<ParsedBundle>` with JSZip (manifest, validates `schema === "freefield.bundle/1"`, file presence, sheet PDFs as `Uint8Array`, models, `data/elements.json`, per-sheet indices). Pure.
- `src/bundle/bundleCalibration.ts` - pure: `buildCalibration(vp, base:[bx,by,bz]) => {pointPairs, transformParams, warnings}` using `worldX = modelX + bx`, `worldZ = -modelY + bz`, `computeSimilarityTransform` (import from `@ff/data/calibration/similarity`); checks uniform scale, rotation, mirror.
- `src/bundle/fragCoordinates.ts` - `readFragCoordinates(fragBytes)` + `guidsToLocalIds(fragBytes, guids)` via `FRAGS.SingleThreadedFragmentsModel` (`@thatopen/fragments` is already a dep of `@ff/data`; add the import through `@ff/data` re-export or confirm apps/settings can resolve it - if not, put these two helpers in `packages/ff-data/src/ifc/` (shared) and export via `ff-data/src/ifc/index.ts`).
- `src/bundle/importBundle.ts` - orchestrator `importBundle({file, projectId, buildingName|buildingId, onProgress, signal, deps?}) => ImportReport`. `deps` injects `ingestIfc`, `loadPdf` for tests.
- `src/bundle/useBundleImport.ts` - React hook: wires `usePdfPipeline().loadPdf`, project id, progress state.
- `src/components/BundleImportButton.tsx` (+ mount in `PdfSidebarBlock.tsx`) - file picker, progress, per-sheet report (imported / skipped with reason).
- `src/bundle/regionStore.ts` + Dexie v12 in `packages/ff-data/src/db/database.ts` and `packages/ff-types` types (needs Architect for ff-types; interim: local types in `src/bundle/`) - `sheetRegions`, `bundleElements`.
- `apps/documents/src/DocumentsViewerPanel.tsx` + new `ElementPropertiesPopover.tsx` - region hit-test on `onTap`, popover (Butler). LinkView hook: written as a request in `docs/integration/` for the Architect (do not touch `apps/linkview`).
- `docs/integration/ffbundle-import-request.md` in free-field - requests for Architect: Dexie v12 + ff-types types, ff-sync table config, `floorZ + by` fix, overlay rotation support, `@thatopen/components-front` resolution.

### 9.3 Data flow
1. `readBundle` -> manifest + blobs. 2. Create/choose building (`findBuildingByName` else `createBuilding({projectId, name: manifest.project.name})`). 3. For each model in manifest order: `ingestIfc({file:new File([ifcBytes], model.file basename), target:{kind:"byName", buildingId}, dedupe:true})`; then fetch the frag blob (`getFragBlob(version.fragBlobKey)`) and `readFragCoordinates`; base `b` = model[0]'s. 4. Resolve storeys: `db.storeys.where("ifcVersionId").equals(versionId)` -> name map (exact match). 5. For each sheet: build `File` from PDF bytes named `"<number> - <name>.pdf"`; `loadPdf(file, buildingId)` (single page enforced). 6. For each viewport with `primary && pdfToModel && storeyName`: `buildCalibration` -> upsert in one `db.transaction("rw", db.calibrations, ...)` (pattern of `MappingView.tsx:384-405`); `confidence: 1`; skip with warning when storey missing. 7. Regions: for each `data/sheets/*.json` element: `guidsToLocalIds` (expressId join) -> `sheetRegions` rows `{pdfVersionId, gid, expressId|null, vp, bbox}`; `elements.json` -> `bundleElements`; links -> `sheetLinks`. 8. Report. Sync happens through Dexie hooks (calibrations/pdf*/ifc*); regions/elements local-only in v1.

### 9.4 Idempotence
Re-import: PDF dedupe by `(buildingId, name)` creates version +1; IFC dedupe by content hash; calibration upsert by `(pdfFileId, storeyName)`; regions replaced per `pdfVersionId`. Persist `{bundleId: exportedAt+project.number, base:b}` on the building `metadata` (existing `BuildingRecord.metadata` field) so later imports reuse the same `b` and flag drift.

### 9.5 Tests to add (vitest, `apps/settings/__tests__/bundle/`)
- `bundleCalibration.test.ts`: reflection -> rotation 0 case; known pair maps `(modelX,modelY)` to expected `(X+bx, -Y+bz)`; rotated / anisotropic warnings. Use the numbers from section 1.2.
- `fragCoordinates.test.ts`: convert a tiny generated IFC (port `mk.ifc`) through `convertIfc` (real web-ifc, as `packages/ff-data/__tests__/ifc/convertIfc.test.ts` does) and assert `coords + (X,Z,-Y)` == `getPositions` for both walls; GlobalId->localId join.
- `readBundle.test.ts` with a JSZip-built fixture (valid, missing manifest, bad schema, missing file).
- `importBundle.test.ts` with `fake-indexeddb`: end-to-end into Dexie asserting `ifcFiles/ifcVersions/storeys/pdfFiles/calibrations` rows, storey-missing skip, idempotent re-import, multi-page rejection.
- `BundleImportButton.test.tsx` (button renders in PdfSidebarBlock, calls importer).
- Cross-repo golden: exporter emits a fixture bundle (+ expected calibration JSON) consumed by this suite.

### 9.6 AGENTS.md hard-rule check
1. No third-party platform names: all new code/UI/test names say "FreeField bundle" / "authoring tool"; the manifest `generator.host` is data, never hard-coded or branched on; do not copy `generator.name` into UI strings or comments; grep gate: `rg -i "speckle|dalux|revit|autodesk|ifcopenshell" apps packages` must stay empty (this doc lives in the exporter repo, not free-field).
2. settings lockdown: no edits to `apps/settings/{package.json,tsconfig.json,vite.config.ts}`; jszip already declared; `@thatopen/fragments` access via `@ff/data` (already a workspace dep) to avoid a new dependency; anything else goes in `docs/integration/` as a request.
3. Branch/commit hygiene: branch `architect/ffbundle-import`, `git add <exact files>`, trailer `Co-Authored-By: Ricardo Zepeda <rzlazo@gmail.com>`, push after each chunk.
4. Module ownership: Butler modules (settings, documents) edited by Butler; `ff-types`, `apps/linkview`, `ff-viewer-react`, Dexie bump (shared) go through the Architect request.
5. Project id: use `useRequiredProjectId()`/`requireProjectId()` for writes; never `"default-project"`.
6. Finish with `.last-task.md` summary, `pnpm test` counts, `pnpm typecheck`, `pnpm lint:drift` (no raw `three` imports; the importer needs none).
