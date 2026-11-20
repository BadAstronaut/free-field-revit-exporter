using System.Text.RegularExpressions;

namespace FreeField.Exporter.Core;

public static class BundleNaming
{
    private static readonly Regex Bad = new("[^A-Za-z0-9._-]", RegexOptions.Compiled);
    private static readonly Regex Multi = new("_{2,}", RegexOptions.Compiled);

    /// <summary>Sanitizes to <c>[A-Za-z0-9._-]</c>, collapses runs of '_', trims '_', '.', '-' ends; never empty.</summary>
    public static string SanitizeFileStem(string? name)
    {
        var s = Bad.Replace(name ?? "", "_");
        s = Multi.Replace(s, "_").Trim('_', '.', ' ');
        return s.Length == 0 ? "_" : s;
    }

    /// <summary>Returns <paramref name="stem"/> or stem_2, stem_3... (case-insensitive) and adds it to <paramref name="used"/>.</summary>
    public static string MakeUnique(string stem, HashSet<string> used)
    {
        var cand = stem; var n = 2;
        while (!used.Add(cand.ToLowerInvariant())) cand = $"{stem}_{n++}";
        return cand;
    }

    /// <summary>Lower-case slug for schedule file names.</summary>
    public static string Slug(string? name) => SanitizeFileStem(name).ToLowerInvariant();
}
