# Status and runbook (2026-11-20)

## Where things stand
| Area | State | Evidence |
|---|---|---|
| Core lib + schema (`freefield.bundle/1`, `freefield.sheet/1`) | Done | 34/34 xUnit |
| Bundle validator | Done | 8/8 tests; PASS on the real STRUCTURAL_CO bundle (6 plan viewports, 410/410 elements in tolerance, det ratio 1.000) |
| Revit add-in (2025 default; 2026/2027 switchable) | Working end to end | headless export of STRUCTURAL_CO in ~24 s; sheet→page mapping proven to ≤0.13 pt; IFC = Revit internal coords ×0.3048 |
| Plan views not on sheets → `VIEW-*` PDFs | Done | 4 pages in the real bundle |
| Outlines, Revit schedules embedded per sheet, dialog smoke | Coded, **not yet run live** | blocked on the unsigned-add-in prompt (see below) |
| FreeField importer (`free-field` branch `architect/ffbundle-import`, worktree `../free-field-bundle`) | Done, not pushed | real bundle: 6 calibrations, 410/410 elements within 0.5 m in FreeField world (median < 1 mm), idempotent re-import; browser walkthrough in Edge passed |
| FreeField drawing data (tap → properties, sheet links + Back, elements table, CSV/XLSX, schedule tabs) | Done | component tests + browser screenshots |
| Rotated calibrations in LinkView | Done | UV-registered overlay tests at 0/25/90/−45/180°; top-down browser check at 25° |

## Blockers needing the user
1. **Unsigned add-in prompt** on every Revit start: run `tools/signing/sign-addins.ps1 -Setup` once (Revit closed, click Yes on the certificate dialog). `install-addin.ps1` signs automatically afterwards. Undo: `-Remove`.
2. **Remotes**: this repo has no GitHub remote (`gh` not installed). The `free-field` branch is unpushed pending approval.

## Runbook
```powershell
# build + install the add-in (Revit closed)
./tools/scripts/install-addin.ps1 -RevitVersion 2025
# headless export (writes out\run-<timestamp>\*.ffbundle; never deletes earlier output)
./tools/scripts/run-export.ps1 -Rvt "$env:LOCALAPPDATA\FreeFieldExporter\work\STRUCTURAL_CO_e2e.rvt" `
  -Options @{createFixtureSheets="true"; fixtureCount="2"; dialogSmoke="true"}
# validate
cd tools/bundle-validator; npm i; npm run build; node dist/cli.js <bundle> --report r.md
```
FreeField: `pnpm --filter settings dev` in `../free-field-bundle` (demo auth), then Settings → PDFs → **Import bundle**.
Opt-in data e2e: `FF_E2E_BUNDLE=<bundle> NODE_OPTIONS="--import tsx" pnpm exec vitest run __tests__/bundle/e2e.realBundle.test.ts` (in `apps/settings`).

## Known limitations / next
- STRUCTURAL_CO has a single native sheet, so plan sheets FF-101/FF-102 are fixtures created in the work copy.
- Element-to-IFC join in FreeField is by GlobalId for geometry checks only; properties come from the PDF attachment.
- The base-model coordinate shift `b` is not persisted in FreeField; adding an IFC that sorts before the base model would shift calibrations (pre-existing FreeField behaviour).
- `@thatopen/components-front` doesn't resolve for 8 settings test suites (pre-existing install drift).
- Production build of FreeField not exercised (dev server only).
