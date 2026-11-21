using Autodesk.Revit.DB;
using FreeField.Exporter.Core.Selection;

namespace FreeField.Exporter.Revit.Pipeline;

/// <summary>Reads sheets, viewports, unplaced plan views and levels into the pure selection model inputs (one fast pass, UI thread).</summary>
public sealed class Survey
{
    const double M2PerFt2 = 0.09290304;
    public List<SheetInfo> Sheets { get; } = new();
    public List<ViewportInfo> UnplacedViews { get; } = new();
    public List<LevelInfo> Levels { get; } = new();
    public Dictionary<string, ViewSheet> SheetByNumber { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ViewPlan> ViewById { get; } = new();   // keyed by view ElementId value (as string)

    public static Survey Collect(Document doc)
    {
        var s = new Survey();
        foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
            s.Levels.Add(new LevelInfo { Name = l.Name, ElevationM = l.Elevation * PageMap.MetresPerFt, StoreyName = l.Name });

        var placed = new HashSet<long>();
        foreach (var sh in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(x => !x.IsPlaceholder).OrderBy(x => x.SheetNumber))
        {
            var info = new SheetInfo { Id = sh.UniqueId, Number = sh.SheetNumber, Name = sh.Name };
            foreach (var id in sh.GetAllViewports())
            {
                if (doc.GetElement(id) is not Viewport vp || doc.GetElement(vp.ViewId) is not View v) continue;
                placed.Add(v.Id.Value);
                var bo = vp.GetBoxOutline();
                double ft2 = (bo.MaximumPoint.X - bo.MinimumPoint.X) * (bo.MaximumPoint.Y - bo.MinimumPoint.Y);
                info.Viewports.Add(new ViewportInfo
                {
                    ViewId = v.UniqueId, ViewName = v.Name, ViewType = v.ViewType.ToString(),
                    LevelName = (v as ViewPlan)?.GenLevel?.Name,
                    ModelAreaM2 = ft2 * (double)v.Scale * v.Scale * M2PerFt2,
                });
            }
            s.Sheets.Add(info);
            s.SheetByNumber[sh.SheetNumber] = sh;
        }

        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().OrderBy(x => x.Name))
        {
            if (v.IsTemplate || placed.Contains(v.Id.Value) || v.GenLevel == null) continue;
            var t = v.ViewType;
            if (t is not (ViewType.FloorPlan or ViewType.EngineeringPlan or ViewType.CeilingPlan or ViewType.AreaPlan)) continue;
            double area = 0;
            try { if (v.CropBoxActive) { var c = v.CropBox; area = (c.Max.X - c.Min.X) * (c.Max.Y - c.Min.Y) * M2PerFt2; } } catch { }
            s.UnplacedViews.Add(new ViewportInfo { ViewId = v.UniqueId, ViewName = v.Name, ViewType = t.ToString(), LevelName = v.GenLevel.Name, ModelAreaM2 = area });
            s.ViewById[v.UniqueId] = v;
        }
        return s;
    }
}
