using System.Text.Json.Serialization;

namespace FreeField.Exporter.Core.Model;

public sealed class BundleManifest
{
    public const string SchemaId = "freefield.bundle/1";
    [JsonPropertyName("schema")] public string Schema { get; set; } = SchemaId;
    [JsonPropertyName("generator")] public GeneratorInfo Generator { get; set; } = new();
    [JsonPropertyName("exportedAt")] public string ExportedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
    [JsonPropertyName("project")] public ProjectInfo Project { get; set; } = new();
    [JsonPropertyName("units")] public UnitsInfo Units { get; set; } = new();
    [JsonPropertyName("coordinates")] public CoordinatesInfo Coordinates { get; set; } = new();
    [JsonPropertyName("models")] public List<ModelEntry> Models { get; set; } = new();
    [JsonPropertyName("sheets")] public List<SheetEntry> Sheets { get; set; } = new();
    [JsonPropertyName("schedules")] public List<ScheduleEntry> Schedules { get; set; } = new();
    [JsonPropertyName("elementsFile")] public string ElementsFile { get; set; } = "data/elements.json";
    [JsonPropertyName("stats")] public BundleStats Stats { get; set; } = new();
}

public sealed class GeneratorInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "FreeField Exporter";
    [JsonPropertyName("version")] public string Version { get; set; } = "0.1.0";
    [JsonPropertyName("host")] public string Host { get; set; } = "";
    [JsonPropertyName("hostVersion")] public string HostVersion { get; set; } = "";
}

public sealed class ProjectInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("number")] public string Number { get; set; } = "";
    [JsonPropertyName("sourceFile")] public string SourceFile { get; set; } = "";
}

public sealed class UnitsInfo
{
    [JsonPropertyName("length")] public string Length { get; set; } = "m";
    [JsonPropertyName("pdf")] public string Pdf { get; set; } = "pt";
}

public sealed class CoordinatesInfo
{
    [JsonPropertyName("modelSpace")] public string ModelSpace { get; set; } = "ifc";
    [JsonPropertyName("ifcPlacement")] public string IfcPlacement { get; set; } = "internal";
    [JsonPropertyName("trueNorthRad")] public double TrueNorthRad { get; set; }
}

public sealed class ModelEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("discipline")] public string Discipline { get; set; } = "";
    [JsonPropertyName("storeys")] public List<StoreyEntry> Storeys { get; set; } = new();
}

public sealed class StoreyEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("elevation")] public double Elevation { get; set; }
    [JsonPropertyName("levelUniqueId")] public string LevelUniqueId { get; set; } = "";
    [JsonPropertyName("ifcGlobalId")] public string IfcGlobalId { get; set; } = "";
}

public sealed class SheetEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("number")] public string Number { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("data")] public string Data { get; set; } = "";
    [JsonPropertyName("page")] public PageSize Page { get; set; } = new();
    [JsonPropertyName("viewports")] public List<ViewportEntry> Viewports { get; set; } = new();
}

public sealed class PageSize
{
    [JsonPropertyName("widthPt")] public double WidthPt { get; set; }
    [JsonPropertyName("heightPt")] public double HeightPt { get; set; }
}

public sealed class ViewportEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("viewId")] public string ViewId { get; set; } = "";
    [JsonPropertyName("viewName")] public string ViewName { get; set; } = "";
    [JsonPropertyName("viewType")] public string ViewType { get; set; } = "";
    [JsonPropertyName("scale")] public double Scale { get; set; }
    [JsonPropertyName("modelId")] public string? ModelId { get; set; }
    [JsonPropertyName("storeyName")] public string? StoreyName { get; set; }
    [JsonPropertyName("primary")] public bool Primary { get; set; }
    [JsonPropertyName("pdfRegion")] public double[] PdfRegion { get; set; } = new double[4];
    /// <summary>Null for non-plan viewports. Always serialized (as null) so the key is present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("pdfToModel")] public double[]? PdfToModel { get; set; }
    [JsonPropertyName("controlPoints")] public List<ControlPoint>? ControlPoints { get; set; }
}

public sealed class ControlPoint
{
    [JsonPropertyName("pdfX")] public double PdfX { get; set; }
    [JsonPropertyName("pdfY")] public double PdfY { get; set; }
    [JsonPropertyName("modelX")] public double ModelX { get; set; }
    [JsonPropertyName("modelY")] public double ModelY { get; set; }
}

public sealed class ScheduleEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
}

