using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace FreeField.Exporter.Revit.Automation;

/// <summary>Auto-dismisses Revit dialogs and failure messages during headless runs, logging each one.</summary>
public sealed class DialogGuard : IDisposable
{
    readonly UIApplication _ui;
    public List<string> Dialogs { get; } = new();

    public DialogGuard(UIApplication ui)
    {
        _ui = ui;
        ui.DialogBoxShowing += OnDialog;
        ui.Application.FailuresProcessing += OnFailures;
    }

    void OnDialog(object? s, DialogBoxShowingEventArgs e)
    {
        string desc = e.DialogId;
        if (e is TaskDialogShowingEventArgs t) desc += " | " + t.Message;
        else if (e is MessageBoxShowingEventArgs m) desc += " | " + m.Message;
        Dialogs.Add(desc);
        Log.Write("dialog: " + desc);
        try
        {
            if (e is TaskDialogShowingEventArgs td)
            {
                // Prefer a non-destructive answer; for upgrade / open prompts "OK/Yes/Close" is what we want.
                var id = td.DialogId;
                if (!td.OverrideResult((int)TaskDialogResult.Ok))
                    if (!td.OverrideResult((int)TaskDialogResult.Yes))
                        td.OverrideResult((int)TaskDialogResult.Close);
            }
            else if (e is MessageBoxShowingEventArgs mb) mb.OverrideResult(1);
            else e.OverrideResult(1);
        }
        catch (Exception ex) { Log.Write("dialog override failed: " + ex.Message); }
    }

    void OnFailures(object? s, FailuresProcessingEventArgs e)
    {
        var fa = e.GetFailuresAccessor();
        foreach (var f in fa.GetFailureMessages())
        {
            
            Dialogs.Add("failure: " + f.GetDescriptionText());
            Log.Write("failure: " + f.GetDescriptionText());
            if (f.GetSeverity() == Autodesk.Revit.DB.FailureSeverity.Warning) fa.DeleteWarning(f);
        }
        e.SetProcessingResult(Autodesk.Revit.DB.FailureProcessingResult.Continue);
    }

    public void Dispose()
    {
        _ui.DialogBoxShowing -= OnDialog;
        _ui.Application.FailuresProcessing -= OnFailures;
    }
}
