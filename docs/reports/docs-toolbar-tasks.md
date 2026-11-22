# Documents toolbar Tasks button + PDF viewer X removal

Branch `architect/docs-toolbar-tasks` in the free-field-bundle worktree. One local commit: 46a75bc (not pushed).

## Change 1 - Tasks in the floating toolbar
- Tasks is a new button right after Outline (checkbox icon, red count badge = tasks on the current page). It appears only while a sheet is open (not in compare). It opens a "Tasks" panel in the same panel slot, so it is mutually exclusive with Documents/Outline/Properties/Elements/Schedules/Compare.
- Panel: In view (current page) / All toggle, Open/Overdue/Mine/All chips, rows with status dot, number, title, relative deadline. Clicking a row marks it selected and jumps the sheet to the task's page (same behaviour as the old rail: the plan marker layer draws the pin on that page). Placing tasks (tap, + marker, compose) is untouched.
- The right-edge rail (TaskMiniPanel, previously mounted by TaskPinDock) is no longer mounted. The component stays exported in @ff/tasks-react, now unused.
- Files: apps/documents/src/DocumentsApp.tsx, TaskPinDock.tsx, new TasksPanelBody.tsx, new hooks/useSheetTasks.ts.
- LinkView: does NOT use the right-edge rail (only Documents mounted TaskMiniPanel), so nothing to change there.

## Change 2 - X removed at the source
- PdfOverlay (packages/ff-viewer-react) lost the onClose prop and the dark x button.
- Only two callers passed onClose:
  - Documents (DocumentsViewerPanel): x cleared the open PDF, returning to the shared 3D surface beneath. No exit kept: Documents has no other close-PDF control now; the 3D model is in the Models module. Picking another sheet still works. FLAG: within Documents you can no longer go back to the bare 3D surface.
  - Settings PDF view (ViewerPanel): x returned to the 3D viewer. Exit kept: clicking an IFC row in the Settings sidebar already does setActivePdfVersionId(null) + closeLevel().
- LinkView and MappingView/calibration never passed onClose, so they never showed the x. Mapping keeps its own Cancel/Save. CompareViewer does not use it either.

## Tests
- apps/documents: 23 files, 212 tests passed (new: TasksPanelBody.test.tsx, Tasks toolbar describe in DocumentsApp.test.tsx, updated TaskPinDockSeam).
- packages/ff-viewer-react: 34 files, 518 passed. packages/ff-viewer: 8 files passed. packages/ff-tasks-react: 38 files passed. apps/linkview: 7 files passed.
- apps/settings tests could not start in this environment (vite.config.ts fails to load: ERR_UNKNOWN_FILE_EXTENSION on ff-tooling vite config). Not caused by this change. Settings ViewerPanel edit (removed one prop and the unused setter) is NOT test-run or typechecked.

## Screenshots (e2e-browser/docs-toolbar/)
a-documents-toolbar-badge.png, b-tasks-panel.png, c-task-focused.png, d-linkview-pdf-pane.png, e0-settings-pdf-view.png. Scripts: docs.mjs, lv.mjs, map.mjs.

## Not verified
- MappingView live (needs a level open to enable Map); verified by code reading only.
- Task row click on a single-page sheet only selects (page already 1); multi-page jump covered by unit test.
