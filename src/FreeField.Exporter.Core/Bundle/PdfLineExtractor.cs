using System.Globalization;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FreeField.Exporter.Core;

/// <summary>A straight line segment in page space (points, origin top-left, y down).</summary>
public readonly record struct Seg(double X0, double Y0, double X1, double Y1);

/// <summary>
/// Extracts the straight stroked/filled line segments (moveto/lineto/rect/closepath) of page 1 from the content stream, in page space.
/// Curves, text, images and form XObjects are ignored. A small hand-written tokenizer is used (PDFsharp's object-model content reader
/// is far too slow for multi-megabyte drawing streams). Used to verify / calibrate where the drawing really sits on the page.
/// </summary>
public static class PdfLineExtractor
{
    public static List<Seg> Extract(string pdfPath)
    {
        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        if (doc.PageCount == 0) return new List<Seg>();
        var page = doc.Pages[0];
        var media = page.MediaBox;
        double bx0 = Math.Min(media.X1, media.X2), by1 = Math.Max(media.Y1, media.Y2);
        var crop = page.CropBox;
        if (!crop.IsZero) { bx0 = Math.Max(bx0, Math.Min(crop.X1, crop.X2)); by1 = Math.Min(by1, Math.Max(crop.Y1, crop.Y2)); }
        var content = page.Contents.CreateSingleContent();
        var bytes = content.Stream?.UnfilteredValue ?? Array.Empty<byte>();
        return Parse(bytes, bx0, by1);
    }

    /// <summary>Parses a content stream (already unfiltered). <paramref name="originX"/>/<paramref name="topY"/> map user space to top-left page space.</summary>
    public static List<Seg> Parse(byte[] s, double originX, double topY)
    {
        var segs = new List<Seg>();
        var ctm = new double[] { 1, 0, 0, 1, 0, 0 };
        var stack = new Stack<double[]>();
        var path = new List<Seg>();
        var ops = new double[8]; int nops = 0;
        double cx = 0, cy = 0, sx = 0, sy = 0;

        void Line(double ax, double ay, double bx, double by)
        {
            double px = ctm[0] * ax + ctm[2] * ay + ctm[4], py = ctm[1] * ax + ctm[3] * ay + ctm[5];
            double qx = ctm[0] * bx + ctm[2] * by + ctm[4], qy = ctm[1] * bx + ctm[3] * by + ctm[5];
            path.Add(new Seg(px - originX, topY - py, qx - originX, topY - qy));
        }
        void Flush() { segs.AddRange(path); path.Clear(); }

        int i = 0, n = s.Length;
        static bool Ws(byte b) => b == 32 || b == 10 || b == 13 || b == 9 || b == 12 || b == 0;
        static bool Delim(byte b) => b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']' || b == '{' || b == '}' || b == '/' || b == '%';
        while (i < n)
        {
            byte c = s[i];
            if (Ws(c)) { i++; continue; }
            if (c == '%') { while (i < n && s[i] != 10 && s[i] != 13) i++; continue; }
            if (c == '(') { int depth = 1; i++; while (i < n && depth > 0) { if (s[i] == (byte)92) i++; else if (s[i] == '(') depth++; else if (s[i] == ')') depth--; i++; } nops = 0; continue; }
            if (c == '<') { if (i + 1 < n && s[i + 1] == '<') { i += 2; } else { while (i < n && s[i] != '>') i++; i++; } continue; }
            if (c == '>' || c == '[' || c == ']' || c == '{' || c == '}' || c == ')') { i++; continue; }
            if (c == '/') { i++; while (i < n && !Ws(s[i]) && !Delim(s[i])) i++; nops = 0; continue; }
            int st = i;
            while (i < n && !Ws(s[i]) && !Delim(s[i])) i++;
            var tok = new ReadOnlySpan<byte>(s, st, i - st);
            if ((tok[0] >= '0' && tok[0] <= '9') || tok[0] == '-' || tok[0] == '+' || tok[0] == '.')
            {
                if (double.TryParse(System.Text.Encoding.ASCII.GetString(tok), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    if (nops == ops.Length) { Array.Copy(ops, 1, ops, 0, ops.Length - 1); nops--; }
                    ops[nops++] = v;
                }
                continue;
            }
            // operator
            string op = System.Text.Encoding.ASCII.GetString(tok);
            switch (op)
            {
                case "q": stack.Push((double[])ctm.Clone()); break;
                case "Q": if (stack.Count > 0) ctm = stack.Pop(); break;
                case "cm":
                    if (nops >= 6)
                    {
                        int o = nops - 6; double m0 = ops[o], m1 = ops[o + 1], m2 = ops[o + 2], m3 = ops[o + 3], m4 = ops[o + 4], m5 = ops[o + 5];
                        ctm = new[] { m0 * ctm[0] + m1 * ctm[2], m0 * ctm[1] + m1 * ctm[3], m2 * ctm[0] + m3 * ctm[2], m2 * ctm[1] + m3 * ctm[3], m4 * ctm[0] + m5 * ctm[2] + ctm[4], m4 * ctm[1] + m5 * ctm[3] + ctm[5] };
                    }
                    break;
                case "m": if (nops >= 2) { cx = sx = ops[nops - 2]; cy = sy = ops[nops - 1]; } break;
                case "l": if (nops >= 2) { double x = ops[nops - 2], y = ops[nops - 1]; Line(cx, cy, x, y); cx = x; cy = y; } break;
                case "re":
                    if (nops >= 4)
                    {
                        double x = ops[nops - 4], y = ops[nops - 3], w = ops[nops - 2], h = ops[nops - 1];
                        Line(x, y, x + w, y); Line(x + w, y, x + w, y + h); Line(x + w, y + h, x, y + h); Line(x, y + h, x, y);
                    }
                    break;
                case "h": Line(cx, cy, sx, sy); cx = sx; cy = sy; break;
                case "c": if (nops >= 6) { cx = ops[nops - 2]; cy = ops[nops - 1]; } break;
                case "v": case "y": if (nops >= 4) { cx = ops[nops - 2]; cy = ops[nops - 1]; } break;
                case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": Flush(); break;
                case "n": path.Clear(); break;
                case "BI": // inline image: skip to EI
                    while (i + 1 < n && !(Ws(s[i - 1 < 0 ? 0 : i - 1]) && s[i] == 'E' && s[i + 1] == 'I')) i++;
                    i += 2; break;
            }
            nops = 0;
        }
        return segs;
    }
}
