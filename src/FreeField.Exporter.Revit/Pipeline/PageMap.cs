using Autodesk.Revit.DB;
using FreeField.Exporter.Core.Geometry;

namespace FreeField.Exporter.Revit.Pipeline;

/// <summary>
/// Model (ft) -> view projection -> sheet (ft) -> PDF page (pt, origin top-left, y down).
/// Sheet->page (proved in docs/revit-spike.md): the page is the sheet outline at 100% zoom, centred:
///   px = W/2 + (sx - cx) * 864 ;  py = H/2 - (sy - cy) * 864   (864 = 72 pt * 12 in per ft)
/// where (cx, cy) is the centre of ViewSheet.Outline.
/// </summary>
public sealed class PageMap
{
    public const double PtPerFt = 864.0;
    public const double MetresPerFt = 0.3048;

    public Transform ModelToProj { get; }
    public Transform ProjToSheet { get; }
    public double Cx { get; }
    public double Cy { get; }
    public double W { get; }
    public double H { get; }

    public PageMap(Transform m2p, Transform p2s, double cx, double cy, double w, double h)
    { ModelToProj = m2p; ProjToSheet = p2s; Cx = cx; Cy = cy; W = w; H = h; }

    public Point2 SheetToPage(double sx, double sy) => new(W / 2 + (sx - Cx) * PtPerFt, H / 2 - (sy - Cy) * PtPerFt);

    public Point2 ModelToPage(XYZ p)
    {
        var s = ProjToSheet.OfPoint(ModelToProj.OfPoint(p));
        return SheetToPage(s.X, s.Y);
    }

    /// <summary>Page (pt) -> model XY in metres, for plan views (model Z irrelevant). Returns null if singular.</summary>
    public Affine2D? PdfToModelMetres()
    {
        try
        {
            var o = ModelToPage(XYZ.Zero); var x = ModelToPage(new XYZ(1, 0, 0)); var y = ModelToPage(new XYZ(0, 1, 0));
            var toPage = Affine2D.FromPointPairs(new Point2(0, 0), o, new Point2(1, 0), x, new Point2(0, 1), y);
            return toPage.Invert().Then(Affine2D.Scale(MetresPerFt, MetresPerFt));
        }
        catch { return null; }
    }

    /// <summary>Full (unclipped) page bbox of a model-space box (all 8 corners through the chain).</summary>
    public BBox2 ProjectBox(BoundingBoxXYZ bb)
    {
        double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
        var t = bb.Transform ?? Transform.Identity;
        foreach (var cx in new[] { bb.Min.X, bb.Max.X })
            foreach (var cy in new[] { bb.Min.Y, bb.Max.Y })
                foreach (var cz in new[] { bb.Min.Z, bb.Max.Z })
                {
                    var p = ModelToPage(t.OfPoint(new XYZ(cx, cy, cz)));
                    minx = Math.Min(minx, p.X); maxx = Math.Max(maxx, p.X); miny = Math.Min(miny, p.Y); maxy = Math.Max(maxy, p.Y);
                }
        return new BBox2(minx, miny, maxx, maxy);
    }
}
