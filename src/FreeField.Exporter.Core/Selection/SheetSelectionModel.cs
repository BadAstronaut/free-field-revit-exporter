namespace FreeField.Exporter.Core.Selection;

public enum MapStatus { Mapped, NotMappable, LevelNotExported }

/// <summary>A Revit level and the IFC storey it becomes (StoreyName null = not exported to IFC).</summary>
public sealed class LevelInfo
{
    public string Name { get; init; } = "";
    public double ElevationM { get; init; }
    public string? StoreyName { get; init; }
    public string? NotExportedReason { get; init; }
}

public sealed class ViewportInfo
{
    public string ViewId { get; init; } = "";
    public string ViewName { get; init; } = "";
    /// <summary>Revit ViewType name: FloorPlan, EngineeringPlan, CeilingPlan, AreaPlan, Section, ThreeD, Legend, ...</summary>
    public string ViewType { get; init; } = "";
    public string? LevelName { get; init; }
    /// <summary>Model area (m2) the viewport shows = viewport box area on the sheet x scale^2.</summary>
    public double ModelAreaM2 { get; init; }
}

public sealed class SheetInfo
{
    public string Id { get; init; } = "";
    public string Number { get; init; } = "";
    public string Name { get; init; } = "";
    public List<ViewportInfo> Viewports { get; init; } = new();
}

public sealed class SelectionOptions
{
    /// <summary>Also map CeilingPlan and AreaPlan viewports (default: only FloorPlan and EngineeringPlan are mapped).</summary>
    public bool MapCeilingAndAreaPlans { get; init; }
    /// <summary>Include sheets with nothing mappable by default (as unmapped documents).</summary>
    public bool IncludeUnmappedSheets { get; init; }
}

/// <summary>One plan viewport / view with its mapping decision.</summary>
public sealed class PlanRef
{
    public string ViewId { get; init; } = "";
    public string ViewName { get; init; } = "";
    public string? LevelName { get; init; }
    public string? StoreyName { get; init; }
    public MapStatus Status { get; init; }
    public string StatusText { get; init; } = "";
    public bool Primary { get; set; }
    public double ModelAreaM2 { get; init; }
}

public sealed class SheetRow
{
    public SheetInfo Sheet { get; init; } = new();
    public List<PlanRef> Plans { get; init; } = new();
    public MapStatus Status { get; init; }
    public string StatusText { get; init; } = "";
    public bool Selected { get; set; }
    public bool IsMappable => Status == MapStatus.Mapped;
    public string PlansText => string.Join(", ", Plans.Select(p => p.ViewName + (p.Primary && Plans.Count(q => q.LevelName == p.LevelName) > 1 ? " (primary)" : "")));
    public string LevelsText => string.Join(", ", Plans.Where(p => p.StoreyName != null).Select(p => p.LevelName + " → " + p.StoreyName).Distinct());
}

public sealed class ViewRow
{
    public string ViewId { get; init; } = "";
    public string ViewName { get; init; } = "";
    public string ViewType { get; init; } = "";
    public string? LevelName { get; init; }
    public string? StoreyName { get; init; }
    public MapStatus Status { get; init; }
    public string StatusText { get; init; } = "";
    public bool Selected { get; set; }
    public double ModelAreaM2 { get; init; }
}

public sealed class LevelRow
{
    public string Name { get; init; } = "";
    public string? StoreyName { get; init; }
    public double ElevationM { get; init; }
    public bool HasPlanOnSelection { get; set; }
    public string? NotExportedReason { get; init; }
}

public sealed record SelectionSummary(int Sheets, int Levels, int Views, int MappedLevels, double EstimatedMb);

/// <summary>
/// Pure selection logic shared by the export dialog and headless jobs: which sheets / unplaced views are mappable, their
/// statuses, the default selection and the summary line. No Revit types.
/// </summary>
public sealed class SheetSelectionModel
{
    public IReadOnlyList<SheetRow> Sheets { get; }
    public IReadOnlyList<ViewRow> Views { get; }
    public IReadOnlyList<LevelRow> Levels { get; }
    public SelectionOptions Options { get; }

    public static bool IsMappedType(string viewType, SelectionOptions o) =>
        viewType is "FloorPlan" or "EngineeringPlan" || (o.MapCeilingAndAreaPlans && viewType is "CeilingPlan" or "AreaPlan");

    static bool IsPlanType(string t) => t is "FloorPlan" or "EngineeringPlan" or "CeilingPlan" or "AreaPlan";

