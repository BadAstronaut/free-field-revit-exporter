using Autodesk.Revit.DB;
using FreeField.Exporter.Core.Geometry;

namespace FreeField.Exporter.Revit.Pipeline;

/// <summary>
/// Cheap plan outline of an element: the outer loop of its largest downward-facing planar face (falls back to the largest
/// upward-facing one). For a rectangular beam/column/footing this is the plan rectangle (oriented along the curve, with the member's
/// width); for slabs it is the footprint. Projected through the page map; capped at <see cref="MaxPoints"/> vertices.
/// </summary>
public static class Outlines
{
    public const int MaxPoints = 64;
    static readonly Options Opts = new() { DetailLevel = ViewDetailLevel.Medium, ComputeReferences = false, IncludeNonVisibleObjects = false };

    public static double[][]? Build(Element e, PageMap map) => Build(e, map, out _);


    /// <summary>Also returns <paramref name="bounds"/>: the page-space XY extent of ALL solid geometry of the element (every edge), i.e. the same extent the IFC geometry has. Null when the element has no solids.</summary>
    public static double[][]? Build(Element e, PageMap map, out BBox2? bounds)
    {
        bounds = null;
        try
        {
            var ge = e.get_Geometry(Opts);
            if (ge == null) return null;
            PlanarFace? best = null; double bestArea = 0; int bestDir = 0;
            double gx0 = double.MaxValue, gy0 = double.MaxValue, gx1 = double.MinValue, gy1 = double.MinValue; bool anyGeo = false;
            foreach (var solid in Solids(ge))
            {
                foreach (Edge ed in solid.Edges)
                {
                    var cv = ed.AsCurve();
                    var pts3 = cv is Line ? new List<XYZ> { cv.GetEndPoint(0), cv.GetEndPoint(1) } : cv.Tessellate();
                    foreach (var q in pts3) { var pp = map.ModelToPage(q); gx0 = Math.Min(gx0, pp.X); gx1 = Math.Max(gx1, pp.X); gy0 = Math.Min(gy0, pp.Y); gy1 = Math.Max(gy1, pp.Y); anyGeo = true; }
                }
                foreach (Face f in solid.Faces)
                {
                    if (f is not PlanarFace pf) continue;
                    double nz = pf.FaceNormal.Z;
                    int dir = nz < -0.98 ? 2 : nz > 0.98 ? 1 : 0;   // prefer bottom faces
                    if (dir == 0) continue;
                    double a = pf.Area;
                    if (dir > bestDir || (dir == bestDir && a > bestArea)) { best = pf; bestArea = a; bestDir = dir; }
                }
            }
            if (anyGeo) bounds = new BBox2(gx0, gy0, gx1, gy1);
            if (best == null) return null;
            var loops = best.GetEdgesAsCurveLoops();
            if (loops.Count == 0) return null;
            // outer loop = the one with the largest extent
            CurveLoop outer = loops[0]; double ext = -1;
            foreach (var l in loops) { var bb = Extent(l); if (bb > ext) { ext = bb; outer = l; } }
            var pts = new List<Point2>();
            foreach (var c in outer)
            {
                var tess = c.IsBound ? c.Tessellate() : new List<XYZ> { c.GetEndPoint(0) };
                for (int i = 0; i < tess.Count - 1; i++) pts.Add(map.ModelToPage(tess[i]));   // drop last = next curve's first
                if (tess.Count == 1) pts.Add(map.ModelToPage(tess[0]));
            }
            if (pts.Count < 3) return null;
            pts = Decimate(pts, MaxPoints);
            return pts.Select(p => new[] { Math.Round(p.X, 2), Math.Round(p.Y, 2) }).ToArray();
        }
        catch { return null; }
    }

    static double Extent(CurveLoop l)
    {
        double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
        foreach (var c in l) { var p = c.GetEndPoint(0); minx = Math.Min(minx, p.X); maxx = Math.Max(maxx, p.X); miny = Math.Min(miny, p.Y); maxy = Math.Max(maxy, p.Y); }
        return (maxx - minx) + (maxy - miny);
    }

    static IEnumerable<Solid> Solids(GeometryElement ge)
    {
        foreach (var o in ge)
        {
            if (o is Solid s && s.Volume > 1e-9 && s.Faces.Size > 0) yield return s;
            else if (o is GeometryInstance gi)
                foreach (var s2 in Solids(gi.GetInstanceGeometry())) yield return s2;
        }
    }

    static List<Point2> Decimate(List<Point2> pts, int max)
    {
        if (pts.Count <= max) return pts;
        var r = new List<Point2>(max);
        for (int i = 0; i < max; i++) r.Add(pts[(int)((long)i * pts.Count / max)]);
        return r;
    }
}
