# Viewer keep-alive: the shared viewer was re-mounted on every module switch

Branch `architect/viewer-keepalive-remount` (free-field-bundle worktree), measured in the browser (headless Edge, dev server with React StrictMode, SwiftShader GPU) on **Snowdon (launch)**: one 40 MB IFC, 18 storeys. Raw data: `.../e2e-browser/perf/{before,after}-{documents,linkview}.json`.

## Answer to "is the properties fix optimal?"

No: `970d758` fixed the symptom (the properties panel followed the new viewer after a re-mount) and the per-frame loop was cheap, but the viewer should never have been re-mounted. Both are now fixed properly: the viewer mounts once, and the properties panel is event-driven.

## Root cause

`ViewerKeepAlive` keeps `PersistentViewerSurface` mounted for the session, but `PersistentViewerSurface` rendered
`createPortal(<ViewerSurface .../>, hostNode)` and returned `null` while `hostNode` was null. `hostNode` is the DOM node of the ACTIVE route's slot, so it changes on every route switch (Models slot, null while the next route lazy-loads, Documents slot / LinkView pane). **A React portal whose container changes remounts its children**, so every switch destroyed the whole `<IfcViewer>` subtree (and the `return null` during the gap did it again): a new viewer, a new fragments worker, new WebGL contexts, and the 40 MB model re-loaded and re-tessellated from IndexedDB. The comments claiming "only the portal target changes, the React subtree never remounts" were wrong.

Ruled out by measurement: `RequireProjectMember` / `ErrorBoundary` re-keying (they sit above the surface and did not re-mount), `renderPaused` (a prop, no structural change).

## Fix

1. `PersistentViewerSurface` portals into ONE stable container element created once. The active route's host merely adopts that element (`appendChild`, a DOM move; the WebGL canvas and context survive a move). With no host (between routes) the container is parked off-screen at a real size (1280x800, hidden) so the canvas never collapses to 0x0.
2. `PropertiesDrawer` no longer polls: `ViewerKeepAlive` creates an observable ref (`createObservableRef`) and `useViewerHandle` (`useSyncExternalStore`) gives the live handle as state; the pick subscription re-runs exactly when the handle appears or is replaced (error-boundary retry, rebuild). A plain `RefObject` still works (waits for the first handle on cold mount with one rAF retry loop that stops as soon as a handle is held).

Tests: `apps/models/__tests__/PersistentSurfaceMount.test.tsx` (one mount, zero unmounts across host A to B to no host to C, same DOM element moved; failed before the fix), `PropertiesDrawerRemount.test.tsx` (re-subscribes when the handle is replaced, no resubscribe while unchanged, plain-ref cold mount stops polling), `useViewerHandle.test.tsx`.

## Numbers (3 round trips each; dev build, StrictMode doubles mounts)

IfcViewer mounts / unmounts (cumulative; baseline = after first load):

| | Before | After |
|---|---|---|
| baseline | 6 / 5 | 8 / 7 |
| after trip 1 | 10 / 9 | 8 / 7 |
| after trip 2 | 14 / 13 | 8 / 7 |
| after trip 3 | 18 / 17 | 8 / 7 |

That is 4 mounts per round trip before (a re-mount, doubled by StrictMode) and 0 after (the baseline of 8 vs 6 is just first-load variance of the building switch).

Time from the route switch back to Models until the viewer is visible / until a pick shows the right element (ms):

| | Before: Documents | After: Documents | Before: LinkView | After: LinkView |
|---|---|---|---|---|
| trip 1 viewer visible | 12,322 | 59 | 24,012 | 51 |
| trip 2 | 15,123 | 768 | 14,558 | 757 |
| trip 3 | 13,117 | 465 | 25,372 | 58 |
| trip 1 interactive | 16,083 | 1,703 | 28,215 | 7,052 |
| trip 2 | 19,714 | 5,081 | 24,103 | 7,097 |
| trip 3 | 16,555 | 1,382 | 30,310 | 980 |

("Interactive" includes the measuring script's own ~1 s click waits and, for LinkView trips 1 and 2, a first pick that hit a different element because LinkView had moved the camera; the viewer itself is visible within 0.05 to 0.8 s.) First load of the 40 MB model is unchanged (about 8 to 12 s, once per session).

Workers and WebGL contexts (created counts; live = created minus terminated):

| after trip 3 | Before: Documents | After: Documents | Before: LinkView | After: LinkView |
|---|---|---|---|---|
| WebGL contexts created | 58 (was 22) | 28 (flat) | 58 | 28 (flat) |
| workers created / live | 13 / 3 | 6 / 2 | 15 / 7 | 6 / 2 |
| JS heap (after GC) | 45.2 MB | 43.9 MB | 48.4 MB | 44.8 MB |

Before: +12 WebGL contexts and +3 to +4 workers per round trip (browsers cap live contexts around 16, so a user would hit context loss after a few switches); on the LinkView path workers leaked (live 2 to 7 after 3 trips: the old viewer's workers were not all terminated) and the heap crept up. After: contexts do not grow, live workers stay at 2, heap is flat within noise. The extra worker created per trip after the fix is the PDF.js worker of the Documents / LinkView page (created and terminated each time), not the viewer's.
GPU memory itself is not exposed to a headless page; context count is the proxy (each context held the model's buffers).

DOM nodes / listeners still grow slowly over trips in both builds (after: nodes 1,804 to 2,905, listeners 615 to 752 on the Documents path): a small per-visit leak in the Documents / LinkView chrome, much smaller than before and not investigated here.

## "handler is not a function"

The pageerror comes from the fragments worker (`ConnectionHandlers.run`: a message for a handler of a model that is already disposed). After the fix it still appears during the FIRST load (several times, with StrictMode's double mount of the first viewer) and not on round trips: it is first-load noise from the dev double mount tearing down a viewer while messages are in flight, not a consequence of re-mounting on route switches. Not fixed here (library code).

## Caveats

- Numbers are dev-server builds with StrictMode and a software GPU; production has half the mounts but the same one-per-switch pattern was present.
- Camera: because the viewer now survives, LinkView/Documents camera changes persist across routes unless a module restores them (LinkView restores its entry pose on unmount; the first pick after a LinkView trip landed a few pixels off in 1 of 3 trips).
