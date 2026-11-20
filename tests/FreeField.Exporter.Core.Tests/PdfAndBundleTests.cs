using System.Text;
using System.Text.Json.Nodes;
using FreeField.Exporter.Core.Model;
using PdfSharp.Pdf.IO;

namespace FreeField.Exporter.Core.Tests;

public class PdfToolsTests
{
    [Fact]
    public void GetPageInfo_ReadsSize()
    {
        var d = TestUtil.TempDir(); var p = Path.Combine(d, "a.pdf");
        TestUtil.MakePdf(p, 2592, 1728);
        var i = PdfTools.GetPageInfo(p);
        Assert.Equal(1, i.PageCount);
        Assert.Equal(2592, i.WidthPt, 3);
        Assert.Equal(1728, i.HeightPt, 3);
    }

    [Fact]
    public void GetPageInfo_Rotation_SwapsAxes_And_CropBoxWins()
    {
        var d = TestUtil.TempDir(); var p = Path.Combine(d, "r.pdf");
        TestUtil.MakePdf(p, 600, 400, rotate: 90);
        var i = PdfTools.GetPageInfo(p);
        Assert.Equal(400, i.WidthPt, 3); Assert.Equal(600, i.HeightPt, 3);

        var c = Path.Combine(d, "c.pdf");
        using (var doc = PdfReader.Open(p, PdfDocumentOpenMode.Modify))
        {
            doc.Pages[0].Elements.SetInteger("/Rotate", 0);
            doc.Pages[0].CropBox = new PdfSharp.Pdf.PdfRectangle(new PdfSharp.Drawing.XRect(0, 0, 300, 200));
            doc.Save(c);
        }
        var ci = PdfTools.GetPageInfo(c);
        Assert.Equal(300, ci.WidthPt, 3); Assert.Equal(200, ci.HeightPt, 3);
    }

    [Fact]
    public void EmbedJson_RoundTrips_And_SetsInfo()
    {
        var d = TestUtil.TempDir(); var p = Path.Combine(d, "e.pdf");
        TestUtil.MakePdf(p, 500, 300);
        var json = Encoding.UTF8.GetBytes("{\"hello\":\"wörld\",\"n\":[1,2,3]}");
        PdfTools.EmbedJson(p, "freefield-sheet.json", json, new Dictionary<string, string>
        {
            ["Title"] = "A103 - Plan", ["Subject"] = "FreeField sheet", ["Keywords"] = "a;b", ["FreeFieldSheetId"] = "sid-1",
        });
        Assert.Equal(json, PdfTools.ReadEmbeddedJson(p, "freefield-sheet.json"));
        Assert.Null(PdfTools.ReadEmbeddedJson(p, "nope.json"));

        // second attachment keeps the first; replacing same name works
        var json2 = Encoding.UTF8.GetBytes("{\"v\":2}");
        PdfTools.EmbedJson(p, "other.json", json2, null);
        PdfTools.EmbedJson(p, "freefield-sheet.json", json2, null);
        Assert.Equal(json2, PdfTools.ReadEmbeddedJson(p, "freefield-sheet.json"));
        Assert.Equal(json2, PdfTools.ReadEmbeddedJson(p, "other.json"));

        using var doc = PdfReader.Open(p, PdfDocumentOpenMode.Import);
        Assert.Equal("A103 - Plan", doc.Info.Title);
        Assert.Equal("FreeField sheet", doc.Info.Subject);
        Assert.Equal("a;b", doc.Info.Keywords);
        Assert.Equal("sid-1", doc.Info.Elements.GetString("/FreeFieldSheetId"));
        Assert.Equal(1, PdfTools.GetPageInfo(p).PageCount);
    }

    [Fact]
    public void LargeishJson_Compressed_RoundTrips()
    {
        var d = TestUtil.TempDir(); var p = Path.Combine(d, "l.pdf");
        TestUtil.MakePdf(p, 500, 300);
        var json = Encoding.UTF8.GetBytes("[" + string.Join(",", Enumerable.Range(0, 20000)) + "]");
        PdfTools.EmbedJson(p, "freefield-sheet.json", json, null);
        Assert.Equal(json, PdfTools.ReadEmbeddedJson(p, "freefield-sheet.json"));
    }
}

