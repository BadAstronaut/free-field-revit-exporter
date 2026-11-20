using System.IO.Compression;
using FreeField.Exporter.Core.Model;

namespace FreeField.Exporter.Core;

/// <summary>Builds a <c>.ffbundle</c> zip. Large files are streamed, never loaded fully into memory.</summary>
public sealed class BundleWriter : IDisposable
{
    public const string SheetAttachmentName = "freefield-sheet.json";

    private readonly string _bundlePath;
    private readonly Dictionary<string, string> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (SheetEntry Sheet, string Path)> _sheets = new(StringComparer.Ordinal);
    private string? _tempDir;
    private bool _disposed;

    public BundleWriter(string bundlePath)
    {
        _bundlePath = Path.GetFullPath(bundlePath ?? throw new ArgumentNullException(nameof(bundlePath)));
    }

    /// <summary>Registers an IFC file; stored at <c>models/&lt;modelId&gt;.ifc</c>.</summary>
    public void AddModelFile(string modelId, string ifcSourcePath)
    {
        if (!File.Exists(ifcSourcePath)) throw new FileNotFoundException("IFC not found", ifcSourcePath);
        _models[modelId] = ifcSourcePath;
    }

    /// <summary>Registers a sheet PDF; stored at <c>sheets/&lt;sanitized sheet number&gt;.pdf</c>.</summary>
    public void AddSheetPdf(SheetEntry sheet, string pdfSourcePath)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (!File.Exists(pdfSourcePath)) throw new FileNotFoundException("PDF not found", pdfSourcePath);
        _sheets[sheet.Id] = (sheet, pdfSourcePath);
    }

    public void Write(
        BundleManifest manifest,
        IReadOnlyDictionary<string, ElementRecord> elements,
        IEnumerable<SheetElementIndex> sheetIndexes,
        IEnumerable<(ScheduleEntry Entry, DataTable Table)> schedules)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(manifest);
        var indexById = new Dictionary<string, SheetElementIndex>(StringComparer.Ordinal);
        foreach (var i in sheetIndexes ?? Array.Empty<SheetElementIndex>()) indexById[i.SheetId] = i;

        // --- models
        foreach (var m in manifest.Models)
        {
            if (!_models.ContainsKey(m.Id)) throw new InvalidOperationException($"No IFC file added for model '{m.Id}'.");
        }
        var modelPaths = new List<(string Entry, string Source)>();
        var usedModelStems = new HashSet<string>();
        foreach (var m in manifest.Models)
        {
            var stem = BundleNaming.MakeUnique(BundleNaming.SanitizeFileStem(m.Id), usedModelStems);
            m.File = $"models/{stem}.ifc";
            modelPaths.Add((m.File, _models[m.Id]));
        }

        // --- sheets
        _tempDir = Path.Combine(Path.GetTempPath(), "ffbundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var usedSheetStems = new HashSet<string>();
        var sheetFiles = new List<(string Entry, string Source)>();
        var sheetData = new List<(string Entry, byte[] Json)>();
        var linkCount = 0;
        foreach (var s in manifest.Sheets)
        {
            if (!_sheets.TryGetValue(s.Id, out var reg)) throw new InvalidOperationException($"No PDF added for sheet '{s.Number}' ({s.Id}).");
            var stem = BundleNaming.MakeUnique(BundleNaming.SanitizeFileStem(s.Number), usedSheetStems);
            s.File = $"sheets/{stem}.pdf";
            s.Data = $"data/sheets/{stem}.json";

            var tmpPdf = Path.Combine(_tempDir, stem + ".pdf");
            File.Copy(reg.Path, tmpPdf, true);
            if (s.Page.WidthPt <= 0 || s.Page.HeightPt <= 0)
            {
                var pi = PdfTools.GetPageInfo(tmpPdf);
                s.Page = new PageSize { WidthPt = pi.WidthPt, HeightPt = pi.HeightPt };
            }

            indexById.TryGetValue(s.Id, out var idx);
            idx ??= new SheetElementIndex { SheetId = s.Id };
            idx.SheetId = s.Id;
            linkCount += idx.Links.Count;

            var payload = new SheetEmbedPayload
            {
                SheetId = s.Id, SheetNumber = s.Number, SheetName = s.Name, Page = s.Page,
                Viewports = s.Viewports, Index = idx,
            };
            foreach (var e in idx.Elements)
                if (elements.TryGetValue(e.Gid, out var rec)) payload.Elements[e.Gid] = rec;

            PdfTools.EmbedJson(tmpPdf, SheetAttachmentName, BundleJson.SerializeToUtf8(payload), new Dictionary<string, string>
            {
                ["Title"] = string.IsNullOrEmpty(s.Name) ? s.Number : $"{s.Number} - {s.Name}",
                ["Subject"] = "FreeField sheet",
                ["Keywords"] = "freefield;bundle;" + s.Number,
                ["FreeFieldSheetId"] = s.Id,
                ["FreeFieldSheetNumber"] = s.Number,
                ["FreeFieldSchema"] = SheetEmbedPayload.SchemaId,
            });
            sheetFiles.Add((s.File, tmpPdf));
            sheetData.Add((s.Data, BundleJson.SerializeToUtf8(idx)));
        }

        // --- schedules
        var usedSlugs = new HashSet<string>();
        var schedFiles = new List<(string Entry, byte[] Json)>();
        manifest.Schedules = new List<ScheduleEntry>();
        foreach (var (entry, table) in schedules ?? Array.Empty<(ScheduleEntry, DataTable)>())
        {
            var slug = BundleNaming.MakeUnique(BundleNaming.Slug(entry.Name), usedSlugs);
            entry.File = $"data/schedules/{slug}.json";
            manifest.Schedules.Add(entry);
            schedFiles.Add((entry.File, BundleJson.SerializeToUtf8(table)));
        }

        manifest.ElementsFile = "data/elements.json";
        manifest.Stats = new BundleStats
        {
            Elements = elements.Count, Sheets = manifest.Sheets.Count,
            Viewports = manifest.Sheets.Sum(x => x.Viewports.Count), Links = linkCount,
        };

        // --- zip (temp file + atomic move)
        var dir = Path.GetDirectoryName(_bundlePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmpZip = _bundlePath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            using (var fs = new FileStream(tmpZip, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddBytes(zip, "manifest.json", BundleJson.SerializeToUtf8(manifest, indented: true));
                foreach (var (e, src) in modelPaths) AddFile(zip, e, src);
                foreach (var (e, src) in sheetFiles) AddFile(zip, e, src);
                AddBytes(zip, manifest.ElementsFile, BundleJson.SerializeToUtf8(elements));
                foreach (var (e, j) in sheetData) AddBytes(zip, e, j);
                foreach (var (e, j) in schedFiles) AddBytes(zip, e, j);
            }
            File.Move(tmpZip, _bundlePath, true);
        }
        catch
        {
            try { File.Delete(tmpZip); } catch { /* best effort */ }
            throw;
        }
        finally
        {
            CleanupTemp();
        }
    }

    private static void AddBytes(ZipArchive zip, string name, byte[] data)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(data, 0, data.Length);
    }

    private static void AddFile(ZipArchive zip, string name, string source)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var dst = entry.Open();
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        src.CopyTo(dst, 1 << 20);
    }

    private void CleanupTemp()
    {
        if (_tempDir is null) return;
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
        _tempDir = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupTemp();
    }
}
