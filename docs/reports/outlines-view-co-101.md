# Outline mismatch on VIEW-CO-101_FIRST_FLOOR (offline investigation)

Bundle: `run-20261120-232643/STRUCTURAL_CO_e2e.ffbundle` (extracted offline, Revit not launched). Tools: pdf.js operator list via
`tools/scripts/pdf-vertices.mjs`, payload `data/sheets/*.json` + `data/elements.json`.

## Verdict
Not an exporter bug. Two causes:
1. **Verifier artifact (partly):** vertex-on-vertex matching under-counts, because Revit merges collinear edges into long polylines
   (no vertex at a beam's interior corner). 
2. **Legitimately different graphics:** the CO-101 view simply does not draw some of the outlined elements as linework.
   The page mapping is correct (columns 80/80 vertex and 240/240 edge samples on linework; bbox validator 100 %).

## Evidence (in-page elements; "vertex" = outline vertex on PDF vertex, "edge" = 4 samples per outline edge within 1.5 pt of a PDF line segment)

| category (CO-101) | elems | vertex hits | edge samples on linework |
|---|---|---|---|
| Structural columns 12x24 | 20 | 80/80 | 240/240 |
| Structural framing (beams) 12x18 | 30 | 71/120 | 180/360 (all 15 horizontal beams' end faces at columns hit; long edges not drawn) |
| Structural foundation (footings, level T.O. FOOTING) | 20 | 33/86 (coincidental: grid dashes / column edges) | 99/240 |
| Edge slab "Thickened 24x12" | 6 | 14/24 | 22/72 |

* Miss distance distribution (112 missed vertices): quartiles 1.5 / 3.6 / 13.0 / 46.7 pt, max 48 pt. Footings: bimodal, 36.3 pt (96" footing) and 47.4 pt
  (126" footing) = half the footing width = the footing outline corners have no PDF vertex anywhere near; beams: 2.0-4.8 pt = the half column
  depth, i.e. the PDF has no vertex at the beam end because the beam edge is not drawn; edge slab 13.4 pt.
  Not a systematic offset: the only systematic term is a 0.1-0.8 pt vertical drift (scale error ~0.07 %) that is inside tolerance, and it is the same in other views.
* In the PDF the vertical beam edges at x=379.8 / 388.9 have no segment at all between the column rectangles (only ~1 pt ticks at column faces and one
  dash chain along the grid centrelines x=384.1); horizontal beams likewise have only the column-width segments (e.g. `393.1,284.4 -> 375.1,284.4`).
  So in CO-101 beams, footings and slab edge are not drawn with their edges (below/above the view range, hidden-line/dashed or centreline-only);
  the exported outline is the model footprint of the element, which is exactly what the spec defines.
* FF-101 shows the *same level* through a different view whose graphics draw them: footings 58/70, beams 76/84 (edge cover 93 %).
  Operator count also differs: CO-101 1084 paths, FF-101 3102 paths; CO-101 has no filled/hatched items for them.
* All other plan pages: edge cover 97-100 %.

Diagnostic image (outline overlay: red footings, green beams, blue columns, magenta slab edges; grey dots = PDF vertices):
`C:/Users/rzlaz/AppData/Local/Temp/claude/eb7d94fc88555600/9eb0d2c6-ee9f-4574-8500-5e9b0e3ab70f/scratchpad/outline-inv/co_full.png`
(zoom: `co_zoom.png` in the same folder). Footing rectangles carry no PDF vertices at all.

## Changes
* `tools/scripts/pdf-vertices.mjs`: `vertices()` now also returns `segs` (straight line segments in page space).
* `tools/scripts/verify-bundle-pdf.mjs`: adds "edge cover" (outline edge samples vs PDF line segments) next to vertex hits.
  New numbers: FF-101 93 %, FF-102 97 %, VIEW-301/401/T.O.FOOTING 100 %, VIEW-CO-101 65 %.

## Not determined offline / follow-up
The exact Revit reason (view range cut/bottom, "Hidden"/"Underlay" display, a view template or filter hiding Structural Framing/Foundation, or a
Coarse detail level) needs the `.rvt`; it was not checked because Revit must not be launched. Quick check when allowed: view CO-101 -> View Range,
Visibility/Graphics overrides for Structural Framing / Foundations / Edge slabs, Underlay. Optional exporter improvement (not done, not needed for
correctness): skip emitting `outline` for elements the view does not display, or flag them (`visible:false`) using `Document.GetElement(viewId)` +
`FilteredElementCollector(doc, viewId)` membership, which already scopes the indexed elements; the elements are indexed because they are in the view's
collector but hidden-by-range elements would need `Element.IsHidden(view)` / cut-plane tests.
