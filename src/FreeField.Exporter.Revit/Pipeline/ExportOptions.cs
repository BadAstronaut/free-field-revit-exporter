namespace FreeField.Exporter.Revit.Pipeline;

public sealed class ExportOptions
{
    /// <summary>Sheet numbers to export; null = all sheets that carry at least one view viewport.</summary>
    public HashSet<string>? Sheets { get; set; }
    /// <summary>Plan view ids (not on sheets) to export as synthetic VIEW-* pages; null = all unplaced plan views, empty = none.</summary>
    public HashSet<long>? PlanViews { get; set; }
    public bool IncludeSheets { get; set; } = true;
    public bool IncludePlanViewsNotOnSheets { get; set; } = true;
    public bool IncludeSchedules { get; set; } = true;
    public bool IncludeOutlines { get; set; } = true;
    public string IfcVersion { get; set; } = "IFC4RV";   // IFC4RV | IFC2x3CV2
    public string OutputPath { get; set; } = "";         // .ffbundle file or folder
    public string? Discipline { get; set; }
    public string? ModelId { get; set; }
    /// <summary>Titleblock for temporary sheets of unplaced plan views: "none" (default: sheet sized to the view, no clipping) or "largest".</summary>
    public string TempTitleBlock { get; set; } = "none";
}
