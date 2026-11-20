namespace FreeField.Exporter.Core.Geometry;

/// <summary>2D point.</summary>
public readonly record struct Point2(double X, double Y);

/// <summary>
/// 2x3 affine transform: x' = A*x + B*y + C, y' = D*x + E*y + F.
/// </summary>
public readonly record struct Affine2D(double A, double B, double C, double D, double E, double F)
{
    public static Affine2D Identity => new(1, 0, 0, 0, 1, 0);

    public double Determinant => A * E - B * D;

    public Point2 Apply(Point2 p) => new(A * p.X + B * p.Y + C, D * p.X + E * p.Y + F);

    /// <summary>Returns the transform that applies <c>this</c> first, then <paramref name="next"/>.</summary>
    public Affine2D Then(Affine2D next) => new(
        next.A * A + next.B * D, next.A * B + next.B * E, next.A * C + next.B * F + next.C,
        next.D * A + next.E * D, next.D * B + next.E * E, next.D * C + next.E * F + next.F);

    public Affine2D Invert()
    {
        var det = Determinant;
        if (Math.Abs(det) < 1e-300 || double.IsNaN(det))
            throw new InvalidOperationException("Affine transform is singular.");
        var ia = E / det; var ib = -B / det; var id = -D / det; var ie = A / det;
        return new(ia, ib, -(ia * C + ib * F), id, ie, -(id * C + ie * F));
    }

    public double[] ToArray() => new[] { A, B, C, D, E, F };

    public static Affine2D FromArray(double[] v)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (v.Length != 6) throw new ArgumentException("Affine array must have 6 elements.", nameof(v));
        return new(v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    public static Affine2D Scale(double sx, double sy) => new(sx, 0, 0, 0, sy, 0);
    public static Affine2D Translate(double dx, double dy) => new(1, 0, dx, 0, 1, dy);
    public static Affine2D Rotate(double rad)
    {
        var c = Math.Cos(rad); var s = Math.Sin(rad);
        return new(c, -s, 0, s, c, 0);
    }

    /// <summary>
    /// Exact solve of the affine mapping three non-collinear source points onto three destination points.
    /// </summary>
    public static Affine2D FromPointPairs(Point2 s0, Point2 d0, Point2 s1, Point2 d1, Point2 s2, Point2 d2)
    {
        // Solve [sx sy 1] * [a b c]^T = dx  and same for dy, via Cramer's rule.
        var det = s0.X * (s1.Y - s2.Y) - s0.Y * (s1.X - s2.X) + (s1.X * s2.Y - s2.X * s1.Y);
        if (Math.Abs(det) < 1e-12) throw new ArgumentException("Source points are collinear.");
        (double, double, double) Solve(double v0, double v1, double v2)
        {
            var a = (v0 * (s1.Y - s2.Y) - s0.Y * (v1 - v2) + (v1 * s2.Y - v2 * s1.Y)) / det;
            var b = (s0.X * (v1 - v2) - v0 * (s1.X - s2.X) + (s1.X * v2 - s2.X * v1)) / det;
            var c = (s0.X * (s1.Y * v2 - s2.Y * v1) - s0.Y * (s1.X * v2 - s2.X * v1) + v0 * (s1.X * s2.Y - s2.X * s1.Y)) / det;
            return (a, b, c);
        }
        var (A, B, C) = Solve(d0.X, d1.X, d2.X);
        var (D, E, F) = Solve(d0.Y, d1.Y, d2.Y);
        return new(A, B, C, D, E, F);
    }

    /// <summary>Convenience overload taking three (source, destination) pairs.</summary>
    public static Affine2D FromPointPairs((Point2 Src, Point2 Dst) p0, (Point2 Src, Point2 Dst) p1, (Point2 Src, Point2 Dst) p2)
        => FromPointPairs(p0.Src, p0.Dst, p1.Src, p1.Dst, p2.Src, p2.Dst);
}

/// <summary>Axis-aligned 2D bounding box.</summary>
public readonly record struct BBox2(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public Point2 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public BBox2 Transform(Affine2D t)
    {
        var pts = new[]
        {
            t.Apply(new(MinX, MinY)), t.Apply(new(MaxX, MinY)),
            t.Apply(new(MaxX, MaxY)), t.Apply(new(MinX, MaxY)),
        };
        return new(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
    }

    public BBox2 Union(BBox2 o) => new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));

    public double[] ToArray() => new[] { MinX, MinY, MaxX, MaxY };

    public static BBox2 FromArray(double[] v)
    {
        if (v is null || v.Length != 4) throw new ArgumentException("BBox array must have 4 elements.", nameof(v));
        return new(v[0], v[1], v[2], v[3]);
    }
}
