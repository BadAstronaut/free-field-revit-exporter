using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using FreeField.Exporter.Revit.Pipeline;

namespace FreeField.Exporter.Revit;

/// <summary>Lean WPF dialog (code-only, no XAML): sheet / plan-view pickers, options, output folder, progress.</summary>
public sealed class ExportDialog : Window
{
    readonly Document _doc;
    readonly List<(CheckBox Box, string Number)> _sheets = new();
    readonly List<(CheckBox Box, long Id)> _views = new();
    readonly CheckBox _includeSchedules = new() { Content = "Include schedules", IsChecked = true };
    readonly ComboBox _ifc = new() { Width = 180 };
    readonly TextBox _out = new() { MinWidth = 380 };
    readonly ProgressBar _bar = new() { Height = 16, Minimum = 0, Maximum = 100 };
    readonly TextBlock _status = new() { Text = "Ready", Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly Button _go = new() { Content = "Export", Width = 90, Margin = new Thickness(6, 0, 0, 0), IsDefault = true };
    readonly Button _close = new() { Content = "Close", Width = 90, Margin = new Thickness(6, 0, 0, 0), IsCancel = true };

    public ExportDialog(Document doc)
    {
        _doc = doc;
        Title = "FreeField - Export Bundle"; Width = 640; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(Header("Sheets (default: sheets with at least one view)"));
        var sheetPanel = new StackPanel();
        foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber))
        {
            var vps = s.GetAllViewports().Select(id => doc.GetElement(id)).OfType<Viewport>().Select(v => doc.GetElement(v.ViewId) as View).Where(v => v != null).ToList();
            var plans = vps.Count(v => ExportPipeline.IsPlan(v!.ViewType));
            var cb = new CheckBox { Content = $"{s.SheetNumber}  {s.Name}   ({vps.Count} views, {plans} plans)", IsChecked = vps.Count > 0, Margin = new Thickness(0, 1, 0, 1) };
            _sheets.Add((cb, s.SheetNumber)); sheetPanel.Children.Add(cb);
        }
        root.Children.Add(new ScrollViewer { Content = sheetPanel, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(1), BorderBrush = System.Windows.Media.Brushes.Gray });

        root.Children.Add(Header("Plan views not placed on any sheet (exported as VIEW-<name> pages)"));
        var placed = new HashSet<long>(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(v => v.ViewId.Value));
        var viewPanel = new StackPanel();
        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => !v.IsTemplate && ExportPipeline.IsPlan(v.ViewType) && !placed.Contains(v.Id.Value) && v.GenLevel != null).OrderBy(v => v.Name))
        {
            var cb = new CheckBox { Content = $"{v.Name}   [{v.GenLevel!.Name}]", IsChecked = true, Margin = new Thickness(0, 1, 0, 1) };
            _views.Add((cb, v.Id.Value)); viewPanel.Children.Add(cb);
        }
        root.Children.Add(new ScrollViewer { Content = viewPanel, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(1), BorderBrush = System.Windows.Media.Brushes.Gray });

        root.Children.Add(Header("Level to storey mapping (read-only: storey name = level name)"));
        var lv = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = string.Join(", ", new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).Select(l => l.Name)) };
        root.Children.Add(lv);

        root.Children.Add(Header("Options"));
        _ifc.Items.Add("IFC4 Reference View"); _ifc.Items.Add("IFC2x3 Coordination View 2.0"); _ifc.SelectedIndex = 0;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_includeSchedules); row.Children.Add(new TextBlock { Text = "   IFC: ", VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(_ifc);
        root.Children.Add(row);
        _out.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FreeField");
        var orow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        orow.Children.Add(new TextBlock { Text = "Output folder: ", VerticalAlignment = VerticalAlignment.Center }); orow.Children.Add(_out);
        var browse = new Button { Content = "...", Width = 28, Margin = new Thickness(4, 0, 0, 0) };
        browse.Click += (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _out.Text }; if (d.ShowDialog() == true) _out.Text = d.FolderName; };
        orow.Children.Add(browse); root.Children.Add(orow);

        root.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10) });
        root.Children.Add(_bar); root.Children.Add(_status);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        btns.Children.Add(_go); btns.Children.Add(_close); root.Children.Add(btns);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _go.Click += OnExport;
    }

    static TextBlock Header(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };

    void OnExport(object? s, RoutedEventArgs e)
    {
        var selSheets = _sheets.Where(x => x.Box.IsChecked == true).Select(x => x.Number).ToHashSet();
        var selViews = _views.Where(x => x.Box.IsChecked == true).Select(x => x.Id).ToHashSet();
        var opt = new ExportOptions
        {
            Sheets = selSheets, PlanViews = selViews, IncludeSheets = selSheets.Count > 0, IncludePlanViewsNotOnSheets = selViews.Count > 0,
            IncludeSchedules = _includeSchedules.IsChecked == true, IfcVersion = _ifc.SelectedIndex == 0 ? "IFC4RV" : "IFC2x3CV2", OutputPath = _out.Text,
        };
        _go.IsEnabled = false; _close.IsEnabled = false;
        try
        {
            var r = ExportPipeline.Export(_doc, opt, (p, m) =>
            {
                _bar.Value = p; _status.Text = m;
                Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            });
            _status.Text = $"Done: {r.Sheets} sheets, {r.Viewports} viewports, {r.Elements} elements, {r.Links} links, {r.Schedules} schedules" +
                           (r.Warnings.Count > 0 ? $" ({r.Warnings.Count} warnings, see log)" : "") + "\n" + r.BundlePath;
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{r.BundlePath}\""); } catch { }
        }
        catch (Exception ex) { _status.Text = "Failed: " + ex.Message; Automation.Log.Write("export failed: " + ex); }
        finally { _go.IsEnabled = true; _close.IsEnabled = true; }
    }
}
