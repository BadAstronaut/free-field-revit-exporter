using Color = System.Windows.Media.Color;
using Binding = System.Windows.Data.Binding;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using FreeField.Exporter.Core.Selection;
using FreeField.Exporter.Revit.Pipeline;

namespace FreeField.Exporter.Revit;

/// <summary>Row view-model for a sheet / unplaced view in the grids.</summary>
public sealed class RowVm : INotifyPropertyChanged
{
    bool _sel;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Action<RowVm>? OnChanged;
    public string Key { get; init; } = "";
    public string Number { get; init; } = "";
    public string Name { get; init; } = "";
    public string Plans { get; init; } = "";
    public string Levels { get; init; } = "";
    public string Status { get; init; } = "";
    public MapStatus MapStatus { get; init; }
    public bool IsSheet { get; init; }
    public IReadOnlyList<string?> LevelNames { get; init; } = Array.Empty<string?>();
    public Brush StatusBrush => MapStatus switch
    {
        MapStatus.Mapped => new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x3B)),
        MapStatus.LevelNotExported => new SolidColorBrush(Color.FromRgb(0xB2, 0x6A, 0x00)),
        _ => Brushes.Gray,
    };
    public bool Selected
    {
        get => _sel;
        set { if (_sel == value) return; _sel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); OnChanged?.Invoke(this); }
    }
    public void SetSilently(bool v) { _sel = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
}

public sealed class LevelVm
{
    public string Level { get; init; } = "";
    public string Storey { get; init; } = "";
    public string Elevation { get; init; } = "";
    public string Plan { get; init; } = "";
}