    public SheetSelectionModel(IEnumerable<SheetInfo> sheets, IEnumerable<ViewportInfo> unplacedPlanViews, IEnumerable<LevelInfo> levels, SelectionOptions? options = null)
    {
        Options = options ?? new SelectionOptions();
        var lv = levels.ToList();
        var byLevel = lv.ToDictionary(l => l.Name, StringComparer.Ordinal);

        (MapStatus, string, string?) Classify(string viewType, string? level)
        {
            if (!IsMappedType(viewType, Options))
                return (MapStatus.NotMappable, IsPlanType(viewType) ? "Not mappable: " + viewType + " (only floor plans are mapped)" : "Not mappable: no floor plan", null);
            if (level == null || !byLevel.TryGetValue(level, out var l)) return (MapStatus.LevelNotExported, "Level not exported to IFC: no level", null);
            if (l.StoreyName == null) return (MapStatus.LevelNotExported, "Level not exported to IFC" + (string.IsNullOrEmpty(l.NotExportedReason) ? "" : ": " + l.NotExportedReason), null);
            return (MapStatus.Mapped, "Mapped → " + l.StoreyName, l.StoreyName);
        }

        var rows = new List<SheetRow>();
        foreach (var s in sheets)
        {
            var plans = new List<PlanRef>();
            foreach (var vp in s.Viewports.Where(v => IsPlanType(v.ViewType)))
            {
                var (st, txt, storey) = Classify(vp.ViewType, vp.LevelName);
                plans.Add(new PlanRef { ViewId = vp.ViewId, ViewName = vp.ViewName, LevelName = vp.LevelName, StoreyName = storey, Status = st, StatusText = txt, ModelAreaM2 = vp.ModelAreaM2 });
            }
            // primary per level: the viewport covering the largest model area (not the largest page area)
            foreach (var g in plans.Where(p => p.Status == MapStatus.Mapped).GroupBy(p => p.LevelName))
                g.OrderByDescending(p => p.ModelAreaM2).First().Primary = true;
            MapStatus status; string text;
            var mapped = plans.Where(p => p.Status == MapStatus.Mapped).ToList();
            if (mapped.Count > 0)
            {
                status = MapStatus.Mapped;
                text = "Mapped → " + string.Join(", ", mapped.Select(p => p.StoreyName).Distinct());
            }
            else if (plans.Any(p => p.Status == MapStatus.LevelNotExported))
            {
                status = MapStatus.LevelNotExported;
                text = plans.First(p => p.Status == MapStatus.LevelNotExported).StatusText;
            }
            else { status = MapStatus.NotMappable; text = "Not mappable: no floor plan"; }
            rows.Add(new SheetRow { Sheet = s, Plans = plans, Status = status, StatusText = text, Selected = status == MapStatus.Mapped || Options.IncludeUnmappedSheets });
        }
        Sheets = rows.OrderBy(r => r.Sheet.Number, StringComparer.OrdinalIgnoreCase).ToList();

        var vrows = new List<ViewRow>();
        foreach (var v in unplacedPlanViews.Where(v => IsPlanType(v.ViewType)))
        {
            var (st, txt, storey) = Classify(v.ViewType, v.LevelName);
            vrows.Add(new ViewRow { ViewId = v.ViewId, ViewName = v.ViewName, ViewType = v.ViewType, LevelName = v.LevelName, StoreyName = storey, Status = st, StatusText = txt, Selected = st == MapStatus.Mapped, ModelAreaM2 = v.ModelAreaM2 });
        }
        Views = vrows.OrderBy(r => r.ViewName, StringComparer.OrdinalIgnoreCase).ToList();

        Levels = lv.OrderBy(l => l.ElevationM).Select(l => new LevelRow { Name = l.Name, StoreyName = l.StoreyName, ElevationM = l.ElevationM, NotExportedReason = l.NotExportedReason }).ToList();
        RefreshLevels();
    }

    /// <summary>Recomputes "has a floor plan on a selected sheet/view" for every level.</summary>
    public void RefreshLevels()
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in Sheets.Where(s => s.Selected))
            foreach (var p in s.Plans.Where(p => p.Status == MapStatus.Mapped && p.LevelName != null)) covered.Add(p.LevelName!);
        foreach (var v in Views.Where(v => v.Selected && v.Status == MapStatus.Mapped && v.LevelName != null)) covered.Add(v.LevelName!);
        foreach (var l in Levels) l.HasPlanOnSelection = covered.Contains(l.Name);
    }

    public void SelectAllMappable()
    {
        foreach (var s in Sheets) s.Selected = s.IsMappable;
        foreach (var v in Views) v.Selected = v.Status == MapStatus.Mapped;
        RefreshLevels();
    }

    public void Clear()
    {
        foreach (var s in Sheets) s.Selected = false;
        foreach (var v in Views) v.Selected = false;
        RefreshLevels();
    }

    public IEnumerable<SheetRow> SelectedSheets => Sheets.Where(s => s.Selected);
    public IEnumerable<ViewRow> SelectedViews => Views.Where(v => v.Selected);
    public bool HasSelection => Sheets.Any(s => s.Selected) || Views.Any(v => v.Selected);

    /// <summary>Row filter used by the dialog's search box and level dropdown (null/empty = no filter).</summary>
    public static bool Matches(string search, string? level, string number, string name, IEnumerable<string?> levels)
    {
        var ls = levels.Where(x => x != null).Select(x => x!).ToList();
        if (!string.IsNullOrEmpty(level) && !ls.Contains(level, StringComparer.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(search)) return true;
        foreach (var term in search.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            bool hit = number.Contains(term, StringComparison.OrdinalIgnoreCase) || name.Contains(term, StringComparison.OrdinalIgnoreCase)
                       || ls.Any(l => l.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (!hit) return false;
        }
        return true;
    }

    /// <summary>Rough bundle size: ~0.65 MB per exported page (vector PDFs, calibrated on Snowdon Towers: 66 pages + 98 MB rvt = 61.5 MB bundle) plus the zipped IFC (about <paramref name="ifcMbPerRvtMb"/> x the rvt size).</summary>
    public SelectionSummary Summary(double rvtMb = 0, double ifcMbPerRvtMb = 0.16)
    {
        var pages = Sheets.Count(s => s.Selected) + Views.Count(v => v.Selected);
        var mappedLevels = Levels.Count(l => l.HasPlanOnSelection && l.StoreyName != null);
        return new SelectionSummary(Sheets.Count(s => s.Selected), mappedLevels, Views.Count(v => v.Selected), mappedLevels, Math.Round(pages * 0.65 + rvtMb * ifcMbPerRvtMb, 1));
    }

    public string SummaryLine(double rvtMb = 0)
    {
        var s = Summary(rvtMb);
        return $"{s.Sheets} sheets · {s.Levels} levels mapped · {s.Views} views not on sheets · est. {s.EstimatedMb:0.#} MB";
    }
}
