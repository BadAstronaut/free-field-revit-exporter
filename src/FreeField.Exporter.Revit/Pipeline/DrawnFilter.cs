using Autodesk.Revit.DB;
using FreeField.Exporter.Core.Geometry;

namespace FreeField.Exporter.Revit.Pipeline;

/// <summary>
/// Decides whether an element that FilteredElementCollector(doc, viewId) returned is actually DRAWN in that view:
/// not hidden in the view / by category, inside the plan view range (view depth .. top clip), and producing non-empty
/// view-specific geometry. Counts why elements were dropped.
/// </summary>
public sealed class DrawnFilter
{
    readonly View _view;
    readonly double _lo = double.NegativeInfinity, _hi = double.PositiveInfinity;
    readonly bool _useRange;
    readonly Options _opts;
    readonly Document _doc;
    readonly List<(HashSet<long> cats, ElementFilter filter)> _hiddenFilters = new();
    public int Hidden, OutOfRange, EmptyGeometry;

    public DrawnFilter(Document doc, View view, bool checkGeometry)
    {
        _view = view;
        _doc = doc;
        try
        {
            foreach (var id in view.GetFilters())
                if (!view.GetFilterVisibility(id) && doc.GetElement(id) is ParameterFilterElement pf) _hiddenFilters.Add((pf.GetCategories().Select(c => c.Value).ToHashSet(), pf.GetElementFilter()));   // incl. filters inherited from the view template
        }
        catch { }
        CheckGeometry = checkGeometry;
        _opts = new Options { View = view, ComputeReferences = false, IncludeNonVisibleObjects = false };
        if (view is ViewPlan vp && view.ViewType != ViewType.CeilingPlan)
        {
            try { (_lo, _hi) = ViewRangeZ(doc, vp); _useRange = !(double.IsNegativeInfinity(_lo) && double.IsPositiveInfinity(_hi)); } catch { }
        }
    }

    public bool CheckGeometry { get; }

    /// <summary>[view depth, top clip] in feet (internal), infinite when unlimited.</summary>
    static (double lo, double hi) ViewRangeZ(Document doc, ViewPlan vp)
    {
        var range = vp.GetViewRange();
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
        var own = vp.GenLevel;
        double Z(PlanViewPlane plane, bool isTop)
        {
            var id = range.GetLevelId(plane);
            double off = range.GetOffset(plane);
            if (id == PlanViewRange.Unlimited) return isTop ? double.PositiveInfinity : double.NegativeInfinity;
            Level? lvl = null;
            if (id == PlanViewRange.LevelAbove) lvl = own == null ? null : levels.FirstOrDefault(l => l.Elevation > own.Elevation + 1e-6);
            else if (id == PlanViewRange.LevelBelow) lvl = own == null ? null : levels.LastOrDefault(l => l.Elevation < own.Elevation - 1e-6);
            else lvl = doc.GetElement(id) as Level;
            if (lvl == null) return isTop ? double.PositiveInfinity : double.NegativeInfinity;
            return lvl.Elevation + off;
        }
        return (Z(PlanViewPlane.ViewDepthPlane, false), Z(PlanViewPlane.TopClipPlane, true));
    }

    public bool IsDrawn(Element e, BoundingBoxXYZ bb)
    {
        try
        {
            if (e.IsHidden(_view)) { Hidden++; return false; }
            var cat = e.Category;
            if (cat != null && _view.GetCategoryHidden(cat.Id)) { Hidden++; return false; }
        }
        catch { }
        foreach (var (cats, filter) in _hiddenFilters)
        {
            try { if (e.Category != null && cats.Contains(e.Category.Id.Value) && (filter == null || filter.PassesFilter(_doc, e.Id))) { Hidden++; return false; } } catch { }
        }
        if (_useRange)
        {
            var t = bb.Transform ?? Transform.Identity;
            double zmin = double.MaxValue, zmax = double.MinValue;
            foreach (var z in new[] { bb.Min.Z, bb.Max.Z })
                foreach (var x in new[] { bb.Min.X, bb.Max.X })
                    foreach (var y in new[] { bb.Min.Y, bb.Max.Y })
                    { var p = t.OfPoint(new XYZ(x, y, z)); zmin = Math.Min(zmin, p.Z); zmax = Math.Max(zmax, p.Z); }
            if (!PlanVisibility.IntersectsViewRange(zmin, zmax, _lo, _hi)) { OutOfRange++; return false; }
        }
        if (CheckGeometry && !HasGeometry(e)) { EmptyGeometry++; return false; }
        return true;
    }

    bool HasGeometry(Element e)
    {
        try
        {
            var ge = e.get_Geometry(_opts);
            return ge != null && Any(ge);
        }
        catch { return true; }   // do not drop on API failure
    }

    static bool Any(GeometryElement ge)
    {
        foreach (var o in ge)
        {
            switch (o)
            {
                case Solid s when s.Faces.Size > 0 || s.Edges.Size > 0: return true;
                case Curve: case PolyLine: case Mesh: return true;
                case GeometryInstance gi:
                    var ig = gi.GetInstanceGeometry();
                    if (ig != null && Any(ig)) return true;
                    break;
            }
        }
        return false;
    }
}
