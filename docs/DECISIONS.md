# Decisions

Append-only log. Format: `## YYYY-MM-DD — title` + context/decision/consequence.

## 2026-11-20 — IFC placement = Revit internal origin
Manifest transforms target IFC world coords. Exporting relative to internal origin keeps Revit internal coords == IFC coords (ft→m) and avoids survey/shared-coordinate ambiguity. Revisit if FreeField needs geo-referencing.

## 2026-11-20 — pdfToModel is a general affine + 2 control points
PDF y-down vs model y-up implies a reflection, which a similarity (scale+rotation) cannot express. The importer fits its own similarity from the control points after converting model XY to its world convention.
