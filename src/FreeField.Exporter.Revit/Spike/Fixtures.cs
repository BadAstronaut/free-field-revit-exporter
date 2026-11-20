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
        made.AddRange(CreateFixtureSchedules(doc));
        return made;
    }

    static readonly BuiltInParameter[][] FramingFields =
    {
        new[] { BuiltInParameter.ALL_MODEL_MARK },
        new[] { BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM },
        new[] { BuiltInParameter.INSTANCE_LENGTH_PARAM, BuiltInParameter.CURVE_ELEM_LENGTH },
        new[] { BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM },
        new[] { BuiltInParameter.STRUCTURAL_MATERIAL_PARAM },
    };
    static readonly BuiltInParameter[][] ColumnFields =
    {
        new[] { BuiltInParameter.ALL_MODEL_MARK },
        new[] { BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM },
        new[] { BuiltInParameter.FAMILY_BASE_LEVEL_PARAM },
        new[] { BuiltInParameter.FAMILY_TOP_LEVEL_PARAM },
        new[] { BuiltInParameter.STRUCTURAL_MATERIAL_PARAM },
    };

    /// <summary>Deterministic schedules: 'FF Structural Framing Schedule' (placed on sheet FF-101) and 'FF Structural Column Schedule'.</summary>
    static List<string> CreateFixtureSchedules(Document doc)
    {
        var made = new List<string>();
        using var t = new Transaction(doc, "FF fixture schedules");
        t.Start();
        ViewSchedule? Make(string name, BuiltInCategory cat, BuiltInParameter[][] fields)
        {
            var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().FirstOrDefault(s => s.Name == name);
            if (existing != null) return existing;
            var s = ViewSchedule.CreateSchedule(doc, new ElementId(cat));
            s.Name = name;
            var sf = s.Definition.GetSchedulableFields();
            foreach (var alts in fields)
            {
                var sfield = alts.Select(a => sf.FirstOrDefault(x => x.ParameterId.Value == (long)a)).FirstOrDefault(x => x != null);
                if (sfield != null) s.Definition.AddField(sfield);
            }
            made.Add(name + " (" + s.Definition.GetFieldCount() + " fields)");
            return s;
        }
        var framing = Make("FF Structural Framing Schedule", BuiltInCategory.OST_StructuralFraming, FramingFields);
        Make("FF Structural Column Schedule", BuiltInCategory.OST_StructuralColumns, ColumnFields);
        var sheet = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().FirstOrDefault(s => s.SheetNumber == "FF-101");
        if (framing != null && sheet != null && !new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Any())
        {
            ScheduleSheetInstance.Create(doc, sheet.Id, framing.Id, new XYZ(0.05, 0.9, 0));
            made.Add("framing schedule placed on FF-101");
        }
        t.Commit();
        return made;
    }
}
