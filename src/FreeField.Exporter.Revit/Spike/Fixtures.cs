using Autodesk.Revit.DB;

namespace FreeField.Exporter.Revit.Spike;

/// <summary>
/// Test fixture (job option createFixtureSheets=true): one sheet 'FF-1xx' per level-bound plan view that is not on a sheet,
/// using the first titleblock type. Deterministic (views ordered by name). Modifies the (work copy) document; never saved by the exporter.
/// </summary>
public static class Fixtures
{
    public static object CreateFixtureSheets(Document doc, int count = int.MaxValue)
    {
        var tb = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().FirstOrDefault();
        var placed = new HashSet<long>(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(v => v.ViewId.Value));
        var made = new List<string>();
        if (new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Any(s => s.SheetNumber.StartsWith("FF-1"))) return new[] { "fixtures already present" };
        using var t = new Transaction(doc, "FF fixture sheets");
        t.Start();
        int n = 100;
        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => !v.IsTemplate && v.GenLevel != null && Pipeline.ExportPipeline.IsPlan(v.ViewType) && !placed.Contains(v.Id.Value)).OrderBy(v => v.Name).Take(count).ToList())
        {
            var sh = ViewSheet.Create(doc, tb?.Id ?? ElementId.InvalidElementId);
            sh.SheetNumber = "FF-" + (++n); sh.Name = v.Name;
            var o = sh.Outline;
            Viewport.Create(doc, sh.Id, v.Id, new XYZ((o.Min.U + o.Max.U) / 2, (o.Min.V + o.Max.V) / 2, 0));
            made.Add(sh.SheetNumber + "=" + v.Name);
        }
        t.Commit();
        return made;
    }
}
