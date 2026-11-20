using System.Reflection;
using System.Runtime.Loader;
using Autodesk.Revit.UI;

namespace FreeField.Exporter.Revit;

/// <summary>
/// Ribbon + headless job hook. Headless: env FREEFIELD_EXPORT_JOB=job.json runs once on first Idling.
/// Dev: if %LOCALAPPDATA%/FreeFieldExporter/dev/hot.json exists ({"dll": "..."}) the worker code is loaded
/// from that dll in a fresh AssemblyLoadContext per job (so rebuilds do not need a Revit restart) and
/// *.json files dropped into %LOCALAPPDATA%/FreeFieldExporter/jobs are picked up while Revit idles.
/// </summary>
public class App : IExternalApplication
{
    static bool _busy, _envDone;
    static System.Windows.Threading.Dispatcher? _disp; static ExternalEvent? _evt; static System.Threading.Timer? _timer;
    static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeFieldExporter");

    public Result OnStartup(UIControlledApplication app)
    {
        try
        {
            try { app.CreateRibbonTab("FreeField"); } catch { }
            var panel = app.CreateRibbonPanel("FreeField", "Export");
            var data = new PushButtonData("ExportBundle", "Export Bundle", Assembly.GetExecutingAssembly().Location,
                typeof(ExportBundleCommand).FullName)
            { ToolTip = "Export IFC + per-sheet PDFs + mapping data as a FreeField bundle (.ffbundle)" };
            panel.AddItem(data);
        }
        catch (Exception ex) { Automation.Log.Write("ribbon failed: " + ex); }
        app.Idling += OnIdling;
        _evt = ExternalEvent.Create(new JobHandler());
        _disp = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _timer = new System.Threading.Timer(_ => { try { if (!_busy && File.Exists(Path.Combine(Root, "dev", "hot.json")) && Directory.Exists(Path.Combine(Root, "jobs")) && Directory.GetFiles(Path.Combine(Root, "jobs"), "*.json").Length > 0) { _evt!.Raise(); Wake(); _disp?.BeginInvoke(new Action(() => { try { _evt!.Raise(); } catch { } })); } } catch { } }, null, 2000, 1000);
        return Result.Succeeded;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    /// <summary>Revit only pumps ExternalEvents/Idling when its message loop wakes; nudge it.</summary>
    static void Wake() { try { var h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; if (h != IntPtr.Zero) PostMessage(h, 0x0200, IntPtr.Zero, IntPtr.Zero); } catch { } }

    public Result OnShutdown(UIControlledApplication app) { app.Idling -= OnIdling; return Result.Succeeded; }

    sealed class JobHandler : IExternalEventHandler
    {
        public void Execute(UIApplication app) => PollAndRun(app, false);
        public string GetName() => "FreeField job poll";
    }

    void OnIdling(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
    {
        if (sender is UIApplication ua) PollAndRun(ua, true);
    }

    static void PollAndRun(UIApplication uiapp, bool fromIdle)
    {
        if (_busy) return;
        string? job = null;
        if (fromIdle && !_envDone)
        {
            _envDone = true;
            job = Environment.GetEnvironmentVariable("FREEFIELD_EXPORT_JOB");
        }
        if (string.IsNullOrEmpty(job) && File.Exists(Path.Combine(Root, "dev", "hot.json")))
        {
            var dir = Path.Combine(Root, "jobs");
            if (Directory.Exists(dir))
            {
                var f = Directory.GetFiles(dir, "*.json").OrderBy(x => x).FirstOrDefault();
                if (f != null)
                {
                    var run = f + ".running";
                    try { File.Move(f, run, true); job = run; } catch { }
                }
            }
        }
        if (string.IsNullOrEmpty(job)) return;
        _busy = true;
        try { Dispatch(uiapp, job!); }
        catch (Exception ex) { Automation.Log.Write("job dispatch failed: " + ex); }
        finally { _busy = false; }
    }

    static void Dispatch(UIApplication uiapp, string job)
    {
        var hot = Path.Combine(Root, "dev", "hot.json");
        if (File.Exists(hot))
        {
            var dll = System.Text.Json.JsonDocument.Parse(File.ReadAllText(hot)).RootElement.GetProperty("dll").GetString()!;
            var dir = Path.GetDirectoryName(dll)!;
            var alc = new AssemblyLoadContext("ffhot" + Guid.NewGuid().ToString("N"), true);
            alc.Resolving += (ctx, name) =>
            {
                var p = Path.Combine(dir, name.Name + ".dll");
                return File.Exists(p) ? ctx.LoadFromStream(new MemoryStream(File.ReadAllBytes(p))) : null;
            };
            var asm = alc.LoadFromStream(new MemoryStream(File.ReadAllBytes(dll)));
            asm.GetType("FreeField.Exporter.Revit.Automation.Entry")!
               .GetMethod("Run")!.Invoke(null, new object[] { uiapp, job });
            return;
        }
        Automation.Entry.Run(uiapp, job);
    }
}
