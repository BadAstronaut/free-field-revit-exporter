using FreeField.Exporter.Core.Geometry;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FreeField.Exporter.Core.Tests;

public class PageShiftTests
{
    static List<Seg> Grid()
    {
        // irregular (non-periodic) pattern so the best shift is unique
        var rnd = new Random(7); var segs = new List<Seg>();
        for (int i = 0; i < 40; i++)
        {
            double x = 40 + rnd.NextDouble() * 400, y = 40 + rnd.NextDouble() * 300;
            if (i % 2 == 0) segs.Add(new Seg(x, y, x + 20 + rnd.NextDouble() * 120, y));
            else segs.Add(new Seg(x, y, x, y + 20 + rnd.NextDouble() * 100));
        }
        return segs;
    }

    static List<Point2> SamplesOn(List<Seg> segs, double dx, double dy)
    {
        var pts = new List<Point2>();
        foreach (var s in segs) foreach (var t in new[] { 0.1, 0.35, 0.6, 0.85 }) pts.Add(new Point2(s.X0 + (s.X1 - s.X0) * t + dx, s.Y0 + (s.Y1 - s.Y0) * t + dy));
        return pts;
    }

    [Fact]
    public void Find_AlignedSamples_NoShift()
    {
        var segs = Grid();
        var r = PageShiftSearch.Find(segs, SamplesOn(segs, 0, 0));
        Assert.True(r.Cover0 > 0.95);
        Assert.InRange(Math.Abs(r.Dx), 0, 1.0); Assert.InRange(Math.Abs(r.Dy), 0, 1.0);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-37.5, 12)]
    [InlineData(80, -120.5)]
    public void Find_RecoversKnownShift(double sdx, double sdy)
    {
        var segs = Grid();
        // samples were computed with the wrong mapping: they sit at (true - shift); the search must return +shift
        var r = PageShiftSearch.Find(segs, SamplesOn(segs, -sdx, -sdy));
        Assert.True(r.Cover0 < 0.5);
        Assert.True(r.BestCover > 0.9);
        Assert.InRange(Math.Abs(r.Dx - sdx), 0, 1.0); Assert.InRange(Math.Abs(r.Dy - sdy), 0, 1.0);
    }

    [Fact]
    public void Find_SkipsSearchWhenAlreadyAligned()
    {
        var segs = Grid();
        var r = PageShiftSearch.Find(segs, SamplesOn(segs, 0, 0), 300, 0.45);
        Assert.Equal(0, r.Dx); Assert.Equal(0, r.Dy);
        Assert.True(r.Cover0 > 0.9);
    }

    [Fact]
    public void PdfLineExtractor_ReadsRectsAndLines_InTopLeftPageSpace()
    {
        var path = Path.Combine(Path.GetTempPath(), "ffseg-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage(); page.Width = XUnit.FromPoint(400); page.Height = XUnit.FromPoint(300);
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawLine(XPens.Black, 10, 20, 110, 20);          // PdfSharp coordinates are top-left y-down
                gfx.DrawRectangle(XPens.Black, 200, 100, 50, 40);
                doc.Save(path);
            }
            var segs = PdfLineExtractor.Extract(path);
            Assert.Contains(segs, s => Math.Abs(s.X0 - 10) < 0.5 && Math.Abs(s.Y0 - 20) < 0.5 && Math.Abs(s.X1 - 110) < 0.5 && Math.Abs(s.Y1 - 20) < 0.5
                                      || Math.Abs(s.X1 - 10) < 0.5 && Math.Abs(s.Y1 - 20) < 0.5 && Math.Abs(s.X0 - 110) < 0.5 && Math.Abs(s.Y0 - 20) < 0.5);
            Assert.True(segs.Count(s => s.X0 >= 199.5 && s.X0 <= 250.5 && s.Y0 >= 99.5 && s.Y0 <= 140.5) >= 4);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
