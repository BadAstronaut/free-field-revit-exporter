using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Advanced;

namespace FreeField.Exporter.Core;

public sealed record PdfPageInfo(int PageCount, double WidthPt, double HeightPt);

public static class PdfTools
{
    /// <summary>
    /// Page count and size of page 1 in points as a viewer displays it: the CropBox (clipped to the MediaBox)
    /// when present, otherwise the MediaBox; width/height are swapped for /Rotate of 90 or 270.
    /// </summary>
    public static PdfPageInfo GetPageInfo(string pdfPath)
    {
        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        var count = doc.PageCount;
        if (count == 0) return new PdfPageInfo(0, 0, 0);
        var page = doc.Pages[0];
        var media = ReadBox(page, "/MediaBox") ?? new double[] { 0, 0, 612, 792 };
        var box = media;
        var crop = ReadBox(page, "/CropBox");
        if (crop is not null)
        {
            var x0 = Math.Max(media[0], crop[0]); var y0 = Math.Max(media[1], crop[1]);
            var x1 = Math.Min(media[2], crop[2]); var y1 = Math.Min(media[3], crop[3]);
            if (x1 > x0 && y1 > y0) box = new[] { x0, y0, x1, y1 };
        }
        double w = box[2] - box[0], h = box[3] - box[1];
        var rot = ((page.Elements.GetInteger("/Rotate") % 360) + 360) % 360;
        if (rot == 90 || rot == 270) (w, h) = (h, w);
        return new PdfPageInfo(count, w, h);
    }

    private static double[]? ReadBox(PdfPage page, string key)
    {
        var r = key == "/MediaBox" ? page.MediaBox : page.CropBox;
        if (r.IsZero) return null;
        double x1 = r.X1, y1 = r.Y1, x2 = r.X2, y2 = r.Y2;
        return new[] { Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2) };
    }

    /// <summary>
    /// Attaches <paramref name="json"/> as an embedded file named <paramref name="attachmentName"/> (replacing any
    /// existing attachment of that name) and sets Info dictionary entries. Title/Subject/Keywords/Author/Creator/Producer
    /// map to the standard properties; every other key becomes a custom /Info entry. Rewrites the file in place.
    /// </summary>
    public static void EmbedJson(string pdfPath, string attachmentName, byte[] json, IDictionary<string, string>? info = null)
    {
        PdfDocument doc;
        using (var fs = File.OpenRead(pdfPath))
        {
            var ms = new MemoryStream();
            fs.CopyTo(ms); ms.Position = 0;
            doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        }
        using (doc)
        {
            // Embedded file stream
            var ef = new PdfDictionary(doc);
            ef.Elements.SetName("/Type", "/EmbeddedFile");
            ef.Elements.SetName("/Subtype", "/application#2Fjson");
            var par = new PdfDictionary(doc);
            par.Elements.SetInteger("/Size", json.Length);
            ef.Elements["/Params"] = par;
            doc.Internals.AddObject(ef);
            ef.CreateStream(json);
            ef.Elements.SetInteger("/Length", json.Length);

            // File specification
            var spec = new PdfDictionary(doc);
            spec.Elements.SetName("/Type", "/Filespec");
            spec.Elements.SetString("/F", attachmentName);
            spec.Elements.SetString("/UF", attachmentName);
            spec.Elements.SetString("/Desc", "FreeField sheet element index");
            spec.Elements.SetName("/AFRelationship", "/Data");
            var efd = new PdfDictionary(doc);
            efd.Elements["/F"] = ef.Reference!;
            efd.Elements["/UF"] = ef.Reference!;
            spec.Elements["/EF"] = efd;
            doc.Internals.AddObject(spec);

            var catalog = doc.Internals.Catalog;
            // /Names /EmbeddedFiles /Names [ (name) ref ... ]  (flat name tree; rebuilt keeping other attachments)
            var names = catalog.Elements.GetDictionary("/Names");
            if (names is null) { names = new PdfDictionary(doc); catalog.Elements["/Names"] = names; }
            var embedded = names.Elements.GetDictionary("/EmbeddedFiles");
            if (embedded is null) { embedded = new PdfDictionary(doc); names.Elements["/EmbeddedFiles"] = embedded; }
            var pairs = new List<(string Name, PdfItem Value)>();
            if (embedded.Elements.GetArray("/Names") is { } old)
            {
                for (var i = 0; i + 1 < old.Elements.Count; i += 2)
                {
                    if (old.Elements[i] is PdfString ps && ps.Value != attachmentName)
                        pairs.Add((ps.Value, old.Elements[i + 1]));
                }
            }
            pairs.Add((attachmentName, spec.Reference!));
            pairs.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            var arr = new PdfArray(doc);
            foreach (var (n, v) in pairs) { arr.Elements.Add(new PdfString(n)); arr.Elements.Add(v); }
            embedded.Elements["/Names"] = arr;

            // Associated files (PDF/A-3 style) for discoverability
            var af = catalog.Elements.GetArray("/AF");
            if (af is null) { af = new PdfArray(doc); catalog.Elements["/AF"] = af; }
            af.Elements.Add(spec.Reference!);

            if (info is not null)
            {
                foreach (var (k, v) in info)
                {
                    switch (k)
                    {
                        case "Title": doc.Info.Title = v; break;
                        case "Subject": doc.Info.Subject = v; break;
                        case "Keywords": doc.Info.Keywords = v; break;
                        case "Author": doc.Info.Author = v; break;
                        case "Creator": doc.Info.Creator = v; break;
                        default: doc.Info.Elements.SetString("/" + k, v); break;
                    }
                }
            }

            var tmp = pdfPath + ".tmp";
            doc.Save(tmp);
            File.Move(tmp, pdfPath, true);
        }
    }

    /// <summary>Reads the embedded file named <paramref name="attachmentName"/>, or null if absent.</summary>
    public static byte[]? ReadEmbeddedJson(string pdfPath, string attachmentName)
    {
        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        return ReadEmbedded(doc, attachmentName);
    }

    public static byte[]? ReadEmbeddedJson(Stream pdf, string attachmentName)
    {
        using var doc = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        return ReadEmbedded(doc, attachmentName);
    }

    private static byte[]? ReadEmbedded(PdfDocument doc, string name)
    {
        var tree = doc.Internals.Catalog.Elements.GetDictionary("/Names")?.Elements.GetDictionary("/EmbeddedFiles");
        if (tree is null) return null;
        return Walk(tree, name);
    }

    private static byte[]? Walk(PdfDictionary node, string name)
    {
        if (node.Elements.GetArray("/Names") is { } arr)
        {
            for (var i = 0; i + 1 < arr.Elements.Count; i += 2)
            {
                if (arr.Elements[i] is PdfString s && s.Value == name)
                {
                    var spec = Deref(arr.Elements[i + 1]) as PdfDictionary;
                    var ef = spec?.Elements.GetDictionary("/EF")?.Elements.GetDictionary("/F");
                    if (ef?.Stream is { } st) return st.UnfilteredValue;
                }
            }
        }
        if (node.Elements.GetArray("/Kids") is { } kids)
        {
            foreach (var k in kids.Elements)
                if (Deref(k) is PdfDictionary kd && Walk(kd, name) is { } r) return r;
        }
        return null;
    }

    private static PdfItem? Deref(PdfItem? i) => i is PdfReference r ? r.Value : i;
}
