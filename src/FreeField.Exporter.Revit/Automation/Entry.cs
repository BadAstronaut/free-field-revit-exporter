using System.Diagnostics;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FreeField.Exporter.Revit.Automation;

public static class Entry
{
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Runs one job (opens the rvt if needed, executes mode, writes resultFile, optionally exits).</summary>
    public static void Run(UIApplication uiapp, string jobPath)
    {
        var sw = Stopwatch.StartNew();
        var result = new Dictionary<string, object?> { ["status"] = "running", ["job"] = jobPath };
        Job? job = null;
        using var guard = new DialogGuard(uiapp);
        try
        {
            job = Job.Load(jobPath);
            Log.Write($"job start mode={job.Mode} rvt={job.Rvt}");
            var uidoc = OpenOrActivate(uiapp, job.Rvt, result);
            result["openMs"] = sw.ElapsedMilliseconds;
            var doc = uidoc.Document;
            switch (job.Mode)
            {
                case "spike": Spike.SpikeRunner.Run(uiapp, uidoc, job, result); break;
                default: Pipeline.ExportPipeline.Run(uiapp, uidoc, job, result); break;
            }
            result["status"] = result.TryGetValue("status", out var st) && st as string != "running" ? st : "ok";
        }
        catch (Exception ex)
        {
            Log.Write("job failed: " + ex);
            result["status"] = "error";
            result["error"] = ex.ToString();
        }
        result["totalMs"] = sw.ElapsedMilliseconds;
        result["dialogs"] = guard.Dialogs;
        try
        {
            var rf = job?.ResultFile ?? Path.ChangeExtension(jobPath, ".result.json");
            Directory.CreateDirectory(Path.GetDirectoryName(rf)!);
            File.WriteAllText(rf, JsonSerializer.Serialize(result, JsonOpts));
        }
        catch (Exception ex) { Log.Write("result write failed: " + ex); }
        Log.Write("job done " + result["status"]);
        if (job?.ExitWhenDone == true) Exit(uiapp);
    }

    static UIDocument OpenOrActivate(UIApplication uiapp, string? rvt, Dictionary<string, object?> result)
    {
        if (string.IsNullOrEmpty(rvt))
            return uiapp.ActiveUIDocument ?? throw new InvalidOperationException("no rvt in job and no active document");
        var full = Path.GetFullPath(rvt);
        foreach (UIDocument ud in uiapp.Application.Documents.Cast<Document>().Select(d => new UIDocument(d)))
        { }
        var open = uiapp.Application.Documents.Cast<Document>()
            .FirstOrDefault(d => string.Equals(d.PathName, full, StringComparison.OrdinalIgnoreCase));
        if (open != null)
        {
            if (uiapp.ActiveUIDocument?.Document.Equals(open) == true) return uiapp.ActiveUIDocument;
            return uiapp.OpenAndActivateDocument(full);
        }
        // OpenAndActivateDocument: an active UIDocument is needed for some exports (view-dependent), and it
        // handles upgrade prompts through DialogGuard. Work copies only.
        var oo = new OpenOptions { Audit = false };
        var mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(full);
        var ud2 = uiapp.OpenAndActivateDocument(mp, oo, false);
        result["opened"] = full;
        return ud2;
    }

    static void Exit(UIApplication uiapp)
    {
        try
        {
            // Documents are unmodified (all transactions rolled back), so Exit does not prompt to save.
            var id = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
            uiapp.PostCommand(id);
        }
        catch (Exception ex) { Log.Write("exit failed: " + ex.Message); }
    }
}
