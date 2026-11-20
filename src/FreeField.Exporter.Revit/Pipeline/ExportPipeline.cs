using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FreeField.Exporter.Core;
using FreeField.Exporter.Core.Geometry;
using FreeField.Exporter.Core.Model;
using FreeField.Exporter.Revit.Automation;

namespace FreeField.Exporter.Revit.Pipeline;

public sealed class ExportResult
{
    public string BundlePath = "";
    public Dictionary<string, long> TimingsMs = new();
    public List<string> Warnings = new();
    public int Sheets, Viewports, Elements, Links, Schedules, PlanViewPages;
}

/// <summary>Full export per SPEC §5. Runs on the Revit UI thread (inside an API context).</summary>
public static class ExportPipeline
{
    sealed class Page
    {
        public string Number = "", Name = "", Id = "";
        public ViewSheet Sheet = null!;
        public bool Temp;
        public View? SourceView;
        public string? Pdf;
        public List<Viewport> Viewports = new();
    }

    // ---- headless entry -------------------------------------------------------------------
    public static void Run(UIApplication uiapp, UIDocument uidoc, Job job, Dictionary<string, object?> result)
    {
        var opt = new ExportOptions
        {
            Sheets = job.Sheets == null ? null : new HashSet<string>(job.Sheets),
            IfcVersion = job.Opt("ifcVersion", "IFC4RV"),
            IncludeSchedules = job.Opt("schedules", "true") != "false",
            IncludeOutlines = job.Opt("outlines", "true") != "false",
            IncludeSheets = job.Opt("includeSheets", "true") != "false",
            IncludePlanViewsNotOnSheets = job.Opt("planViews", "true") != "false",
            OutputPath = job.Output ?? "",
            TempTitleBlock = job.Opt("tempTb", "none"),
            Discipline = job.Options != null && job.Options.TryGetValue("discipline", out var d) ? d : null,
        };
        if (job.Opt("createFixtureSheets") == "true") result["fixture"] = Spike.Fixtures.CreateFixtureSheets(uidoc.Document, int.Parse(job.Opt("fixtureCount", "9999")));
        ExportResult r;
        if (job.Opt("dialogSmoke") == "true")
        {
            // Exercises the UI code path: construct the dialog, keep its default selections, run what the Export button runs, close.
            var dlg = new ExportDialog(uidoc.Document) { OutputFolder = job.Output ?? "" };
            r = dlg.RunExport(false);
            result["dialogSmoke"] = dlg.StatusText;
            dlg.Close();
        }
        else r = Export(uidoc.Document, opt, (p, m) => Log.Write($"[{p}%] {m}"));
        result["bundle"] = r.BundlePath; result["timingsMs"] = r.TimingsMs; result["warnings"] = r.Warnings;
        result["counts"] = new { r.Sheets, r.Viewports, r.Elements, r.Links, r.Schedules, r.PlanViewPages };
    }

