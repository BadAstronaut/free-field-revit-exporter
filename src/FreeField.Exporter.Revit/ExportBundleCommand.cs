using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FreeField.Exporter.Revit;

[Transaction(TransactionMode.Manual)]
public class ExportBundleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        // Phase 2: shows the WPF dialog.
        TaskDialog.Show("FreeField", "Export dialog not implemented yet.");
        return Result.Succeeded;
    }
}
