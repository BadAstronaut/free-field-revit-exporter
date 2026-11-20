using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeField.Exporter.Revit.Automation;

public class Job
{
    [JsonPropertyName("mode")] public string Mode { get; set; } = "export";   // export | spike
    [JsonPropertyName("rvt")] public string? Rvt { get; set; }
    [JsonPropertyName("output")] public string? Output { get; set; }
    [JsonPropertyName("sheets")] public List<string>? Sheets { get; set; }
    [JsonPropertyName("exitWhenDone")] public bool ExitWhenDone { get; set; }
    [JsonPropertyName("resultFile")] public string? ResultFile { get; set; }
    [JsonPropertyName("options")] public Dictionary<string, string>? Options { get; set; }

    public string Opt(string k, string d = "") => Options != null && Options.TryGetValue(k, out var v) ? v : d;
    public static Job Load(string path) =>
        JsonSerializer.Deserialize<Job>(File.ReadAllText(path), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip })!;
}
