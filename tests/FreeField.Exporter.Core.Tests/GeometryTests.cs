using FreeField.Exporter.Core.Geometry;

namespace FreeField.Exporter.Core.Tests;

public class GeometryTests
{
    private static void Near(Point2 a, Point2 b, double tol = 1e-9)
    {
        Assert.InRange(Math.Abs(a.X - b.X), 0, tol);
        Assert.InRange(Math.Abs(a.Y - b.Y), 0, tol);
    }

    [Fact]
    public void Identity_Apply() => Near(new Point2(3, 4), Affine2D.Identity.Apply(new Point2(3, 4)));

    [Fact]
    public void Then_AppliesThisFirst()
    {
        var t = Affine2D.Scale(2, 3).Then(Affine2D.Translate(10, 20));
        Near(new Point2(12, 26), t.Apply(new Point2(1, 2)));
        var u = Affine2D.Translate(10, 20).Then(Affine2D.Scale(2, 3));
        Near(new Point2(22, 66), u.Apply(new Point2(1, 2)));
    }

    [Fact]
    public void Invert_RoundTrips()
    {
        var t = Affine2D.Rotate(0.7).Then(Affine2D.Scale(3, -2)).Then(Affine2D.Translate(-4, 9));
        var p = new Point2(12.5, -7.25);
        Near(p, t.Invert().Apply(t.Apply(p)));
        var id = t.Then(t.Invert());
        Assert.InRange(Math.Abs(id.A - 1) + Math.Abs(id.E - 1) + Math.Abs(id.B) + Math.Abs(id.D) + Math.Abs(id.C) + Math.Abs(id.F), 0, 1e-9);
    }

    [Fact]
    public void Invert_Singular_Throws() => Assert.Throws<InvalidOperationException>(() => new Affine2D(1, 2, 0, 2, 4, 0).Invert());

    [Fact]
    public void Reflection_PdfYDownToModelYUp_HasNegativeDeterminant()
    {
        // 1 pt = 1/72 in; scale 1:100 -> model metres per pt = 100 * 0.0254 / 72. y flipped, page height 800.
        var k = 100 * 0.0254 / 72;
        var t = Affine2D.Translate(0, -800).Then(Affine2D.Scale(k, -k)); // y_model = -k*(py-800)
        Assert.True(t.Determinant < 0);
        Near(new Point2(0, 800 * k), t.Apply(new Point2(0, 0)));   // page top-left -> highest model y
        Near(new Point2(0, 0), t.Apply(new Point2(0, 800)));       // page bottom-left -> y = 0
        Assert.Equal(-k * k, t.Determinant, 12);
    }

    [Fact]
    public void ArrayRoundTrip()
    {
        var t = new Affine2D(1, 2, 3, 4, 5, 6);
        Assert.Equal(t, Affine2D.FromArray(t.ToArray()));
        Assert.Throws<ArgumentException>(() => Affine2D.FromArray(new double[5]));
    }

    [Fact]
    public void FromPointPairs_ExactSolve()
    {
        var truth = Affine2D.Rotate(0.3).Then(Affine2D.Scale(0.5, -0.5)).Then(Affine2D.Translate(12, -3));
        var s = new[] { new Point2(0, 0), new Point2(100, 10), new Point2(20, 90) };
        var t = Affine2D.FromPointPairs(s[0], truth.Apply(s[0]), s[1], truth.Apply(s[1]), s[2], truth.Apply(s[2]));
        foreach (var v in truth.ToArray().Zip(t.ToArray())) Assert.Equal(v.First, v.Second, 9);
        var t2 = Affine2D.FromPointPairs((s[0], truth.Apply(s[0])), (s[1], truth.Apply(s[1])), (s[2], truth.Apply(s[2])));
        Assert.Equal(t, t2);
    }

    [Fact]
    public void FromPointPairs_Collinear_Throws() =>
        Assert.Throws<ArgumentException>(() => Affine2D.FromPointPairs(new(0, 0), new(0, 0), new(1, 1), new(1, 1), new(2, 2), new(2, 2)));

    [Fact]
    public void BBox_Transform_AndUnion()
    {
        var b = new BBox2(0, 0, 10, 20);
        var r = b.Transform(Affine2D.Rotate(Math.PI / 2));
        Assert.Equal(-20, r.MinX, 9); Assert.Equal(0, r.MaxX, 9);
        Assert.Equal(0, r.MinY, 9); Assert.Equal(10, r.MaxY, 9);
        var flip = b.Transform(Affine2D.Scale(1, -1));
        Assert.Equal(new BBox2(0, -20, 10, 0), flip);
        Assert.Equal(new BBox2(-5, 0, 10, 30), b.Union(new BBox2(-5, 25, 0, 30)));
        Assert.Equal(new[] { 0.0, 0, 10, 20 }, b.ToArray());
    }

    [Fact]
    public void ControlPointsFor_AreExactUnderAffine()
    {
        var t = Affine2D.Scale(0.0343, -0.0343).Then(Affine2D.Rotate(0.4)).Then(Affine2D.Translate(120.5, -33.25));
        var region = new BBox2(50, 60, 2000, 1500);
        var cps = ViewportMath.ControlPointsFor(t, region);
        Assert.Equal(2, cps.Count);
        Assert.Equal((50.0, 60.0), (cps[0].PdfX, cps[0].PdfY));
        Assert.Equal((2000.0, 1500.0), (cps[1].PdfX, cps[1].PdfY));
        foreach (var c in cps)
        {
            var m = t.Apply(new Point2(c.PdfX, c.PdfY));
            Assert.Equal(m.X, c.ModelX, 12);
            Assert.Equal(m.Y, c.ModelY, 12);
        }
    }
}

public class PlanVisibilityTests
{
    [Theory]
    [InlineData(-1.5, 0.0, 0.0, 10.0, true)]      // beam top touches the view depth: drawn
    [InlineData(-5.5, -4.0, 0.0, 10.0, false)]    // footing entirely below view depth
    [InlineData(-4.0, 28.0, 0.0, 10.0, true)]     // column spanning the range
    [InlineData(11.0, 12.0, 0.0, 10.0, false)]    // entirely above the top clip plane
    [InlineData(-1.5, -0.1, 0.0, 10.0, false)]    // just below
    public void IntersectsViewRange_Cases(double zMin, double zMax, double lo, double hi, bool expected)
        => Assert.Equal(expected, FreeField.Exporter.Core.Geometry.PlanVisibility.IntersectsViewRange(zMin, zMax, lo, hi));

    [Fact]
    public void IntersectsViewRange_UnlimitedAndSwapped()
    {
        Assert.True(FreeField.Exporter.Core.Geometry.PlanVisibility.IntersectsViewRange(-100, -50, double.NegativeInfinity, 5));
        Assert.True(FreeField.Exporter.Core.Geometry.PlanVisibility.IntersectsViewRange(3, -3, 2, 10)); // swapped element args
        Assert.True(FreeField.Exporter.Core.Geometry.PlanVisibility.IntersectsViewRange(0, 1, 5, -5));  // swapped range args
    }
}
