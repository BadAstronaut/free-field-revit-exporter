using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using FreeField.Exporter.Revit.Automation;

namespace FreeField.Exporter.Revit.Pipeline;

public sealed class IfcInfo
{
    public string Path { get; set; } = "";
    public HashSet<string> GlobalIds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> StoreyGuidByName { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> StoreyElevationFt { get; } = new(StringComparer.Ordinal);
}

public static class IfcTools
{
    /// <summary>
    /// IFC export. Needs a Transaction (the exporter stores IFC GUIDs / may touch parameters); it is rolled back.
    /// Options: SitePlacement=Internal (IFC world == Revit internal origin, lengths in file unit FOOT = 0.3048 m),
    /// StoreIFCGUID=true. Must NOT be called while another transaction is open.
    /// </summary>
    public static bool Export(Document doc, string folder, string name, string version, out string error)
    {
        error = "";
        var opt = new IFCExportOptions
        {
            FileVersion = version.StartsWith("IFC2", StringComparison.OrdinalIgnoreCase) ? IFCVersion.IFC2x3CV2 : IFCVersion.IFC4RV,
            ExportBaseQuantities = true,
            WallAndColumnSplitting = false,
            SpaceBoundaryLevel = 0,
            FilterViewId = ElementId.InvalidElementId,
        };
        opt.AddOption("SitePlacement", "Internal");
        opt.AddOption("StoreIFCGUID", "true");
        opt.AddOption("ExportInternalRevitPropertySets", "false");
        using var t = new Transaction(doc, "FreeField IFC export");
        t.Start();
        try { return doc.Export(folder, name, opt); }
        catch (Exception ex) { error = ex.Message; return false; }
        finally { t.RollBack(); }
    }

    static readonly Regex Guid22 = new(@"^#\d+\s*=\s*(IFC\w+)\('([0-9A-Za-z_$]{22})'(.*)$", RegexOptions.Compiled);
    static readonly Regex StoreyRx = new(@"^#\d+\s*=\s*IFCBUILDINGSTOREY\('([0-9A-Za-z_$]{22})',[^,]*,(?:'((?:[^']|'')*)'|\$)", RegexOptions.Compiled);

    /// <summary>Scans the IFC text once: all GlobalIds and the storey name -> GlobalId map.</summary>
    public static IfcInfo Scan(string path)
    {
        var info = new IfcInfo { Path = path };
        foreach (var line in File.ReadLines(path, System.Text.Encoding.Latin1))
        {
            if (line.Length < 30 || line[0] != '#') continue;
            var m = Guid22.Match(line);
            if (!m.Success) continue;
            info.GlobalIds.Add(m.Groups[2].Value);
            if (m.Groups[1].Value == "IFCBUILDINGSTOREY")
            {
                var s = StoreyRx.Match(line);
                if (s.Success && s.Groups[2].Success) info.StoreyGuidByName[Decode(s.Groups[2].Value.Replace("''", "'"))] = s.Groups[1].Value;
            }
        }
        return info;
    }

    /// <summary>Decodes IFC \X2\...\X0\ and \X\ escapes.</summary>
    static string Decode(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        s = Regex.Replace(s, @"\\X2\\([0-9A-Fa-f]+)\\X0\\", m =>
        {
            var h = m.Groups[1].Value; var sb = new System.Text.StringBuilder();
            for (int i = 0; i + 4 <= h.Length; i += 4) sb.Append((char)Convert.ToInt32(h.Substring(i, 4), 16));
            return sb.ToString();
        });
        s = Regex.Replace(s, @"\\X\\([0-9A-Fa-f]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        return s;
    }

    /// <summary>IFC GlobalId of an element: the stored IfcGUID parameter (after export with StoreIFCGUID) else the deterministic alternate GUID.</summary>
    public static string GlobalIdOf(Element e)
    {
        try
        {
            var p = e.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString();
            if (!string.IsNullOrEmpty(p)) return p!;
        }
        catch { }
        try { return Autodesk.Revit.DB.IFC.ExporterIFCUtils.CreateAlternateGUID(e); } catch { return ""; }
    }
}