    // ---- main -----------------------------------------------------------------------------
    public static ExportResult Export(Document doc, ExportOptions opt, Action<int, string> progress)
    {
        var res = new ExportResult();
        var sw = Stopwatch.StartNew();
        void Stage(string name) { res.TimingsMs[name] = sw.ElapsedMilliseconds; sw.Restart(); }
        void Warn(string w) { res.Warnings.Add(w); Log.Write("WARN " + w); }

        var projTitle = Path.GetFileNameWithoutExtension(doc.PathName);
        if (string.IsNullOrEmpty(projTitle)) projTitle = doc.Title;
        var bundlePath = ResolveBundlePath(opt.OutputPath, projTitle);
        var work = Path.Combine(Path.GetTempPath(), "ffexp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(work);
        res.BundlePath = bundlePath;
        try
        {
            // 1. collect
            progress(2, "Collecting sheets and views");
            var onSheetViewIds = new HashSet<long>();
            var allSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber).ToList();
            var pages = new List<Page>();
            foreach (var s in allSheets)
            {
                var vps = s.GetAllViewports().Select(id => doc.GetElement(id)).OfType<Viewport>().ToList();
                foreach (var vp in vps) onSheetViewIds.Add(vp.ViewId.Value);
                bool hasView = vps.Any(vp => doc.GetElement(vp.ViewId) is View v && v.ViewType != ViewType.Legend);
                bool want = opt.IncludeSheets && (opt.Sheets == null ? hasView : opt.Sheets.Contains(s.SheetNumber));
                if (want) pages.Add(new Page { Number = s.SheetNumber, Name = s.Name, Id = s.UniqueId, Sheet = s, Viewports = vps });
            }
            var planViews = new List<ViewPlan>();
            if (opt.IncludePlanViewsNotOnSheets)
            {
                foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>())
                {
                    if (v.IsTemplate || !IsPlan(v.ViewType) || onSheetViewIds.Contains(v.Id.Value) || v.GenLevel == null && v.ViewType != ViewType.AreaPlan) continue;
                    if (opt.PlanViews != null && !opt.PlanViews.Contains(v.Id.Value)) continue;
                    planViews.Add(v);
                }
                planViews = planViews.OrderBy(v => v.Name).ToList();
            }
            Stage("collect");

            // 2. IFC
            progress(5, "Exporting IFC");
            var ifcName = "model";
            if (!IfcTools.Export(doc, work, ifcName, opt.IfcVersion, out var ifcErr)) throw new InvalidOperationException("IFC export failed: " + ifcErr);
            var ifcPath = Path.Combine(work, ifcName + ".ifc");
            if (!File.Exists(ifcPath)) throw new InvalidOperationException("IFC export produced no file");
            Stage("ifc");
            var ifc = IfcTools.Scan(ifcPath);
            Stage("ifcScan");

            // 3. temp sheets for unplaced plan views (rolled back at the end)
            TransactionGroup? tg = null;
            var usedNumbers = new HashSet<string>(pages.Select(p => p.Number), StringComparer.OrdinalIgnoreCase);
            try
            {
                if (planViews.Count > 0)
                {
                    tg = new TransactionGroup(doc, "FreeField temp sheets");
                    tg.Start();
                    var tb = opt.TempTitleBlock != "largest" ? ElementId.InvalidElementId : PickTitleBlock(doc);
                    foreach (var v in planViews)
                    {
                        using var t = new Transaction(doc, "temp sheet");
                        t.Start();
                        var sh = ViewSheet.Create(doc, tb);
                        var num = "VIEW-" + BundleNaming.SanitizeFileStem(v.Name);
                        num = BundleNaming.MakeUnique(num, usedNumbers);
                        var o = sh.Outline;
                        var vp = Viewport.Create(doc, sh.Id, v.Id, new XYZ((o.Min.U + o.Max.U) / 2, (o.Min.V + o.Max.V) / 2, 0));
                        var bo = vp.GetBoxOutline(); var c = vp.GetBoxCenter();
                        vp.SetBoxCenter(new XYZ(c.X + ((o.Min.U + o.Max.U) / 2 - (bo.MinimumPoint.X + bo.MaximumPoint.X) / 2), c.Y + ((o.Min.V + o.Max.V) / 2 - (bo.MinimumPoint.Y + bo.MaximumPoint.Y) / 2), c.Z));
                        t.Commit();
                        var nbo = vp.GetBoxOutline();
                        if (tb != ElementId.InvalidElementId && (nbo.MaximumPoint.X - nbo.MinimumPoint.X > o.Max.U - o.Min.U + 1e-6 || nbo.MaximumPoint.Y - nbo.MinimumPoint.Y > o.Max.V - o.Min.V + 1e-6))
                            Warn($"plan view '{v.Name}' is larger than the temporary sheet and will be clipped");
                        pages.Add(new Page { Number = num, Name = v.Name, Id = "view:" + v.UniqueId, Sheet = sh, Temp = true, SourceView = v, Viewports = new List<Viewport> { vp } });
                        res.PlanViewPages++;
                    }
                }
                Stage("tempSheets");

                // 4. PDFs
                int pi = 0;
                foreach (var pg in pages)
                {
                    progress(10 + 40 * pi++ / Math.Max(1, pages.Count), $"PDF {pg.Number}");
                    pg.Pdf = ExportPdf(doc, pg.Sheet, work, "p" + pi);
                    if (pg.Pdf == null) Warn($"PDF export failed for {pg.Number}");
                }
                pages = pages.Where(p => p.Pdf != null).ToList();
                Stage("pdf");

                // 5. geometry + elements + links
                var modelId = opt.ModelId ?? BundleNaming.Slug(projTitle);
                var discipline = opt.Discipline ?? InferDiscipline(doc);
                var rb = new RecordBuilder(doc, modelId);
                var records = new Dictionary<string, ElementRecord>(StringComparer.Ordinal);
                var manifest = new BundleManifest();
                var indexes = new List<SheetElementIndex>();
                var viewToPage = pages.Where(p => p.Temp).ToDictionary(p => p.SourceView!.Id.Value, p => p);
                foreach (var p in pages.Where(p => !p.Temp)) foreach (var vp in p.Viewports) viewToPage[vp.ViewId.Value] = p;
                int done = 0;
                var markerMap = BuildMarkerMap(doc); Log.Write("view markers mapped: " + markerMap.Count);
                var pagesByNum = new Dictionary<string, Page>();
                foreach (var pg in pages)
                {
                    progress(50 + 35 * done++ / Math.Max(1, pages.Count), $"Mapping {pg.Number}");
                    BuildPage(doc, pg, ifc, modelId, rb, records, viewToPage, markerMap, manifest, indexes, Warn, opt);
                }
                Stage("geometry");
                var storeysUsed = manifest.Sheets.SelectMany(s => s.Viewports).Select(v => v.StoreyName).Where(n => n != null).Distinct().ToList();
                Log.Write($"storeys used by viewports: {string.Join(", ", storeysUsed)}");

                // 6. schedules
                var schedules = new List<(ScheduleEntry, DataTable)>();
                var placedMap = new Dictionary<long, List<string>>();
                foreach (var pg in pages.Where(x => !x.Temp))
                    foreach (var ssi in new FilteredElementCollector(doc, pg.Sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                    {
                        if (!placedMap.TryGetValue(ssi.ScheduleId.Value, out var l)) placedMap[ssi.ScheduleId.Value] = l = new List<string>();
                        if (!l.Contains(pg.Id)) l.Add(pg.Id);
                    }
                if (tg != null) { tg.RollBack(); tg = null; }
                if (opt.IncludeSchedules) { progress(86, "Schedules"); schedules = ScheduleExporter.Export(doc, ifc.GlobalIds, records, rb, Warn, placedMap); }
                Stage("schedules");

                // 7. manifest + bundle
                progress(90, "Writing bundle");
                var pi2 = doc.ProjectInformation;
                manifest.Generator = new GeneratorInfo { Host = "Revit", HostVersion = doc.Application.VersionNumber };
                manifest.Project = new ProjectInfo { Name = pi2?.Name ?? "", Number = pi2?.Number ?? "", SourceFile = Path.GetFileName(doc.PathName) };
                manifest.Coordinates = new CoordinatesInfo { ModelSpace = "ifc", IfcPlacement = "internal", TrueNorthRad = TrueNorth(doc) };
                var model = new ModelEntry { Id = modelId, Name = projTitle, Discipline = discipline };
                foreach (var lvl in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation))
                {
                    if (!ifc.StoreyGuidByName.TryGetValue(lvl.Name, out var sg)) { Warn($"level '{lvl.Name}' has no IfcBuildingStorey of the same name"); continue; }
                    model.Storeys.Add(new StoreyEntry { Name = lvl.Name, Elevation = lvl.Elevation * PageMap.MetresPerFt, LevelUniqueId = lvl.UniqueId, IfcGlobalId = sg });
                }
                manifest.Models.Add(model);
                using (var bw = new BundleWriter(bundlePath))
                {
                    bw.AddModelFile(modelId, ifcPath);
                    foreach (var s in manifest.Sheets) bw.AddSheetPdf(s, pages.First(p => p.Id == s.Id).Pdf!);
                    bw.Write(manifest, records, indexes, schedules);
                }
                res.Sheets = manifest.Sheets.Count; res.Viewports = manifest.Sheets.Sum(s => s.Viewports.Count);
                res.Elements = records.Count; res.Links = indexes.Sum(i => i.Links.Count); res.Schedules = schedules.Count;
                Stage("bundle");
                progress(100, "Done");
            }
            finally
            {
                if (tg != null && tg.HasStarted() && !tg.HasEnded()) tg.RollBack();
            }
        }
        finally { try { Directory.Delete(work, true); } catch { } }
        return res;
    }

