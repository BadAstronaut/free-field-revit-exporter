using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FreeField.Exporter.Revit;

[Transaction(TransactionMode.Manual)]
public class ExportBundleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var doc = data.Application.ActiveUIDocument?.Document;
        if (doc == null || doc.IsFamilyDocument) { message = "Open a project document first."; return Result.Failed; }
        new ExportDialog(doc).ShowDialog();
        return Result.Succeeded;
    }
}
