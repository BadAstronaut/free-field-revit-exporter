using Autodesk.Revit.DB;
using FreeField.Exporter.Core.Model;

namespace FreeField.Exporter.Revit.Pipeline;

/// <summary>Builds ElementRecords (instance + type parameters, SI-converted raw values, display strings).</summary>
public sealed class RecordBuilder
{
    const int MaxString = 2000;
    readonly Document _doc;
    readonly string _modelId;
    readonly Dictionary<long, Dictionary<string, ParamValue>> _typeCache = new();

    public RecordBuilder(Document doc, string modelId) { _doc = doc; _modelId = modelId; }

    public ElementRecord Build(Element e, string gid)
    {
        var cat = e.Category;
        var rec = new ElementRecord
        {
            Uid = e.UniqueId, Eid = e.Id.Value, Gid = gid,
            Category = cat?.Name ?? "",
            Bic = cat != null && cat.Id.Value < 0 ? ((BuiltInCategory)cat.Id.Value).ToString() : "",
            ModelId = _modelId,
            Level = LevelName(e),
        };
        var tid = e.GetTypeId();
        Element? type = tid != ElementId.InvalidElementId ? _doc.GetElement(tid) : null;
        if (type is ElementType et) { rec.Family = et.FamilyName ?? ""; rec.Type = et.Name ?? ""; }
        else if (e is Wall w) { rec.Type = w.WallType.Name; rec.Family = w.WallType.FamilyName ?? ""; }
        foreach (var p in e.Parameters.Cast<Parameter>()) Add(rec.Params, p, false);
        if (type != null)
        {
            if (!_typeCache.TryGetValue(tid.Value, out var tp))
            {
                tp = new Dictionary<string, ParamValue>();
                foreach (var p in type.Parameters.Cast<Parameter>()) Add(tp, p, true);
                _typeCache[tid.Value] = tp;
            }
            foreach (var kv in tp)
            {
                var key = rec.Params.ContainsKey(kv.Key) ? kv.Key + " (Type)" : kv.Key;
                rec.Params[key] = kv.Value;
            }
        }
        return rec;
    }

    string LevelName(Element e)
    {
        try
        {
            if (e.LevelId != ElementId.InvalidElementId) return _doc.GetElement(e.LevelId)?.Name ?? "";
            foreach (var bip in new[] { BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM, BuiltInParameter.STAIRS_BASE_LEVEL_PARAM })
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.ElementId) { var n = _doc.GetElement(p.AsElementId())?.Name; if (!string.IsNullOrEmpty(n)) return n!; }
                    else { var s = p.AsString() ?? p.AsValueString(); if (!string.IsNullOrEmpty(s)) return s!; }
                }
            }
        }
        catch { }
        return "";
    }

    public static string KeyOf(Parameter p)
    {
        var d = p.Definition;
        if (d is InternalDefinition idf && idf.BuiltInParameter != BuiltInParameter.INVALID) return idf.BuiltInParameter.ToString();
        if (p.IsShared) { try { return p.GUID.ToString(); } catch { } }
        return d.Name;
    }

    void Add(Dictionary<string, ParamValue> dict, Parameter p, bool isType)
    {
        try
        {
            if (!p.HasValue) return;
            var name = p.Definition.Name;
            if (string.IsNullOrEmpty(name) || dict.ContainsKey(name)) return;
            object? v = null; string? u = null; string? d = null;
            switch (p.StorageType)
            {
                case StorageType.String:
                    var s = p.AsString(); if (string.IsNullOrEmpty(s)) return;
                    v = Cap(s!); d = Cap(s!); break;
                case StorageType.Integer:
                    v = (long)p.AsInteger(); d = p.AsValueString() ?? p.AsInteger().ToString(); break;
                case StorageType.Double:
                    (v, u) = ConvertDouble(p); d = p.AsValueString() ?? p.AsDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture); break;
                case StorageType.ElementId:
                    var id = p.AsElementId(); v = id.Value;
                    d = p.AsValueString(); if (string.IsNullOrEmpty(d)) d = _doc.GetElement(id)?.Name; if (string.IsNullOrEmpty(d) && id == ElementId.InvalidElementId) return;
                    break;
                default: return;
            }
            if (d != null) d = Cap(d);
            dict[name] = new ParamValue { V = v, D = d, U = u, T = isType, K = KeyOf(p) };
        }
        catch { }
    }

    static string Cap(string s) => s.Length > MaxString ? s.Substring(0, MaxString) : s;

    static (object v, string? u) ConvertDouble(Parameter p)
    {
        var raw = p.AsDouble();
        ForgeTypeId spec;
        try { spec = p.Definition.GetDataType(); } catch { return (raw, null); }
        if (spec == SpecTypeId.Length) return (UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.Meters), "m");
        if (spec == SpecTypeId.Area) return (UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.SquareMeters), "m2");
        if (spec == SpecTypeId.Volume) return (UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.CubicMeters), "m3");
        if (spec == SpecTypeId.Angle) return (raw, "rad");
        return (raw, null);
    }
}