/// <summary>
/// Export dialog: sheets (mappable first), "Other sheets (not mapped)" group, floor-plan views not on sheets, level mapping panel, options,
/// progress/cancel and a result panel. All selection logic lives in <see cref="SheetSelectionModel"/> (also used by headless jobs).
/// </summary>
public sealed class ExportDialog : Window
{
    readonly Document _doc;
    Survey _survey;
    SheetSelectionModel _model;
    readonly double _rvtMb;
    readonly ObservableCollection<RowVm> _mainRows = new(), _otherRows = new(), _viewRows = new();
    readonly ObservableCollection<LevelVm> _levelRows = new();
    readonly TextBox _search = new() { MinWidth = 220 };
    readonly ComboBox _levelFilter = new() { MinWidth = 170 };
    readonly TextBlock _summary = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    readonly CheckBox _schedules = new() { Content = "Include schedules", IsChecked = true, Margin = new Thickness(0, 0, 14, 0) };
    readonly CheckBox _outlines = new() { Content = "Element outlines", IsChecked = true, Margin = new Thickness(0, 0, 14, 0) };
    readonly CheckBox _ceilingArea = new() { Content = "Map ceiling / area plans", IsChecked = false, Margin = new Thickness(0, 0, 14, 0) };
    readonly ComboBox _ifc = new() { Width = 200 };
    readonly TextBox _out = new() { MinWidth = 420 };
    readonly ProgressBar _bar = new() { Height = 14, Minimum = 0, Maximum = 100 };
    readonly TextBlock _status = new() { Text = "Ready", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    readonly Button _go = new() { Content = "Export", Width = 100, Margin = new Thickness(6, 0, 0, 0), IsDefault = true };
    readonly Button _cancel = new() { Content = "Cancel", Width = 100, Margin = new Thickness(6, 0, 0, 0), IsEnabled = false };
    readonly Button _close = new() { Content = "Close", Width = 100, Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
    readonly StackPanel _result = new() { Visibility = System.Windows.Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    readonly TextBlock _resultText = new() { TextWrapping = TextWrapping.Wrap };
    string _lastBundle = "";
    bool _running, _cancelRequested, _suspend;
    ICollectionView? _mainView, _otherView, _viewsView;

    static string PrefsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeFieldExporter", "prefs.json");
    string DocKey => string.IsNullOrEmpty(_doc.PathName) ? _doc.Title : _doc.PathName;

    public ExportDialog(Document doc)
    {
        _doc = doc;
        Title = "FreeField - Export Bundle"; Width = 1080; Height = 820; MinWidth = 820; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        long len = 0; try { if (File.Exists(doc.PathName)) len = new FileInfo(doc.PathName).Length; } catch { }
        _rvtMb = len / 1048576.0;
        _survey = Survey.Collect(doc);
        _model = BuildModel();
        _ifc.Items.Add("IFC4 Reference View"); _ifc.Items.Add("IFC2x3 Coordination View 2.0"); _ifc.SelectedIndex = 0;
        _out.Text = LoadPrefs()?.Out ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FreeField");
        BuildUi();
        Reload(applyPrefs: true);
        _search.TextChanged += (_, _) => RefreshFilters();
        _levelFilter.SelectionChanged += (_, _) => RefreshFilters();
        _ceilingArea.Click += (_, _) => { if (!_running) Reload(false); };
        _go.Click += (_, _) => { try { RunExport(true); } catch (OperationCanceledException) { _status.Text = "Cancelled"; } catch (Exception ex) { _status.Text = "Failed: " + ex.Message; Automation.Log.Write("export failed: " + ex); } };
        _cancel.Click += (_, _) => { _cancelRequested = true; _status.Text = "Cancelling after the current stage..."; };
        Closing += (_, e) => { if (_running) { e.Cancel = true; _cancelRequested = true; } };
    }

    SheetSelectionModel BuildModel() =>
        new(_survey.Sheets, _survey.UnplacedViews, _survey.Levels, new SelectionOptions { MapCeilingAndAreaPlans = _ceilingArea.IsChecked == true });

    // ---- UI construction -------------------------------------------------------------------
    DataGrid MakeGrid(ObservableCollection<RowVm> items, double height, bool view)
    {
        var g = new DataGrid
        {
            ItemsSource = items, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, IsReadOnly = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            SelectionMode = DataGridSelectionMode.Single, MinHeight = 60, Height = height, RowHeaderWidth = 0,
        };
        var cb = new FrameworkElementFactory(typeof(CheckBox));
        cb.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(RowVm.Selected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        cb.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        g.Columns.Add(new DataGridTemplateColumn { Header = "", Width = 34, CellTemplate = new DataTemplate { VisualTree = cb } });
        g.Columns.Add(new DataGridTextColumn { Header = view ? "Type" : "Sheet", Binding = new Binding(nameof(RowVm.Number)), IsReadOnly = true, Width = 84 });
        g.Columns.Add(new DataGridTextColumn { Header = view ? "View name" : "Sheet name", Binding = new Binding(nameof(RowVm.Name)), IsReadOnly = true, Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
        if (!view) g.Columns.Add(new DataGridTextColumn { Header = "Floor plan view(s)", Binding = new Binding(nameof(RowVm.Plans)), IsReadOnly = true, Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        g.Columns.Add(new DataGridTextColumn { Header = "Level → IFC storey", Binding = new Binding(nameof(RowVm.Levels)), IsReadOnly = true, Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        var st = new FrameworkElementFactory(typeof(TextBlock));
        st.SetBinding(TextBlock.TextProperty, new Binding(nameof(RowVm.Status)));
        st.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(RowVm.StatusBrush)));
        st.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0));
        st.SetBinding(TextBlock.ToolTipProperty, new Binding(nameof(RowVm.Status)));
        st.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        g.Columns.Add(new DataGridTemplateColumn { Header = "Status", Width = new DataGridLength(view ? 3 : 1.6, DataGridLengthUnitType.Star), CellTemplate = new DataTemplate { VisualTree = st } });
        AutomationProperties.SetName(g, view ? "Floor plan views not on sheets" : "Sheets");
        return g;
    }

    static TextBlock H(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 3) };

    void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        // bottom: progress + buttons + result
        var bottom = new StackPanel();
        bottom.Children.Add(_bar); bottom.Children.Add(_status);
        _result.Children.Add(_resultText);
        var rbtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var open = new Button { Content = "Open folder", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) => { try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_lastBundle}\""); } catch { } };
        var copy = new Button { Content = "Copy bundle path", Padding = new Thickness(10, 2, 10, 2) };
        copy.Click += (_, _) => { try { Clipboard.SetText(_lastBundle); _status.Text = "Path copied"; } catch { } };
        AutomationProperties.SetName(open, "Open folder"); AutomationProperties.SetName(copy, "Copy bundle path");
        rbtns.Children.Add(open); rbtns.Children.Add(copy); _result.Children.Add(rbtns);
        bottom.Children.Add(_result);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        btns.Children.Add(_go); btns.Children.Add(_cancel); btns.Children.Add(_close);
        bottom.Children.Add(btns);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);

        // top bar
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        top.Children.Add(new TextBlock { Text = "Search ", VerticalAlignment = VerticalAlignment.Center }); top.Children.Add(_search);
        top.Children.Add(new TextBlock { Text = "   Level ", VerticalAlignment = VerticalAlignment.Center }); top.Children.Add(_levelFilter);
        var all = new Button { Content = "Select all mappable", Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        var clr = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        all.Click += (_, _) => { _model.SelectAllMappable(); SyncRowsFromModel(); UpdateSummary(); };
        clr.Click += (_, _) => { _model.Clear(); SyncRowsFromModel(); UpdateSummary(); };
        top.Children.Add(all); top.Children.Add(clr); top.Children.Add(_summary);
        AutomationProperties.SetName(_search, "Search"); AutomationProperties.SetName(_levelFilter, "Level filter");
        AutomationProperties.SetName(all, "Select all mappable"); AutomationProperties.SetName(clr, "Clear");   // the summary / status / result TextBlocks keep their text as UIA Name so scripts can read them
        AutomationProperties.SetName(_go, "Export"); AutomationProperties.SetName(_cancel, "Cancel"); AutomationProperties.SetName(_close, "Close");
        AutomationProperties.SetName(_out, "Output folder"); AutomationProperties.SetName(_ifc, "IFC version");
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

        // scrolling middle
        var mid = new StackPanel();
        var mainGrid = MakeGrid(_mainRows, 250, false);
        mid.Children.Add(H("Sheets"));
        mid.Children.Add(mainGrid);
        var otherGrid = MakeGrid(_otherRows, 180, false);
        var otherExp = new Expander { Header = "Other sheets (not mapped)", IsExpanded = false, Content = otherGrid, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(otherExp, "Other sheets (not mapped)");
        mid.Children.Add(otherExp);
        var viewGrid = MakeGrid(_viewRows, 150, true);
        var viewExp = new Expander { Header = "Floor plan views not on any sheet", IsExpanded = true, Content = viewGrid, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(viewExp, "Floor plan views not on any sheet");
        mid.Children.Add(viewExp);

        var levelGrid = new DataGrid { ItemsSource = _levelRows, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 150, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0 };
        levelGrid.Columns.Add(new DataGridTextColumn { Header = "Revit level", Binding = new Binding(nameof(LevelVm.Level)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        levelGrid.Columns.Add(new DataGridTextColumn { Header = "IFC storey", Binding = new Binding(nameof(LevelVm.Storey)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        levelGrid.Columns.Add(new DataGridTextColumn { Header = "Elevation (m)", Binding = new Binding(nameof(LevelVm.Elevation)), Width = 100 });
        levelGrid.Columns.Add(new DataGridTextColumn { Header = "Floor plan on a selected sheet", Binding = new Binding(nameof(LevelVm.Plan)), Width = 200 });
        AutomationProperties.SetName(levelGrid, "Levels");
        var levelsExp = new Expander { Header = "Levels (Revit level → IFC storey, read-only)", IsExpanded = false, Content = levelGrid, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(levelsExp, "Levels panel");
        mid.Children.Add(levelsExp);

        var opts = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var r1 = new StackPanel { Orientation = Orientation.Horizontal };
        r1.Children.Add(_schedules); r1.Children.Add(_outlines); r1.Children.Add(_ceilingArea);
        r1.Children.Add(new TextBlock { Text = "IFC ", VerticalAlignment = VerticalAlignment.Center }); r1.Children.Add(_ifc);
        opts.Children.Add(r1);
        var r2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        r2.Children.Add(new TextBlock { Text = "Output folder ", VerticalAlignment = VerticalAlignment.Center }); r2.Children.Add(_out);
        var browse = new Button { Content = "Browse...", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        browse.Click += (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _out.Text }; if (d.ShowDialog() == true) _out.Text = d.FolderName; };
        r2.Children.Add(browse); opts.Children.Add(r2);
        mid.Children.Add(new Expander { Header = "Options", IsExpanded = true, Content = opts, Margin = new Thickness(0, 6, 0, 0) });

        root.Children.Add(new ScrollViewer { Content = mid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    // ---- model <-> rows --------------------------------------------------------------------
    RowVm VmOf(SheetRow r) => new()
    {
        Key = r.Sheet.Number, Number = r.Sheet.Number, Name = r.Sheet.Name, Plans = r.PlansText, Levels = r.LevelsText,
        Status = r.StatusText, MapStatus = r.Status, IsSheet = true, LevelNames = r.Plans.Select(p => p.LevelName).ToList(),
    };

    void Reload(bool applyPrefs)
    {
        _suspend = true;
        _model = BuildModel();
        var prefs = applyPrefs ? LoadPrefs() : null;
        _mainRows.Clear(); _otherRows.Clear(); _viewRows.Clear();
        foreach (var r in _model.Sheets)
        {
            var vm = VmOf(r); vm.SetSilently(r.Selected); vm.OnChanged = OnRowChanged;
            if (prefs?.Sheets != null && prefs.Sheets.TryGetValue(r.Sheet.Number, out var was)) { vm.SetSilently(was); r.Selected = was; }
            (r.Status == MapStatus.NotMappable ? _otherRows : _mainRows).Add(vm);
        }
        foreach (var v in _model.Views)
        {
            var vm = new RowVm { Key = v.ViewId, Number = v.ViewType.Replace("Plan", ""), Name = v.ViewName, Plans = v.ViewName, Levels = v.StoreyName == null ? v.LevelName ?? "" : v.LevelName + " → " + v.StoreyName,
                Status = v.StatusText, MapStatus = v.Status, OnChanged = OnRowChanged, LevelNames = new[] { v.LevelName } };
            vm.SetSilently(v.Selected);
            if (prefs?.Views != null && prefs.Views.TryGetValue(v.ViewId, out var was)) { vm.SetSilently(was); v.Selected = was; }
            _viewRows.Add(vm);
        }
        _levelFilter.Items.Clear(); _levelFilter.Items.Add("(all levels)");
        foreach (var l in _model.Levels) _levelFilter.Items.Add(l.Name);
        _levelFilter.SelectedIndex = 0;
        _mainView = CollectionViewSource.GetDefaultView(_mainRows); _otherView = CollectionViewSource.GetDefaultView(_otherRows); _viewsView = CollectionViewSource.GetDefaultView(_viewRows);
        _suspend = false;
        _model.RefreshLevels();
        RefreshFilters(); UpdateSummary();
    }

    void OnRowChanged(RowVm vm)
    {
        if (_suspend) return;
        if (vm.IsSheet) { var r = _model.Sheets.FirstOrDefault(x => x.Sheet.Number == vm.Key); if (r != null) r.Selected = vm.Selected; }
        else { var r = _model.Views.FirstOrDefault(x => x.ViewId == vm.Key); if (r != null) r.Selected = vm.Selected; }
        _model.RefreshLevels(); UpdateSummary();
    }

    void SyncRowsFromModel()
    {
        _suspend = true;
        foreach (var vm in _mainRows.Concat(_otherRows)) vm.SetSilently(_model.Sheets.First(x => x.Sheet.Number == vm.Key).Selected);
        foreach (var vm in _viewRows) vm.SetSilently(_model.Views.First(x => x.ViewId == vm.Key).Selected);
        _suspend = false;
    }

    void RefreshFilters()
    {
        if (_mainView == null) return;
        string? lvl = _levelFilter.SelectedIndex > 0 ? _levelFilter.SelectedItem as string : null;
        Predicate<object> f = o => o is RowVm r && SheetSelectionModel.Matches(_search.Text, lvl, r.Number, r.Name, r.LevelNames);
        _mainView.Filter = f; _otherView!.Filter = f; _viewsView!.Filter = f;
    }

    void UpdateSummary()
    {
        _summary.Text = _model.SummaryLine(_rvtMb);
        _go.IsEnabled = !_running && _model.HasSelection;
        _levelRows.Clear();
        foreach (var l in _model.Levels)
            _levelRows.Add(new LevelVm { Level = l.Name, Storey = l.StoreyName ?? "(not exported)" + (string.IsNullOrEmpty(l.NotExportedReason) ? "" : " - " + l.NotExportedReason), Elevation = l.ElevationM.ToString("0.00"), Plan = l.HasPlanOnSelection ? "✓" : "✗" });
    }

    // ---- prefs -----------------------------------------------------------------------------
    sealed class Prefs { public string? Out { get; set; } public Dictionary<string, bool>? Sheets { get; set; } public Dictionary<string, bool>? Views { get; set; } }
    Prefs? LoadPrefs()
    {
        try
        {
            if (!File.Exists(PrefsPath)) return null;
            var all = JsonSerializer.Deserialize<Dictionary<string, Prefs>>(File.ReadAllText(PrefsPath));
            return all != null && all.TryGetValue(DocKey, out var p) ? p : null;
        }
        catch { return null; }
    }
    void SavePrefs()
    {
        try
        {
            var all = new Dictionary<string, Prefs>();
            try { if (File.Exists(PrefsPath)) all = JsonSerializer.Deserialize<Dictionary<string, Prefs>>(File.ReadAllText(PrefsPath)) ?? all; } catch { }
            all[DocKey] = new Prefs { Out = _out.Text, Sheets = _model.Sheets.ToDictionary(s => s.Sheet.Number, s => s.Selected), Views = _model.Views.ToDictionary(v => v.ViewId, v => v.Selected) };
            Directory.CreateDirectory(Path.GetDirectoryName(PrefsPath)!);
            File.WriteAllText(PrefsPath, JsonSerializer.Serialize(all));
        }
        catch { }
    }

    // ---- export ----------------------------------------------------------------------------
    public string OutputFolder { get => _out.Text; set => _out.Text = value; }
    public string StatusText => _status.Text;
    public SheetSelectionModel Model => _model;

    /// <summary>The export path the Export button uses (also used by the headless dialog smoke test). Throws on failure/cancel.</summary>
    public ExportResult RunExport(bool openFolder)
    {
        if (!_model.HasSelection) throw new InvalidOperationException("nothing selected");
        var opt = new ExportOptions
        {
            Sheets = _model.SelectedSheets.Select(s => s.Sheet.Number).ToHashSet(StringComparer.OrdinalIgnoreCase),
            PlanViews = _model.SelectedViews.Select(v => v.ViewId).ToHashSet(),
            IncludeSheets = _model.SelectedSheets.Any(), IncludePlanViewsNotOnSheets = _model.SelectedViews.Any(),
            IncludeSchedules = _schedules.IsChecked == true, IncludeOutlines = _outlines.IsChecked == true,
            MapCeilingAndAreaPlans = _ceilingArea.IsChecked == true,
            IfcVersion = _ifc.SelectedIndex == 0 ? "IFC4RV" : "IFC2x3CV2", OutputPath = _out.Text,
            Cancel = () => _cancelRequested,
        };
        _running = true; _cancelRequested = false; _go.IsEnabled = false; _cancel.IsEnabled = true; _close.IsEnabled = false; _result.Visibility = System.Windows.Visibility.Collapsed;
        SavePrefs();
        try
        {
            var r = ExportPipeline.Export(_doc, opt, (p, m) =>
            {
                _bar.Value = p; _status.Text = m;
                Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));   // keep Cancel / window responsive
            });
            _lastBundle = r.BundlePath;
            _status.Text = "Done";   // details are shown once, in the result panel below
            _resultText.Text = $"{r.Sheets} pages ({r.PlanViewPages} from views), {r.Viewports} viewports, {r.Elements} elements, {r.Links} links, {r.Schedules} schedules, {r.Warnings.Count} warnings.\n{r.BundlePath}";
            _result.Visibility = System.Windows.Visibility.Visible;
            if (openFolder) { try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{r.BundlePath}\""); } catch { } }
            return r;
        }
        finally { _running = false; _cancel.IsEnabled = false; _close.IsEnabled = true; UpdateSummary(); }
    }
}
