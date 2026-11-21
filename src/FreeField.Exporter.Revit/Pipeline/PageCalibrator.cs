using FreeField.Exporter.Core;
using FreeField.Exporter.Core.Geometry;
using FreeField.Exporter.Core.Model;
using FreeField.Exporter.Revit.Automation;

namespace FreeField.Exporter.Revit.Pipeline;

/// <summary>
/// Safety net for the sheet-to-page mapping. Revit does not always place the printed sheet where ViewSheet.Outline says (seen on
/// titleblock-less temporary sheets: the page was taller and the content sat at the top). After the geometry is computed, the expected
/// element outlines are compared with the line segments really drawn in the PDF; if they do not coincide but do after a pure translation,
/// every page-space value of that page is translated (regions, pdfToModel, control points, element boxes/outlines, links).
/// </summary>
public static class PageCalibrator
{
    public const double AcceptCover = 0.45;

    public static void Calibrate(string pageNumber, string pdfPath, SheetEntry entry, SheetElementIndex index, Action<string> warn)
    {
        var withOutline = index.Elements.Where(e => e.Outline != null && e.Outline.Length >= 3).ToList();
        if (withOutline.Count < 8) return;
        List<Seg> segs;
        try { segs = PdfLineExtractor.Extract(pdfPath); } catch (Exception ex) { Log.Write($"calibrate {pageNumber}: segment extraction failed: {ex.Message}"); return; }
        if (segs.Count < 50) return;
        var regions = entry.Viewports.ToDictionary(v => v.Id, v => v.PdfRegion);
        var all = new List<Point2>();
        foreach (var e in withOutline)
        {
            regions.TryGetValue(e.Vp, out var rg);
            for (int i = 0; i < e.Outline!.Length; i++)
            {
                var a = e.Outline[i]; var b = e.Outline[(i + 1) % e.Outline.Length];
                foreach (var t in new[] { 0.0, 0.5 })
                {
                    double x = a[0] + (b[0] - a[0]) * t, y = a[1] + (b[1] - a[1]) * t;
                    if (rg != null && (x < rg[0] || x > rg[2] || y < rg[1] || y > rg[3])) continue;
                    all.Add(new Point2(x, y));
                }
            }
        }
        if (all.Count < 60) return;
        int step = Math.Max(1, all.Count / 400);
        var samples = all.Where((_, i) => i % step == 0).ToList();
        var r = PageShiftSearch.Find(segs, samples, 300, AcceptCover);
        if (r.Cover0 >= AcceptCover) return;
        if (r.BestCover >= AcceptCover && r.BestCover >= r.Cover0 + 0.2 && (Math.Abs(r.Dx) > 0.8 || Math.Abs(r.Dy) > 0.8))
        {
            Shift(entry, index, r.Dx, r.Dy);
            warn($"{pageNumber}: page mapping corrected by ({r.Dx:F1}, {r.Dy:F1}) pt (outline cover {r.Cover0:P0} -> {r.BestCover:P0})");
        }
        else Log.Write($"calibrate {pageNumber}: low outline cover {r.Cover0:P0}, no better translation (best {r.BestCover:P0} at {r.Dx:F1},{r.Dy:F1}); kept as exported");
    }

    static void Shift(SheetEntry entry, SheetElementIndex index, double dx, double dy)
    {
        foreach (var v in entry.Viewports)
        {
            v.PdfRegion = new[] { v.PdfRegion[0] + dx, v.PdfRegion[1] + dy, v.PdfRegion[2] + dx, v.PdfRegion[3] + dy };
            if (v.PdfToModel != null)
            {
                var m = Affine2D.Translate(-dx, -dy).Then(Affine2D.FromArray(v.PdfToModel));
                v.PdfToModel = m.ToArray();
                v.ControlPoints = ViewportMath.ControlPointsFor(m, BBox2.FromArray(v.PdfRegion));
            }
        }
        foreach (var e in index.Elements)
        {
            e.Bbox = new[] { e.Bbox[0] + dx, e.Bbox[1] + dy, e.Bbox[2] + dx, e.Bbox[3] + dy };
            if (e.Outline != null) e.Outline = e.Outline.Select(p => new[] { Math.Round(p[0] + dx, 2), Math.Round(p[1] + dy, 2) }).ToArray();
        }
        foreach (var l in index.Links) l.Bbox = new[] { l.Bbox[0] + dx, l.Bbox[1] + dy, l.Bbox[2] + dx, l.Bbox[3] + dy };
    }
}
