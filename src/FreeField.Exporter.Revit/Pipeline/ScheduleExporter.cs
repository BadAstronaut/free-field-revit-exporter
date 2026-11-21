using Autodesk.Revit.DB;
using FreeField.Exporter.Core.Model;

namespace FreeField.Exporter.Revit.Pipeline;

/// <summary>ViewSchedule -> DataTable. Rows are bound to elements by matching visible cell text to element parameter strings.</summary>
public static class ScheduleExporter
{
    public static List<(ScheduleEntry, DataTable)> Export(Document doc, HashSet<string> ifcGids, Dictionary<string, ElementRecord> records,
        RecordBuilder rb, Action<string> warn, Dictionary<long, List<string>>? placed = null)
    {
        var res = new List<(ScheduleEntry, DataTable)>();
        foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
        {
            try
            {
                if (s.IsTemplate || s.IsTitleblockRevisionSchedule || s.IsInternalKeynoteSchedule) continue;
                var def = s.Definition;
                if (def.CategoryId == ElementId.InvalidElementId) continue; // multi-category / revision-type
                var fields = new List<ScheduleField>();
                for (int i = 0; i < def.GetFieldCount(); i++) { var f = def.GetField(i); if (!f.IsHidden) fields.Add(f); }
                try { s.RefreshData(); } catch { }
                var body = s.GetTableData().GetSectionData(SectionType.Body);
                int nr = body.NumberOfRows, nc = body.NumberOfColumns;
                var catName = Category.GetCategory(doc, def.CategoryId)?.Name ?? "";
                var table = new DataTable { Name = s.Name, Category = catName };
                for (int c = 0; c < nc; c++)
                {
                    var f = c < fields.Count ? fields[c] : null;
                    table.Columns.Add(new DataColumn
                    {
                        Heading = f?.ColumnHeading ?? "", ParamKey = f == null ? "" : ParamKey(doc, f),
                        FieldType = f != null && f.FieldType == ScheduleFieldType.ElementType ? "type" : "instance",
                    });
                }
                // signatures of scheduled elements
                var sigs = new Dictionary<string, Queue<Element>>();
                foreach (var e in new FilteredElementCollector(doc, s.Id).WhereElementIsNotElementType())
                {
                    var sig = string.Join("\u001f", fields.Take(nc).Select(f => FieldText(doc, e, f)));
                    if (!sigs.TryGetValue(sig, out var q)) sigs[sig] = q = new Queue<Element>();
                    q.Enqueue(e);
                }
                int bound = 0, rowsOut = 0;
                for (int r = 0; r < nr; r++)
                {
                    var cells = new List<string>();
                    for (int c = 0; c < nc; c++) cells.Add(s.GetCellText(SectionType.Body, r, c) ?? "");
                    if (cells.All(string.IsNullOrEmpty)) continue;
                    if (r == 0 && cells.SequenceEqual(table.Columns.Select(c => c.Heading))) continue;   // column heading row
                    var row = new DataRow { Cells = cells };
                    if (sigs.TryGetValue(string.Join("\u001f", cells), out var q) && q.Count > 0)
                    {
                        var e = q.Dequeue();
                        var gid = IfcTools.GlobalIdOf(e);
                        row.Uid = e.UniqueId; row.Gid = gid; bound++;
                        if (ifcGids.Contains(gid) && !records.ContainsKey(gid)) records[gid] = rb.Build(e, gid);
                    }
                    table.Rows.Add(row); rowsOut++;
                }
                warn($"schedule '{s.Name}': {rowsOut} rows, {bound} bound to elements");
                var se = new ScheduleEntry { Id = s.UniqueId, Name = s.Name, Category = catName };
                if (placed != null && placed.TryGetValue(s.Id.Value, out var onSheets)) se.PlacedOnSheetIds.AddRange(onSheets);
                res.Add((se, table));
            }
            catch (Exception ex) { warn($"schedule '{s.Name}' failed: {ex.Message}"); }
        }
        return res;
    }

    static string ParamKey(Document doc, ScheduleField f)
    {
        var id = f.ParameterId;
        if (id.Value < 0 && id.Value != ElementId.InvalidElementId.Value) return ((BuiltInParameter)id.Value).ToString();
        if (doc.GetElement(id) is SharedParameterElement sp) return sp.GuidValue.ToString();
        return f.GetName();
    }

    static string FieldText(Document doc, Element e, ScheduleField f)
    {
        try
        {
            var p = Find(doc, e, f);
            if (p == null) return "";
            return p.AsValueString() ?? p.AsString() ?? "";
        }
        catch { return ""; }
    }

    static Parameter? Find(Document doc, Element e, ScheduleField f)
    {
        Parameter? Look(Element x)
        {
            var id = f.ParameterId;
            if (id.Value < 0 && id.Value != ElementId.InvalidElementId.Value) return x.get_Parameter((BuiltInParameter)id.Value);
            if (doc.GetElement(id) is SharedParameterElement sp) return x.get_Parameter(sp.GuidValue);
            return x.LookupParameter(f.GetName());
        }
        var p = f.FieldType == ScheduleFieldType.ElementType ? null : Look(e);
        if (p == null || !p.HasValue)
        {
            var t = doc.GetElement(e.GetTypeId());
            if (t != null) p = Look(t) ?? p;
        }
        return p;
    }
}