public sealed class BundleStats
{
    [JsonPropertyName("elements")] public int Elements { get; set; }
    [JsonPropertyName("sheets")] public int Sheets { get; set; }
    [JsonPropertyName("viewports")] public int Viewports { get; set; }
    [JsonPropertyName("links")] public int Links { get; set; }
}

public sealed class ElementRecord
{
    [JsonPropertyName("uid")] public string Uid { get; set; } = "";
    [JsonPropertyName("eid")] public long Eid { get; set; }
    [JsonPropertyName("gid")] public string Gid { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("bic")] public string Bic { get; set; } = "";
    [JsonPropertyName("family")] public string Family { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("level")] public string Level { get; set; } = "";
    [JsonPropertyName("modelId")] public string ModelId { get; set; } = "";
    [JsonPropertyName("params")] public Dictionary<string, ParamValue> Params { get; set; } = new();
}

public sealed class ParamValue
{
    /// <summary>Raw value: string, double, long or bool.</summary>
    [JsonPropertyName("v")] public object? V { get; set; }
    [JsonPropertyName("d")] public string? D { get; set; }
    [JsonPropertyName("u")] public string? U { get; set; }
    [JsonPropertyName("t")] public bool T { get; set; }
    [JsonPropertyName("k")] public string? K { get; set; }
}

public sealed class SheetElementIndex
{
    [JsonPropertyName("sheetId")] public string SheetId { get; set; } = "";
    [JsonPropertyName("elements")] public List<SheetElement> Elements { get; set; } = new();
    [JsonPropertyName("links")] public List<SheetLink> Links { get; set; } = new();
}

public sealed class SheetElement
{
    [JsonPropertyName("gid")] public string Gid { get; set; } = "";
    [JsonPropertyName("vp")] public string Vp { get; set; } = "";
    [JsonPropertyName("bbox")] public double[] Bbox { get; set; } = new double[4];
    [JsonPropertyName("outline")] public double[][]? Outline { get; set; }
}

public sealed class SheetLink
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("bbox")] public double[] Bbox { get; set; } = new double[4];
    [JsonPropertyName("targetSheetId")] public string? TargetSheetId { get; set; }
    [JsonPropertyName("targetSheetNumber")] public string? TargetSheetNumber { get; set; }
    [JsonPropertyName("targetViewId")] public string? TargetViewId { get; set; }
}

public sealed class DataTable
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("columns")] public List<DataColumn> Columns { get; set; } = new();
    [JsonPropertyName("rows")] public List<DataRow> Rows { get; set; } = new();
}

public sealed class DataColumn
{
    [JsonPropertyName("heading")] public string Heading { get; set; } = "";
    [JsonPropertyName("paramKey")] public string ParamKey { get; set; } = "";
    /// <summary>"instance" or "type".</summary>
    [JsonPropertyName("fieldType")] public string FieldType { get; set; } = "instance";
}

public sealed class DataRow
{
    [JsonPropertyName("gid")] public string Gid { get; set; } = "";
    [JsonPropertyName("uid")] public string Uid { get; set; } = "";
    [JsonPropertyName("cells")] public List<string> Cells { get; set; } = new();
}

/// <summary>
/// Self-sufficient payload embedded in each sheet PDF as the attachment <c>freefield-sheet.json</c>.
/// Consumers read BIM properties from this alone (no bundle data files needed).
/// </summary>
public sealed class EmbeddedSheetPayload
{
    public const string SchemaId = "freefield.sheet/1";
    [JsonPropertyName("schema")] public string Schema { get; set; } = SchemaId;
    [JsonPropertyName("sheetId")] public string SheetId { get; set; } = "";
    [JsonPropertyName("sheetNumber")] public string SheetNumber { get; set; } = "";
    [JsonPropertyName("sheetName")] public string SheetName { get; set; } = "";
    [JsonPropertyName("page")] public PageSize Page { get; set; } = new();
    /// <summary>Viewports of this sheet (id, storeyName, pdfRegion, pdfToModel and the rest of the manifest entry).</summary>
    [JsonPropertyName("viewports")] public List<ViewportEntry> Viewports { get; set; } = new();
    /// <summary>The SheetElementIndex entries (gid, vp, bbox, outline).</summary>
    [JsonPropertyName("elements")] public List<SheetElement> Elements { get; set; } = new();
    /// <summary>ElementRecords referenced by <see cref="Elements"/>, keyed by gid.</summary>
    [JsonPropertyName("elementRecords")] public Dictionary<string, ElementRecord> ElementRecords { get; set; } = new();
    /// <summary>Links, with targetSheetId and targetSheetNumber.</summary>
    [JsonPropertyName("links")] public List<SheetLink> Links { get; set; } = new();
}
