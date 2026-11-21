namespace FreeField.Exporter.Core.Geometry;

/// <summary>
/// Finds the page-space translation that best puts expected outline sample points onto the line segments actually drawn in a PDF.
/// Used as a safety net when Revit does not centre the printed sheet where the API says it will.
/// </summary>
public static class PageShiftSearch
{
    public sealed record Result(double Cover0, double BestCover, double Dx, double Dy);

    sealed class Index
    {
        const double Cell = 6;
        readonly Dictionary<long, List<Seg>> _g = new();
        public Index(IReadOnlyList<Seg> segs)
        {
            foreach (var q in segs)
            {
                var len = Math.Sqrt((q.X1 - q.X0) * (q.X1 - q.X0) + (q.Y1 - q.Y0) * (q.Y1 - q.Y0));
                int n = Math.Max(1, (int)Math.Ceiling(len / (Cell / 2)));
                var seen = new HashSet<long>();
                for (int k = 0; k <= n; k++)
                {
                    double x = q.X0 + (q.X1 - q.X0) * k / n, y = q.Y0 + (q.Y1 - q.Y0) * k / n;
                    for (int i = -1; i <= 1; i++)
                        for (int j = -1; j <= 1; j++)
                        {
                            var key = Key((long)Math.Floor(x / Cell) + i, (long)Math.Floor(y / Cell) + j);
                            if (!seen.Add(key)) continue;
                            if (!_g.TryGetValue(key, out var l)) _g[key] = l = new List<Seg>();
                            l.Add(q);
                        }
                }
            }
        }
        static long Key(long i, long j) => (i << 32) ^ (j & 0xffffffffL);
        public bool On(double x, double y, double tol)
        {
            if (!_g.TryGetValue(Key((long)Math.Floor(x / Cell), (long)Math.Floor(y / Cell)), out var l)) return false;
            foreach (var q in l)
            {
                double dx = q.X1 - q.X0, dy = q.Y1 - q.Y0, d2 = dx * dx + dy * dy;
                double t = d2 > 0 ? ((x - q.X0) * dx + (y - q.Y0) * dy) / d2 : 0; t = Math.Max(0, Math.Min(1, t));
                double ex = x - q.X0 - t * dx, ey = y - q.Y0 - t * dy;
                if (ex * ex + ey * ey <= tol * tol) return true;
            }
            return false;
        }
    }

    static double Cover(Index idx, IReadOnlyList<Point2> pts, double dx, double dy, double tol)
    {
        int hit = 0;
        foreach (var p in pts) if (idx.On(p.X + dx, p.Y + dy, tol)) hit++;
        return pts.Count == 0 ? 0 : (double)hit / pts.Count;
    }

    /// <summary>Searches translations in [-range, range]^2 (coarse then fine). Cover is the fraction of samples within 1.5 pt of a segment.</summary>
    /// <param name="skipIfCover0AtLeast">When the unshifted cover is already at least this, the (expensive) search is skipped and the result has Dx = Dy = 0.</param>
    public static Result Find(IReadOnlyList<Seg> segs, IReadOnlyList<Point2> samples, double range = 300, double skipIfCover0AtLeast = 2)
    {
        var idx = new Index(segs);
        double cover0 = Cover(idx, samples, 0, 0, 1.5);
        if (cover0 >= skipIfCover0AtLeast) return new Result(cover0, cover0, 0, 0);
        double bestC = -1, bdx = 0, bdy = 0;
        for (double dx = -range; dx <= range; dx += 3)
            for (double dy = -range; dy <= range; dy += 3)
            {
                var c = Cover(idx, samples, dx, dy, 3.5);
                if (c > bestC) { bestC = c; bdx = dx; bdy = dy; }
            }
        // fine stage: take the centre of the plateau of best shifts (axis-aligned lines make the optimum a plateau)
        var cand = new List<(double dx, double dy, double c)>();
        double fc = -1;
        for (double dx = bdx - 4; dx <= bdx + 4; dx += 0.5)
            for (double dy = bdy - 4; dy <= bdy + 4; dy += 0.5)
            {
                var c = Cover(idx, samples, dx, dy, 1.5);
                cand.Add((dx, dy, c)); if (c > fc) fc = c;
            }
        var top = cand.Where(t => t.c >= fc - 1e-9).ToList();
        double fx = top.Average(t => t.dx), fy = top.Average(t => t.dy);
        return new Result(cover0, fc, fx, fy);
    }
}