public class BundleRoundTripTests
{
    [Fact]
    public void Writer_Reader_RoundTrip()
    {
        var d = TestUtil.TempDir();
        var ifc = Path.Combine(d, "src.ifc");
        File.WriteAllText(ifc, "ISO-10303-21;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n");
        var pdf = Path.Combine(d, "src.pdf");
        TestUtil.MakePdf(pdf, 1000, 800);

        var m = TestUtil.SampleManifest(out var els, out var idx);
        var sched = new DataTable
        {
            Name = "Door Schedule", Category = "Doors",
            Columns = { new DataColumn { Heading = "Mark", ParamKey = "ALL_MODEL_MARK" } },
            Rows = { new DataRow { Gid = "gid1", Uid = "uid1", Cells = { "D1" } } },
        };
        var bundle = Path.Combine(d, "out", "p.ffbundle");
        using (var w = new BundleWriter(bundle))
        {
            w.AddModelFile("arch", ifc);
            w.AddSheetPdf(m.Sheets[0], pdf);
            w.Write(m, els, idx, new[] { (new ScheduleEntry { Id = "s1", Name = "Door Schedule", Category = "Doors" }, sched) });
        }
        Assert.True(File.Exists(bundle));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(bundle)!, "*.tmp"));

        using var r = new BundleReader(bundle);
        Assert.Equal("freefield.bundle/1", r.Manifest.Schema);
        Assert.Equal("models/arch.ifc", r.Manifest.Models[0].File);
        var sh = r.Manifest.Sheets[0];
        Assert.Equal("sheets/A_103_x.pdf", sh.File);
        Assert.Equal("data/sheets/A_103_x.json", sh.Data);
        Assert.Equal("data/schedules/door_schedule.json", r.Manifest.Schedules[0].File);
        Assert.Equal(2, r.Manifest.Stats.Elements);
        Assert.Equal(1, r.Manifest.Stats.Sheets);
        Assert.Equal(2, r.Manifest.Stats.Viewports);
        Assert.Equal(1, r.Manifest.Stats.Links);
        Assert.Equal(new[] { 0.01, 0, 5, 0, -0.01, 50 }, sh.Viewports[0].PdfToModel);
        Assert.Null(sh.Viewports[1].PdfToModel);

        var els2 = r.ReadElements();
        Assert.Equal(2, els2.Count);
        Assert.Equal("D1", els2["gid1"].Params["Mark"].V);
        var idx2 = r.ReadSheetIndex(sh);
        Assert.Equal("gid1", idx2.Elements[0].Gid);
        Assert.Equal(2, idx2.Elements[0].Outline!.Length);
        Assert.Equal("A201", idx2.Links[0].TargetSheetNumber);
        Assert.Equal("Door Schedule", r.ReadSchedule(r.Manifest.Schedules[0]).Name);

        // IFC copied byte for byte
        var ifcOut = Path.Combine(d, "x.ifc");
        r.ExtractEntry("models/arch.ifc", ifcOut);
        Assert.Equal(File.ReadAllBytes(ifc), File.ReadAllBytes(ifcOut));

        // PDF has embedded sheet json + page size
        var pdfOut = Path.Combine(d, "x.pdf");
        r.ExtractEntry(sh.File, pdfOut);
        var info = PdfTools.GetPageInfo(pdfOut);
        Assert.Equal(1, info.PageCount);
        Assert.Equal(1000, info.WidthPt, 3);
        var emb = JsonNode.Parse(PdfTools.ReadEmbeddedJson(pdfOut, BundleWriter.SheetAttachmentName)!)!;
        Assert.Equal("freefield.sheet/1", (string?)emb["schema"]);
        Assert.Equal("sheet-1", (string?)emb["sheetId"]);
        Assert.Equal("A/103 x", (string?)emb["sheetNumber"]);
        Assert.Equal("Third Floor Plan", (string?)emb["sheetName"]);
        Assert.Equal("gid1", (string?)emb["elements"]![0]!["gid"]);
        Assert.NotNull(emb["elementRecords"]!["gid1"]);
        Assert.Null(emb["elementRecords"]!["gid2"]); // only referenced records
        Assert.Equal("Level 3", (string?)emb["viewports"]![0]!["storeyName"]);
        Assert.Equal(6, emb["viewports"]![0]!["pdfToModel"]!.AsArray().Count);
        Assert.Equal("sheet-2-target", (string?)emb["links"]![0]!["targetSheetId"]);
        Assert.Equal("A201", (string?)emb["links"]![0]!["targetSheetNumber"]);
        Assert.True(JsonTests.Validate("EmbeddedSheetPayload", emb.ToJsonString()).IsValid);
    }

    [Fact]
    public void Writer_MissingFiles_Throw()
    {
        var d = TestUtil.TempDir();
        var m = TestUtil.SampleManifest(out var els, out var idx);
        using var w = new BundleWriter(Path.Combine(d, "x.ffbundle"));
        Assert.Throws<InvalidOperationException>(() => w.Write(m, els, idx, Array.Empty<(ScheduleEntry, DataTable)>()));
        Assert.False(File.Exists(Path.Combine(d, "x.ffbundle")));
    }

    [Fact]
    public void Writer_DuplicateSheetNumbers_GetUniqueFiles()
    {
        var d = TestUtil.TempDir();
        var ifc = Path.Combine(d, "s.ifc"); File.WriteAllText(ifc, "x");
        var pdf = Path.Combine(d, "s.pdf"); TestUtil.MakePdf(pdf, 100, 100);
        var m = TestUtil.SampleManifest(out var els, out var idx);
        m.Sheets.Add(new SheetEntry { Id = "sheet-2", Number = "A/103_x", Name = "dup" });
        var bundle = Path.Combine(d, "b.ffbundle");
        using (var w = new BundleWriter(bundle))
        {
            w.AddModelFile("arch", ifc);
            w.AddSheetPdf(m.Sheets[0], pdf); w.AddSheetPdf(m.Sheets[1], pdf);
            w.Write(m, els, idx, Array.Empty<(ScheduleEntry, DataTable)>());
        }
        using var r = new BundleReader(bundle);
        Assert.Equal(2, r.Manifest.Sheets.Select(s => s.File).Distinct().Count());
        Assert.All(r.Manifest.Sheets, s => Assert.True(r.Exists(s.File) && r.Exists(s.Data)));
        Assert.Equal(100, r.Manifest.Sheets[1].Page.WidthPt, 3); // filled from the PDF
    }
}