    // ---- per page -------------------------------------------------------------------------
    static void BuildPage(Document doc, Page pg, IfcInfo ifc, string modelId, RecordBuilder rb, Dictionary<string, ElementRecord> records,
        Dictionary<long, Page> viewToPage, Dictionary<long, View> markerMap, BundleManifest manifest, List<SheetElementIndex> indexes, Action<string> warn, ExportOptions opt)
    {
        var pdfInfo = PdfTools.GetPageInfo(pg.Pdf!);
        double W = pdfInfo.WidthPt, H = pdfInfo.HeightPt;
        var o = pg.Sheet.Outline;
        double cx = (o.Min.U + o.Max.U) / 2, cy = (o.Min.V + o.Max.V) / 2;
        // Centre alignment holds even when the page is larger than the outline (sheets without titleblock get a symmetric margin).
        if (W + 3 < (o.Max.U - o.Min.U) * PageMap.PtPerFt || H + 3 < (o.Max.V - o.Min.V) * PageMap.PtPerFt || (!pg.Temp && (Math.Abs((o.Max.U - o.Min.U) * PageMap.PtPerFt - W) > 3 || Math.Abs((o.Max.V - o.Min.V) * PageMap.PtPerFt - H) > 3)))
            warn($"{pg.Number}: PDF page {W:F1}x{H:F1}pt differs from sheet outline {(o.Max.U - o.Min.U) * PageMap.PtPerFt:F1}x{(o.Max.V - o.Min.V) * PageMap.PtPerFt:F1}pt; mapping may be off");
        var entry = new SheetEntry { Id = pg.Id, Number = pg.Number, Name = pg.Name, Page = new PageSize { WidthPt = W, HeightPt = H } };
        var index = new SheetElementIndex { SheetId = pg.Id };
        var seen = new HashSet<(string, string)>();
        var linkSeen = new HashSet<string>();
        var vpEntries = new List<(ViewportEntry E, double Area)>();

        foreach (var vp in pg.Viewports)
        {
            if (doc.GetElement(vp.ViewId) is not View view) continue;
            if (view.ViewType == ViewType.Legend || view.ViewType == ViewType.Schedule) { }
            Transform m2p = Transform.Identity;
            try { var tw = view.GetModelToProjectionTransforms(); if (tw.Count > 0) m2p = tw[0].GetModelToProjectionTransform(); } catch { }
            var map = new PageMap(m2p, vp.GetProjectionToSheetTransform(), cx, cy, W, H);
            var bo = vp.GetBoxOutline();
            var c1 = map.SheetToPage(bo.MinimumPoint.X, bo.MaximumPoint.Y); var c2 = map.SheetToPage(bo.MaximumPoint.X, bo.MinimumPoint.Y);
            var region = new BBox2(Math.Min(c1.X, c2.X), Math.Min(c1.Y, c2.Y), Math.Max(c1.X, c2.X), Math.Max(c1.Y, c2.Y));
            var isPlan = IsPlan(view.ViewType);
            var ve = new ViewportEntry
            {
                Id = vp.UniqueId, ViewId = view.UniqueId, ViewName = view.Name, ViewType = view.ViewType.ToString(), Scale = view.Scale,
                PdfRegion = region.ToArray(),
            };
            if (isPlan)
            {
                var lvl = (view as ViewPlan)?.GenLevel;
                var a = map.PdfToModelMetres();
                if (lvl != null && a != null && ifc.StoreyGuidByName.ContainsKey(lvl.Name))
                {
                    ve.ModelId = modelId; ve.StoreyName = lvl.Name; ve.PdfToModel = a.Value.ToArray();
                    ve.ControlPoints = ViewportMath.ControlPointsFor(a.Value, region);
                }
                else warn($"{pg.Number}/{view.Name}: no calibration (level '{lvl?.Name}' not in IFC storeys or singular transform)");
            }
            vpEntries.Add((ve, region.Width * region.Height));

            bool perspective = view is View3D v3 && v3.IsPerspective;
            if (view.ViewType is ViewType.Legend or ViewType.Schedule or ViewType.DraftingView or ViewType.Rendering || perspective) continue;

            // elements
            foreach (var e in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                try
                {
                    var cat = e.Category;
                    if (cat == null || cat.CategoryType != CategoryType.Model || e is ImportInstance || e is RevitLinkInstance) continue;
                    var gid = IfcTools.GlobalIdOf(e);
                    if (gid.Length == 0 || !ifc.GlobalIds.Contains(gid)) continue;
                    var bb = e.get_BoundingBox(view) ?? e.get_BoundingBox(null);
                    if (bb == null) continue;
                    var pb = map.ProjectBox(bb);
                    if (pb.MaxX < region.MinX || pb.MinX > region.MaxX || pb.MaxY < region.MinY || pb.MinY > region.MaxY) continue;
                    if (!seen.Add((gid, ve.Id))) continue;
                    if (!records.ContainsKey(gid)) records[gid] = rb.Build(e, gid);
                    index.Elements.Add(new SheetElement { Gid = gid, Vp = ve.Id, Bbox = pb.ToArray(), Outline = isPlan && opt.IncludeOutlines ? Outlines.Build(e, map) : null });
                }
                catch { }
            }

            // links
            foreach (var l in Links(doc, view, map, viewToPage, markerMap, region, pg))
                if (linkSeen.Add(l.TargetViewId + "|" + l.Kind + "|" + (int)l.Bbox[0] + "," + (int)l.Bbox[1])) index.Links.Add(l);
        }

        // primary per (sheet, storey): largest region
        foreach (var grp in vpEntries.Where(v => v.E.StoreyName != null).GroupBy(v => v.E.StoreyName))
            grp.OrderByDescending(v => v.Area).First().E.Primary = true;
        entry.Viewports = vpEntries.Select(v => v.E).ToList();
        manifest.Sheets.Add(entry);
        indexes.Add(index);
    }

