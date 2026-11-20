# Bundle validation: C:\Users\rzlaz\AppData\Local/FreeFieldExporter/out/STRUCTURAL_CO_e2e.ffbundle

Result: **PASS** (0.6 s)

Thresholds: >= 90% of elements within 0.5 m, median < 0.25 m.

## Checks

| Status | Check | Detail |
|---|---|---|
| PASS | manifest matches schema |  |
| PASS | referenced files exist | 1 models, 7 sheets |
| PASS | elements.json matches schema | 203 elements |
| PASS | sheet index files valid |  |
| PASS | stats consistent |  |
| PASS | pdf FF-101 | 1 page 1190.5x841.8 pt |
| PASS | pdf FF-101 embedded json (pdf.js) | 77 elements, 77 records, 1 links |
| PASS | pdf FF-102 | 1 page 1190.5x841.8 pt |
| PASS | pdf FF-102 embedded json (pdf.js) | 68 elements, 68 records, 1 links |
| PASS | pdf S-101 | 1 page 1190.5x841.8 pt |
| PASS | pdf S-101 embedded json (pdf.js) | 135 elements, 93 records, 0 links |
| PASS | pdf VIEW-301_THIRD_FLOOR | 1 page 1404.0x2366.0 pt |
| PASS | pdf VIEW-301_THIRD_FLOOR embedded json (pdf.js) | 74 elements, 74 records, 1 links |
| PASS | pdf VIEW-401_FOURTH_FLOOR | 1 page 1404.0x2366.0 pt |
| PASS | pdf VIEW-401_FOURTH_FLOOR embedded json (pdf.js) | 74 elements, 74 records, 1 links |
| PASS | pdf VIEW-CO-101_FIRST_FLOOR | 1 page 1404.0x2366.0 pt |
| PASS | pdf VIEW-CO-101_FIRST_FLOOR embedded json (pdf.js) | 77 elements, 77 records, 1 links |
| PASS | pdf VIEW-T.O._FOOTING | 1 page 1404.0x2366.0 pt |
| PASS | pdf VIEW-T.O._FOOTING embedded json (pdf.js) | 40 elements, 40 records, 1 links |
| PASS | controlPoints FF-101/101 FIRST FLOOR | 2 points, max deviation 0.00e+0 m |
| PASS | controlPoints FF-102/201 SECOND FLOOR | 2 points, max deviation 0.00e+0 m |
| PASS | controlPoints VIEW-301_THIRD_FLOOR/301 THIRD FLOOR | 2 points, max deviation 0.00e+0 m |
| PASS | controlPoints VIEW-401_FOURTH_FLOOR/401 FOURTH FLOOR | 2 points, max deviation 0.00e+0 m |
| PASS | controlPoints VIEW-CO-101_FIRST_FLOOR/CO-101 FIRST FLOOR | 2 points, max deviation 0.00e+0 m |
| PASS | controlPoints VIEW-T.O._FOOTING/T.O. FOOTING | 2 points, max deviation 0.00e+0 m |
| PASS | model structural_co_e2e storeys | 5 IfcBuildingStorey, schema IFC4 |
| PASS | viewport FF-101/101 FIRST FLOOR storey | 101 FIRST FLOOR |
| PASS | viewport FF-102/201 SECOND FLOOR storey | 201 SECOND FLOOR |
| PASS | viewport VIEW-301_THIRD_FLOOR/301 THIRD FLOOR storey | 301 THIRD FLOOR |
| PASS | viewport VIEW-401_FOURTH_FLOOR/401 FOURTH FLOOR storey | 401 FOURTH FLOOR |
| PASS | viewport VIEW-CO-101_FIRST_FLOOR/CO-101 FIRST FLOOR storey | 101 FIRST FLOOR |
| PASS | viewport VIEW-T.O._FOOTING/T.O. FOOTING storey | T.O. FOOTING |

## Geometric check per plan viewport

| Status | Sheet | Viewport | Storey | Indexed | In IFC | Geometry | Within tol | Fraction | Median (m) | P90 (m) | Max (m) | det ratio | Note |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| PASS | FF-101 | 101 FIRST FLOOR | 101 FIRST FLOOR | 77 | 77 | 77 | 77 | 100.0% | 0.000 | 0.000 | 0.000 | 1.000 |  |
| PASS | FF-102 | 201 SECOND FLOOR | 201 SECOND FLOOR | 68 | 68 | 68 | 68 | 100.0% | 0.000 | 0.000 | 0.000 | 1.000 |  |
| PASS | VIEW-301_THIRD_FLOOR | 301 THIRD FLOOR | 301 THIRD FLOOR | 74 | 74 | 74 | 74 | 100.0% | 0.000 | 0.000 | 0.000 | 1.000 |  |
| PASS | VIEW-401_FOURTH_FLOOR | 401 FOURTH FLOOR | 401 FOURTH FLOOR | 74 | 74 | 74 | 74 | 100.0% | 0.000 | 0.000 | 0.000 | 1.000 |  |
| PASS | VIEW-CO-101_FIRST_FLOOR | CO-101 FIRST FLOOR | 101 FIRST FLOOR | 77 | 77 | 77 | 77 | 100.0% | 0.000 | 0.000 | 0.000 | 1.000 |  |
| PASS | VIEW-T.O._FOOTING | T.O. FOOTING | T.O. FOOTING | 40 | 40 | 40 | 40 | 100.0% | 0.000 | 0.000 | 0.000 | 1.000 |  |

`det ratio` = |det(pdfToModel)| / (viewScale * 0.0254 / 72)^2, expected ~1.
