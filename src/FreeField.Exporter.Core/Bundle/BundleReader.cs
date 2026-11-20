using System.IO.Compression;
using FreeField.Exporter.Core.Model;

namespace FreeField.Exporter.Core;

/// <summary>Reads a <c>.ffbundle</c> zip.</summary>
public sealed class BundleReader : IDisposable
{
    private readonly ZipArchive _zip;

    public BundleReader(string bundlePath)
    {
        _zip = ZipFile.OpenRead(bundlePath);
        Manifest = ReadJson<BundleManifest>("manifest.json");
    }

    public BundleManifest Manifest { get; }

    public IEnumerable<string> EntryNames => _zip.Entries.Select(e => e.FullName);

    public bool Exists(string entryName) => _zip.GetEntry(entryName) is not null;

    public Stream OpenEntry(string entryName)
        => (_zip.GetEntry(entryName) ?? throw new FileNotFoundException("Bundle entry not found: " + entryName)).Open();

    public byte[] ReadEntryBytes(string entryName)
    {
        using var s = OpenEntry(entryName);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Streams an entry (e.g. a large IFC) to a file.</summary>
    public void ExtractEntry(string entryName, string destPath)
    {
        using var s = OpenEntry(entryName);
        using var d = File.Create(destPath);
        s.CopyTo(d, 1 << 20);
    }

    public T ReadJson<T>(string entryName)
    {
        using var s = OpenEntry(entryName);
        return BundleJson.Deserialize<T>(s);
    }

    public Dictionary<string, ElementRecord> ReadElements() => ReadJson<Dictionary<string, ElementRecord>>(Manifest.ElementsFile);

    public SheetElementIndex ReadSheetIndex(SheetEntry sheet) => ReadJson<SheetElementIndex>(sheet.Data);

    public DataTable ReadSchedule(ScheduleEntry schedule) => ReadJson<DataTable>(schedule.File);

    public void Dispose() => _zip.Dispose();
}
