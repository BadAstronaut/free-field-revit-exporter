using FreeField.Exporter.Core.Geometry;
using FreeField.Exporter.Core.Model;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FreeField.Exporter.Core.Tests;

internal static class TestUtil
{
    public static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FreeField.Exporter.sln"))) dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("repo root not found");
        return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
    }

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ffb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    public static void MakePdf(string path, double wPt, double hPt, int rotate = 0)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(wPt);
        page.Height = XUnit.FromPoint(hPt);
        if (rotate != 0) page.Rotate = rotate;
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawRectangle(XPens.Black, 10, 10, 50, 50);
        doc.Save(path);
    }

    public static BundleManifest SampleManifest(out Dictionary<string, ElementRecord> elements, out List<SheetElementIndex> indexes)
    {
        var m = new BundleManifest
        {
            Generator = { Host = "Authoring tool", HostVersion = "1" },
            Project = { Name = "P", Number = "1", SourceFile = "p.rvt" },
        };
        m.Models.Add(new ModelEntry
        {
            Id = "arch", Name = "Arch", Discipline = "architecture",
            Storeys = { new StoreyEntry { Name = "Level 3", Elevation = 7.2, LevelUniqueId = "lvl-uid", IfcGlobalId = "ifc-lvl" } },
        });
        var pdfToModel = Affine2D.Scale(0.01, -0.01).Then(Affine2D.Translate(5, 50));
        var region = new BBox2(100, 100, 900, 700);
        var sheet = new SheetEntry { Id = "sheet-1", Number = "A/103 x", Name = "Third Floor Plan", Page = { WidthPt = 1000, HeightPt = 800 } };
        sheet.Viewports.Add(new ViewportEntry
        {
            Id = "vp1", ViewId = "view1", ViewName = "Level 3", ViewType = "FloorPlan", Scale = 96, ModelId = "arch",
            StoreyName = "Level 3", Primary = true, PdfRegion = region.ToArray(), PdfToModel = pdfToModel.ToArray(),
            ControlPoints = ViewportMath.ControlPointsFor(pdfToModel, region),
        });
        sheet.Viewports.Add(new ViewportEntry
        {
            Id = "vp2", ViewId = "view2", ViewName = "Section 1", ViewType = "Section", Scale = 48, PdfRegion = new double[] { 0, 0, 10, 10 },
        });
        m.Sheets.Add(sheet);
        elements = new()
        {
            ["gid1"] = new ElementRecord
            {
                Uid = "uid1", Eid = 123, Gid = "gid1", Category = "Doors", Bic = "OST_Doors", Level = "Level 3", ModelId = "arch",
                Params =
                {
                    ["Mark"] = new ParamValue { V = "D1", D = "D1", T = false, K = "ALL_MODEL_MARK" },
                    ["Width"] = new ParamValue { V = 0.9, D = "900 mm", U = "m", T = true, K = "Width" },
                    ["Count"] = new ParamValue { V = 3L, D = "3" },
                    ["Fire"] = new ParamValue { V = true, D = "Yes" },
                },
            },
            ["gid2"] = new ElementRecord { Uid = "uid2", Eid = 124, Gid = "gid2", Category = "Walls", ModelId = "arch" },
        };
        indexes = new()
        {
            new SheetElementIndex
            {
                SheetId = "sheet-1",
                Elements = { new SheetElement { Gid = "gid1", Vp = "vp1", Bbox = new double[] { 120, 130, 140, 150 }, Outline = new[] { new[] { 120.0, 130.0 }, new[] { 140.0, 150.0 } } } },
                Links = { new SheetLink { Kind = "section", Bbox = new double[] { 1, 2, 3, 4 }, TargetSheetId = "sheet-2-target", TargetSheetNumber = "A201" } },
            },
        };
        return m;
    }
}
