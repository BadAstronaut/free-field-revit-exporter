using FreeField.Exporter.Core.Selection;

namespace FreeField.Exporter.Core.Tests;

public class SheetSelectionModelTests
{
    static ViewportInfo Vp(string id, string type, string? level, double area = 100, string? name = null) =>
        new() { ViewId = id, ViewName = name ?? id, ViewType = type, LevelName = level, ModelAreaM2 = area };

    static List<LevelInfo> Levels() => new()
    {
        new() { Name = "L1", ElevationM = 0, StoreyName = "L1" },
        new() { Name = "L2", ElevationM = 3, StoreyName = "L2" },
        new() { Name = "Roof", ElevationM = 9, StoreyName = null, NotExportedReason = "no elements" },
    };

    static SheetSelectionModel Model(SelectionOptions? o = null)
    {
        var sheets = new[]
        {
            new SheetInfo { Id = "s0", Number = "A000", Name = "Cover" },
            new SheetInfo { Id = "s1", Number = "A101", Name = "First Floor", Viewports = { Vp("v1", "FloorPlan", "L1", 500), Vp("v1b", "FloorPlan", "L1", 80, "Enlarged"), Vp("l1", "Legend", null) } },
            new SheetInfo { Id = "s2", Number = "S201", Name = "Structural", Viewports = { Vp("v2", "EngineeringPlan", "L2") } },
            new SheetInfo { Id = "s3", Number = "A301", Name = "Sections", Viewports = { Vp("v3", "Section", null), Vp("v3b", "ThreeD", null) } },
            new SheetInfo { Id = "s4", Number = "A401", Name = "Roof Plan", Viewports = { Vp("v4", "FloorPlan", "Roof") } },
            new SheetInfo { Id = "s5", Number = "A501", Name = "RCP", Viewports = { Vp("v5", "CeilingPlan", "L1"), Vp("v5b", "AreaPlan", "L2") } },
        };
        var views = new[] { Vp("u1", "FloorPlan", "L2"), Vp("u2", "FloorPlan", "Roof"), Vp("u3", "CeilingPlan", "L1"), Vp("u4", "Section", null) };
        return new SheetSelectionModel(sheets, views, Levels(), o);
    }

    [Fact]
    public void Defaults_SelectOnlyMappableSheetsAndViews()
    {
        var m = Model();
        Assert.Equal(new[] { "A101", "S201" }, m.SelectedSheets.Select(s => s.Sheet.Number).ToArray());
        Assert.Equal(new[] { "u1" }, m.SelectedViews.Select(v => v.ViewId).ToArray());
    }

    [Fact]
    public void Statuses()
    {
        var m = Model();
        Assert.Equal(MapStatus.NotMappable, m.Sheets.First(s => s.Sheet.Number == "A000").Status);
        Assert.Equal("Not mappable: no floor plan", m.Sheets.First(s => s.Sheet.Number == "A301").StatusText);
        var roof = m.Sheets.First(s => s.Sheet.Number == "A401");
        Assert.Equal(MapStatus.LevelNotExported, roof.Status);
        Assert.Contains("no elements", roof.StatusText);
        Assert.Equal("Mapped → L1", m.Sheets.First(s => s.Sheet.Number == "A101").StatusText);
        // ceiling / area plans are not mapped by default
        var rcp = m.Sheets.First(s => s.Sheet.Number == "A501");
        Assert.Equal(MapStatus.NotMappable, rcp.Status);
        Assert.All(rcp.Plans, p => Assert.Equal(MapStatus.NotMappable, p.Status));
    }

    [Fact]
    public void CeilingAndAreaPlans_MappedWhenOptionOn()
    {
        var m = Model(new SelectionOptions { MapCeilingAndAreaPlans = true });
        Assert.Equal(MapStatus.Mapped, m.Sheets.First(s => s.Sheet.Number == "A501").Status);
        Assert.Contains("A501", m.SelectedSheets.Select(s => s.Sheet.Number));
        Assert.Contains("u3", m.SelectedViews.Select(v => v.ViewId));
    }

    [Fact]
    public void Primary_IsLargestModelArea_NotPageArea()
    {
        var m = Model();
        var plans = m.Sheets.First(s => s.Sheet.Number == "A101").Plans;
        Assert.True(plans.First(p => p.ViewId == "v1").Primary);
        Assert.False(plans.First(p => p.ViewId == "v1b").Primary);
    }

    [Fact]
    public void IncludeUnmappedSheets_SelectsEverything()
    {
        var m = Model(new SelectionOptions { IncludeUnmappedSheets = true });
        Assert.Equal(6, m.SelectedSheets.Count());
    }

    [Fact]
    public void SelectAllAndClear_AndLevelCoverage()
    {
        var m = Model();
        Assert.True(m.Levels.First(l => l.Name == "L1").HasPlanOnSelection);
        Assert.True(m.Levels.First(l => l.Name == "L2").HasPlanOnSelection);
        Assert.False(m.Levels.First(l => l.Name == "Roof").HasPlanOnSelection);
        m.Clear();
        Assert.False(m.HasSelection);
        Assert.All(m.Levels, l => Assert.False(l.HasPlanOnSelection));
        m.SelectAllMappable();
        Assert.Equal(2, m.SelectedSheets.Count());
    }

    [Fact]
    public void SummaryLine_Counts()
    {
        var m = Model();
        Assert.StartsWith("2 sheets · 2 levels mapped · 1 views not on sheets · est. ", m.SummaryLine());
    }

    [Theory]
    [InlineData("", null, true)]
    [InlineData("first", null, true)]
    [InlineData("a101", null, true)]
    [InlineData("zzz", null, false)]
    [InlineData("", "L1", true)]
    [InlineData("", "L2", false)]
    [InlineData("a101 l1", null, true)]
    public void Matches_SearchAndLevel(string search, string? level, bool expected)
        => Assert.Equal(expected, SheetSelectionModel.Matches(search, level, "A101", "First Floor", new string?[] { "L1" }));
}
