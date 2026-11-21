namespace FreeField.Exporter.Revit.Pipeline;

public sealed class ExportOptions
{
    /// <summary>Sheet numbers to export; null = all sheets that carry at least one view viewport.</summary>
    public HashSet<string>? Sheets { get; set; }
    /// <summary>Plan view ids (not on sheets) to export as synthetic VIEW-* pages; null = all unplaced plan views, empty = none.</summary>
    public HashSet<string>? PlanViews { get; set; }   // view UniqueIds
    /// <summary>Only FloorPlan and EngineeringPlan viewports are mapped unless this is on.</summary>
    public bool MapCeilingAndAreaPlans { get; set; }
    /// <summary>Default selection also includes sheets with nothing mappable (as unmapped documents).</summary>
    public bool IncludeUnmappedSheets { get; set; }
    /// <summary>Polled between stages; when it returns true the export stops with OperationCanceledException.</summary>
    public Func<bool>? Cancel { get; set; }
    public bool IncludeSheets { get; set; } = true;
    public bool IncludePlanViewsNotOnSheets { get; set; } = true;
    /// <summary>Also require non-empty view-specific geometry (Options{View=view}) for an element to be indexed.</summary>
    public bool CheckDrawnGeometry { get; set; } = true;
    public bool IncludeSchedules { get; set; } = true;
    public bool IncludeOutlines { get; set; } = true;
    public string IfcVersion { get; set; } = "IFC4RV";   // IFC4RV | IFC2x3CV2
    public string OutputPath { get; set; } = "";         // .ffbundle file or folder
    public string? Discipline { get; set; }
    public string? ModelId { get; set; }
    /// <summary>Titleblock for temporary sheets of unplaced plan views: "none" (default: sheet sized to the view, no clipping) or "largest".</summary>
    public string TempTitleBlock { get; set; } = "none";
}