    static IEnumerable<SheetLink> Links(Document doc, View view, PageMap map, Dictionary<long, Page> viewToPage, Dictionary<long, View> markerMap, BBox2 region, Page self)
    {
        var found = new List<(ElementId target, string kind, BoundingBoxXYZ bb)>();
        foreach (var e in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
        {
            try
            {
                if (e is ElevationMarker em)
                {
                    var bb = em.get_BoundingBox(view); if (bb == null) continue;
                    for (int i = 0; i < 4; i++) if (!em.IsAvailableIndex(i)) { var vid = em.GetViewId(i); if (vid != ElementId.InvalidElementId) found.Add((vid, "elevation", bb)); }
                }
                else if (markerMap.TryGetValue(e.Id.Value, out var mv) && mv.Id != view.Id)
                {
                    var bb = e.get_BoundingBox(view); if (bb == null) continue;
                    found.Add((mv.Id, "", bb));
                }
                else if (e is View tv && tv.Id != view.Id && !tv.IsTemplate)
                {
                    var bb = e.get_BoundingBox(view); if (bb == null) continue;
                    var kind = tv.IsCallout ? "callout" : tv.ViewType switch { ViewType.Section => "section", ViewType.Elevation => "elevation", ViewType.Detail => "detail", _ => "view" };
                    found.Add((tv.Id, kind, bb));
                }
            }
            catch { }
        }
        foreach (var (target, kind0, bb) in found)
        {
            var kind = kind0;
        {
            if (!viewToPage.TryGetValue(target.Value, out var tp) || tp == self) continue;
            var pb = map.ProjectBox(bb);
            if (pb.MaxX < region.MinX || pb.MinX > region.MaxX || pb.MaxY < region.MinY || pb.MinY > region.MaxY) continue;
            var tvw = doc.GetElement(target) as View;
            if (kind.Length == 0 && tvw != null) kind = tvw.IsCallout ? "callout" : tvw.ViewType switch { ViewType.Section => "section", ViewType.Elevation => "elevation", ViewType.Detail => "detail", _ => "view" };
            yield return new SheetLink { Kind = kind, Bbox = pb.ToArray(), TargetSheetId = tp.Id, TargetSheetNumber = tp.Number, TargetViewId = tvw?.UniqueId };
        }
        }
    }

    /// <summary>Section/detail/callout/camera marker element id -> the view it opens (markers are not View elements themselves).</summary>
    static Dictionary<long, View> BuildMarkerMap(Document doc)
    {
        var map = new Dictionary<long, View>();
        var cats = new[] { BuiltInCategory.OST_Viewers, BuiltInCategory.OST_Callouts, BuiltInCategory.OST_Cameras };
        foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
        {
            if (v.IsTemplate || v.ViewType is ViewType.ProjectBrowser or ViewType.SystemBrowser or ViewType.Internal) continue;
            foreach (var c in cats)
            {
                try { foreach (var id in v.GetDependentElements(new ElementCategoryFilter(c))) map[id.Value] = v; } catch { }
            }
        }
        return map;
    }

    // ---- helpers --------------------------------------------------------------------------
    public static bool IsPlan(ViewType t) => t is ViewType.FloorPlan or ViewType.EngineeringPlan or ViewType.CeilingPlan or ViewType.AreaPlan;

    static string? ExportPdf(Document doc, ViewSheet sheet, string work, string stem)
    {
        var tmp = Path.Combine(work, stem);
        Directory.CreateDirectory(tmp);
        var opt = new PDFExportOptions
        {
            Combine = false, ZoomType = ZoomType.Zoom, ZoomPercentage = 100, PaperFormat = ExportPaperFormat.Default,
            PaperPlacement = PaperPlacementType.Center, ExportQuality = PDFExportQualityType.DPI600, ColorDepth = ColorDepthType.Color,
            RasterQuality = RasterQualityType.High, AlwaysUseRaster = false, HideCropBoundaries = true, HideReferencePlane = true,
            HideScopeBoxes = true, HideUnreferencedViewTags = true, ViewLinksInBlue = false, MaskCoincidentLines = false,
            ReplaceHalftoneWithThinLines = false, StopOnError = false, FileName = stem,
        };
        try { doc.Export(tmp, new List<ElementId> { sheet.Id }, opt); } catch (Exception ex) { Log.Write("pdf export: " + ex.Message); return null; }
        var pdfs = Directory.GetFiles(tmp, "*.pdf");
        return pdfs.Length == 1 ? pdfs[0] : null;
    }

    static ElementId PickTitleBlock(Document doc)
    {
        FamilySymbol? best = null; double bestArea = -1;
        foreach (var s in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Cast<FamilySymbol>())
        {
            var w = s.get_Parameter(BuiltInParameter.SHEET_WIDTH)?.AsDouble() ?? 0; var h = s.get_Parameter(BuiltInParameter.SHEET_HEIGHT)?.AsDouble() ?? 0;
            if (w * h > bestArea) { bestArea = w * h; best = s; }
        }
        if (best != null && !best.IsActive) { using var t = new Transaction(doc, "activate titleblock"); t.Start(); best.Activate(); t.Commit(); }
        return best?.Id ?? ElementId.InvalidElementId;
    }

    static string InferDiscipline(Document doc)
    {
        int Count(BuiltInCategory c) => new FilteredElementCollector(doc).OfCategory(c).WhereElementIsNotElementType().GetElementCount();
        int st = Count(BuiltInCategory.OST_StructuralFraming) + Count(BuiltInCategory.OST_StructuralColumns) + Count(BuiltInCategory.OST_StructuralFoundation);
        int ar = Count(BuiltInCategory.OST_Walls) + Count(BuiltInCategory.OST_Doors) + Count(BuiltInCategory.OST_Windows);
        return st > ar ? "structural" : "architecture";
    }

    static double TrueNorth(Document doc)
    {
        try { return doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle; } catch { return 0; }
    }

    static string ResolveBundlePath(string output, string title)
    {
        if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeFieldExporter", "out");
        if (output.EndsWith(".ffbundle", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(output);
        return Path.GetFullPath(Path.Combine(output, BundleNaming.SanitizeFileStem(title) + ".ffbundle"));
    }
}
