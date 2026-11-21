using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FreeField.Exporter.Revit.Automation;

namespace FreeField.Exporter.Revit.Spike;

/// <summary>
/// Phase-1 spike. Steps (job.options.step, comma list): list | pdf | ifc.  Writes spike-*.json into job.output.
/// pdf: exports one sheet (options.sheet) and dumps expected sheet-space points (grids, titleblock, viewport boxes).
/// </summary>
public static class SpikeRunner
{
    static readonly JsonSerializerOptions J = new() { WriteIndented = true };
    const double Ft = 1.0;

    public static void Run(UIApplication uiapp, UIDocument uidoc, Job job, Dictionary<string, object?> result)
    {
        var doc = uidoc.Document;
        var outDir = job.Output ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeFieldExporter", "out");
        Directory.CreateDirectory(outDir);
        var steps = job.Opt("step", "list").Split(',', StringSplitOptions.RemoveEmptyEntries);
        result["revit"] = uiapp.Application.VersionNumber + " " + uiapp.Application.VersionBuild;
        result["doc"] = doc.Title;
        foreach (var step in steps)
        {
            try
            {
                switch (step)
                {
                    case "list": Save(outDir, "spike-list.json", List(doc)); break;
                    case "rotvp": Save(outDir, "spike-rotvp.json", RotVp(doc)); break;
                    case "viewpdf": Save(outDir, "spike-viewpdf-" + job.Opt("viewId") + ".json", ViewPdf(doc, job, outDir)); break;
                    case "viewelems": Save(outDir, "spike-viewelems.json", ViewElems(doc, job)); break;
                    case "dialog": { var dlg = new ExportDialog(doc); dlg.Show(); dlg.Close(); Save(outDir, "spike-dialog.json", new { ok = true }); break; }
                    case "viewvis": Save(outDir, "spike-viewvis.json", ViewVis(doc, job)); break;
                    case "tempprobe": Save(outDir, "spike-tempprobe.json", TempProbe(doc, job, outDir)); break;
                    case "clickbutton":
                    {
                        // Same effect as clicking the ribbon button: posts the add-in command by its ribbon command id.
                        var cid = RevitCommandId.LookupCommandId("CustomCtrl_%CustomCtrl_%FreeField%Export%ExportBundle");
                        uiapp.PostCommand(cid);
                        Save(outDir, "spike-clickbutton.json", new { posted = true });
                        break;
                    }
                    case "showdialog":
                    {
                        // Opens the real dialog modally (valid API context for its Export button). The result file is written first so a caller
                        // polling for it is released; Entry overwrites it when the dialog closes. Used by tools/scripts/ui-drive.ps1 -ViaJob.
                        if (job.ResultFile != null) File.WriteAllText(job.ResultFile, "{ \"status\": \"dialog-open\" }");
                        new ExportDialog(doc).ShowDialog();
                        break;
                    }
                    case "schedplace":
                        Save(outDir, "spike-schedplace.json", new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                            .Where(i => !i.IsTitleblockRevisionSchedule).Select(i => new { schedule = doc.GetElement(i.ScheduleId)?.Name, sheet = (doc.GetElement(i.OwnerViewId) as ViewSheet)?.SheetNumber }).ToList());
                        break;
                    case "views": Save(outDir, "spike-views.json", Views(doc)); break;
                    case "mksheets": Save(outDir, "spike-mksheets.json", MakeSheets(doc, job, outDir)); break;
                    case "pdf": Save(outDir, "spike-pdf-" + job.Opt("sheet", "x") + ".json", Pdf(doc, job, outDir)); break;
                    case "ifc": Save(outDir, "spike-ifc.json", Ifc(doc, job, outDir)); break;
                }
            }
            catch (Exception ex) { result["error-" + step] = ex.ToString(); }
        }
    }

    static void Save(string dir, string name, object o) => File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(o, J));

    static double[] P(XYZ p) => new[] { p.X, p.Y, p.Z };

    public static bool IsPlan(ViewType t) => t is ViewType.FloorPlan or ViewType.EngineeringPlan or ViewType.CeilingPlan or ViewType.AreaPlan;

    static object List(Document doc)
    {
        var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .OrderBy(s => s.SheetNumber).Select(s =>
            {
                var o = s.Outline;
                return new
                {
                    number = s.SheetNumber, name = s.Name, uid = s.UniqueId, id = s.Id.Value,
                    outline = new[] { o.Min.U, o.Min.V, o.Max.U, o.Max.V },
                    viewports = s.GetAllViewports().Select(id => (Viewport)doc.GetElement(id)).Select(vp =>
                    {
                        var v = (View)doc.GetElement(vp.ViewId);
                        var bo = vp.GetBoxOutline();
                        return new
                        {
                            vpId = vp.Id.Value, viewName = v.Name, type = v.ViewType.ToString(), scale = v.Scale,
                            level = v.GenLevel?.Name,
                            box = new[] { bo.MinimumPoint.X, bo.MinimumPoint.Y, bo.MaximumPoint.X, bo.MaximumPoint.Y },
                            cropActive = v.CropBoxActive, rotated = vp.Rotation.ToString()
                        };
                    }).ToList()
                };
            }).ToList();
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation)
            .Select(l => new { l.Name, elevFt = l.Elevation, uid = l.UniqueId }).ToList();
        var scheds = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
            .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule)
            .Select(s => new { s.Name, cat = doc.GetElement(s.Definition.CategoryId)?.Name ?? Category.GetCategory(doc, s.Definition.CategoryId)?.Name, onSheet = new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().Any(i => i.ScheduleId == s.Id) }).ToList();
        var pi = doc.ProjectInformation;
        return new
        {
            title = doc.Title, path = doc.PathName, project = new { pi.Name, pi.Number },
            sheetCount = sheets.Count, sheets, levels, schedules = scheds,
            units = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId().TypeId,
            trueNorth = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle
        };
    }

    static object Pdf(Document doc, Job job, string outDir)
    {
        var num = job.Opt("sheet");
        var sheet = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().First(s => s.SheetNumber == num);
        var tmp = Path.Combine(outDir, "pdf-tmp-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var placement = job.Opt("placement", "Center");
        var opt = new PDFExportOptions
        {
            Combine = false,
            ZoomType = ZoomType.Zoom,
            ZoomPercentage = 100,
            PaperFormat = ExportPaperFormat.Default,
            PaperPlacement = placement == "LowerLeft" ? PaperPlacementType.LowerLeft : PaperPlacementType.Center,
            ExportQuality = PDFExportQualityType.DPI600,
            ColorDepth = ColorDepthType.Color,
            RasterQuality = RasterQualityType.High,
            AlwaysUseRaster = false,
            HideCropBoundaries = true,
            HideReferencePlane = true,
            HideScopeBoxes = true,
            HideUnreferencedViewTags = true,
            ViewLinksInBlue = false,
            MaskCoincidentLines = false,
            ReplaceHalftoneWithThinLines = false,
            StopOnError = false,
            FileName = "ff-" + num,
        };
        bool ok = doc.Export(tmp, new List<ElementId> { sheet.Id }, opt);
        var pdfs = Directory.GetFiles(tmp, "*.pdf");
        var final = Path.Combine(outDir, "sheet-" + num + "-" + placement + ".pdf");
        if (pdfs.Length == 1) File.Copy(pdfs[0], final, true);

        // expected sheet-space geometry
        var o = sheet.Outline;
        var tbs = new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType()
            .Select(tb => { var bb = tb.get_BoundingBox(sheet); return new { id = tb.Id.Value, name = tb.Name, bb = bb == null ? null : new[] { bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y } }; }).ToList();
        var vps = new List<object>();
        foreach (var vid in sheet.GetAllViewports())
        {
            var vp = (Viewport)doc.GetElement(vid);
            var v = (View)doc.GetElement(vp.ViewId);
            var bo = vp.GetBoxOutline();
            var toSheet = vp.GetProjectionToSheetTransform();
            var m2p = v.GetModelToProjectionTransforms().Select(t => new { t = Tf(t.GetModelToProjectionTransform()), hasBoundary = t.GetBoundary() != null }).ToList();
            var tf0 = v.GetModelToProjectionTransforms().FirstOrDefault()?.GetModelToProjectionTransform() ?? Transform.Identity;
            var pts = new List<object>();
            if (IsPlan(v.ViewType))
            {
                foreach (var g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
                {
                    IEnumerable<Curve> curves;
                    try { curves = g.GetCurvesInView(DatumExtentType.ViewSpecific, v); } catch { curves = Enumerable.Empty<Curve>(); }
                    foreach (var c in curves)
                    {
                        if (c is not Line) continue;
                        foreach (var p in new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
                        {
                            var sp = toSheet.OfPoint(tf0.OfPoint(p));
                            pts.Add(new { grid = g.Name, model = P(p), sheet = P(sp) });
                        }
                    }
                }
            }
            var crop = v.CropBox;
            vps.Add(new
            {
                vpId = vp.Id.Value, view = v.Name, type = v.ViewType.ToString(), scale = v.Scale,
                box = new[] { bo.MinimumPoint.X, bo.MinimumPoint.Y, bo.MaximumPoint.X, bo.MaximumPoint.Y },
                boxCenter = P(vp.GetBoxCenter()), rotation = vp.Rotation.ToString(),
                toSheet = Tf(toSheet), modelToProjection = m2p,
                cropActive = v.CropBoxActive, cropShown = v.CropBoxVisible,
                cropMin = P(crop.Min), cropMax = P(crop.Max), cropTf = Tf(crop.Transform),
                viewDir = P(v.ViewDirection), right = P(v.RightDirection), up = P(v.UpDirection), origin = P(v.Origin),
                gridPoints = pts
            });
        }
        return new
        {
            sheet = num, exportOk = ok, files = pdfs.Select(Path.GetFileName).ToList(), pdf = final,
            outline = new[] { o.Min.U, o.Min.V, o.Max.U, o.Max.V }, titleblocks = tbs, viewports = vps,
            placement
        };
    }

    static object RotVp(Document doc)
    {
        var tb = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().First();
        var src = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel?.Name == "201 SECOND FLOOR");
        using var t = new Transaction(doc, "FF rot fixture");
        t.Start();
        var dupId = src.Duplicate(ViewDuplicateOption.Duplicate);
        var dup = (ViewPlan)doc.GetElement(dupId);
        dup.Name = "FF rotated crop";
        dup.CropBoxActive = true;
        var cb = dup.CropBox; cb.Min = new XYZ(-40, -60, cb.Min.Z); cb.Max = new XYZ(30, 5, cb.Max.Z); dup.CropBox = cb;
        var sh = ViewSheet.Create(doc, tb.Id); sh.SheetNumber = "FF-201"; sh.Name = "Rotated crop";
        var vp = Viewport.Create(doc, sh.Id, dup.Id, new XYZ(0.7, 0.5, 0));
        vp.Rotation = ViewportRotation.Clockwise;
        t.Commit();
        doc.Save();
        return new { sheet = sh.SheetNumber, vp = vp.Id.Value, rot = vp.Rotation.ToString() };
    }

    static object ViewPdf(Document doc, Job job, string outDir)
    {
        var v = (View)doc.GetElement(new ElementId(long.Parse(job.Opt("viewId"))));
        var tmp = Path.Combine(outDir, "vpdf-tmp-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(tmp);
        var opt = new PDFExportOptions { Combine = false, ZoomType = ZoomType.Zoom, ZoomPercentage = 100, PaperFormat = ExportPaperFormat.Default,
            PaperPlacement = PaperPlacementType.Center, HideCropBoundaries = true, HideReferencePlane = true, HideScopeBoxes = true, HideUnreferencedViewTags = true,
            ViewLinksInBlue = false, FileName = "ffv" };
        bool ok = doc.Export(tmp, new List<ElementId> { v.Id }, opt);
        var pdfs = Directory.GetFiles(tmp, "*.pdf");
        var final = Path.Combine(outDir, "view-" + v.Id.Value + ".pdf");
        if (pdfs.Length == 1) File.Copy(pdfs[0], final, true);
        var tf0 = v.GetModelToProjectionTransforms().First().GetModelToProjectionTransform();
        var pts = new List<object>();
        foreach (var g in new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
        {
            IEnumerable<Curve> curves; try { curves = g.GetCurvesInView(DatumExtentType.ViewSpecific, v); } catch { curves = Enumerable.Empty<Curve>(); }
            foreach (var c in curves) { if (c is not Line) continue; foreach (var q in new[] { c.GetEndPoint(0), c.GetEndPoint(1) }) pts.Add(new { grid = g.Name, model = P(q), proj = P(tf0.OfPoint(q)) }); }
        }
        var crop = v.CropBox; var ct = crop.Transform;
        var cc = new[] { crop.Min, new XYZ(crop.Max.X, crop.Min.Y, 0), crop.Max, new XYZ(crop.Min.X, crop.Max.Y, 0) }.Select(q => P(tf0.OfPoint(ct.OfPoint(q)))).ToList();
        return new { view = v.Name, id = v.Id.Value, scale = v.Scale, ok, pdf = final, cropActive = v.CropBoxActive, annoCrop = v.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.AsInteger(), cropCornersProj = cc, pts };
    }

    static object ViewElems(Document doc, Job job)
    {
        var res = new Dictionary<string, object>();
        foreach (var idStr in job.Opt("viewIds").Split(','))
        {
            var v = (View)doc.GetElement(new ElementId(long.Parse(idStr)));
            var g = new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType().GroupBy(e => (e.Category?.Name ?? "-") + " | " + e.GetType().Name).ToDictionary(x => x.Key, x => x.Count());
            res[v.Name] = g;
        }
        var secs = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => v.ViewType is ViewType.Section or ViewType.Elevation or ViewType.Detail || v.IsCallout).Select(v => v.Name + " | " + v.ViewType + " | callout=" + v.IsCallout + " | id=" + v.Id.Value).ToList();
        return new { res, secs, markers = new FilteredElementCollector(doc).OfClass(typeof(ElevationMarker)).Select(m => m.Id.Value).ToList() };
    }

    static object ViewVis(Document doc, Job job)
    {
        var res = new List<object>();
        foreach (var name in job.Opt("viewNames").Split('|'))
        {
            var v = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().FirstOrDefault(x => x.Name == name && !x.IsTemplate);
            if (v == null) { res.Add(new { name, found = false }); continue; }
            var walls = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().ToList();
            var cat = Category.GetCategory(doc, BuiltInCategory.OST_Walls);
            object? ov = null;
            try { var o = v.GetCategoryOverrides(cat.Id); ov = new { halftone = o.Halftone, transp = o.Transparency, lw = o.ProjectionLineWeight, cutLw = o.CutLineWeight }; } catch { }
            res.Add(new
            {
                name, found = true, type = v.ViewType.ToString(), detail = v.DetailLevel.ToString(), style = v.DisplayStyle.ToString(),
                template = v.ViewTemplateId == ElementId.InvalidElementId ? null : doc.GetElement(v.ViewTemplateId)?.Name,
                modelCatsHidden = v.AreModelCategoriesHidden, wallsHidden = SafeHidden(v, cat), wallCount = walls.Count,
                filters = v.GetFilters().Select(f => new { n = doc.GetElement(f)?.Name, vis = v.GetFilterVisibility(f) }).ToList(), wallOverrides = ov,
                discipline = v.Discipline.ToString(), crop = v.CropBoxActive, scale = v.Scale,
                viewRange = (v as ViewPlan) is ViewPlan vp ? new { top = vp.GetViewRange().GetOffset(PlanViewPlane.TopClipPlane), cut = vp.GetViewRange().GetOffset(PlanViewPlane.CutPlane), bottom = vp.GetViewRange().GetOffset(PlanViewPlane.BottomClipPlane), depth = vp.GetViewRange().GetOffset(PlanViewPlane.ViewDepthPlane) } : null
            });
        }
        return res;
    }
    static bool SafeHidden(View v, Category c) { try { return v.GetCategoryHidden(c.Id); } catch { return false; } }

    static object TempProbe(Document doc, Job job, string outDir)
    {
        var res = new List<object>();
        foreach (var name in job.Opt("viewNames").Split('|'))
        {
            var v = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(x => x.Name == name && !x.IsTemplate);
            if (v == null) continue;
            using var tg = new TransactionGroup(doc, "probe"); tg.Start();
            ViewSheet sh; Viewport vp;
            using (var t = new Transaction(doc, "mk")) { t.Start(); sh = ViewSheet.Create(doc, ElementId.InvalidElementId); var o0 = sh.Outline; vp = Viewport.Create(doc, sh.Id, v.Id, new XYZ((o0.Min.U + o0.Max.U) / 2, (o0.Min.V + o0.Max.V) / 2, 0)); t.Commit(); }
            var o = sh.Outline; var bo = vp.GetBoxOutline(); var lo = vp.GetLabelOutline();
            var tmp = Path.Combine(outDir, "probe-" + res.Count); Directory.CreateDirectory(tmp);
            var pdf = Pipeline.ExportPipeline.ExportPdf(doc, sh, tmp, "p" + res.Count);
            if (pdf != null) File.Copy(pdf, Path.Combine(outDir, "probe-" + res.Count + ".pdf"), true);
            res.Add(new { name, outline = new[] { o.Min.U, o.Min.V, o.Max.U, o.Max.V }, box = new[] { bo.MinimumPoint.X, bo.MinimumPoint.Y, bo.MaximumPoint.X, bo.MaximumPoint.Y }, label = new[] { lo.MinimumPoint.X, lo.MinimumPoint.Y, lo.MaximumPoint.X, lo.MaximumPoint.Y }, boxCenter = P(vp.GetBoxCenter()), viewOutline = new[] { v.Outline.Min.U, v.Outline.Min.V, v.Outline.Max.U, v.Outline.Max.V }, p2s = Tf(vp.GetProjectionToSheetTransform()), scale = v.Scale, pdf = "probe-" + (res.Count) + ".pdf" });
            tg.RollBack();
        }
        return res;
    }

    static object Views(Document doc)
    {
        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate)
            .Select(v => new { v.Name, type = v.ViewType.ToString(), id = v.Id.Value, scale = v.Scale, level = v.GenLevel?.Name, crop = v.CropBoxActive,
                grids = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Grid)).GetElementCount(), sheet = v.get_Parameter(BuiltInParameter.VIEWPORT_SHEET_NUMBER)?.AsString() }).ToList();
        var tbs = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Select(e => ((FamilySymbol)e).FamilyName + ":" + e.Name).ToList();
        var scheds = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Select(s => new { s.Name, s.IsTemplate, kind = s.Definition.IsKeySchedule, cat = Category.GetCategory(doc, s.Definition.CategoryId)?.Name }).ToList();
        return new { views, tbs, scheds, links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Select(l => l.Name).ToList() };
    }

    /// <summary>Test fixture only: one sheet per plan view (with first available titleblock), then SaveAs a copy.</summary>
    static object MakeSheets(Document doc, Job job, string outDir)
    {
        var tb = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().FirstOrDefault();
        var made = new List<object>();
        using (var t = new Transaction(doc, "FF fixture sheets"))
        {
            t.Start();
            int n = 100;
            foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => !v.IsTemplate && IsPlan(v.ViewType)).OrderBy(v => v.Name))
            {
                var sh = ViewSheet.Create(doc, tb?.Id ?? ElementId.InvalidElementId);
                sh.SheetNumber = "FF-" + (++n); sh.Name = v.Name;
                if (!Viewport.CanAddViewToSheet(doc, sh.Id, v.Id)) { made.Add(new { v = v.Name, can = false }); continue; }
                var o = sh.Outline;
                var vp = Viewport.Create(doc, sh.Id, v.Id, new XYZ((o.Min.U + o.Max.U) / 2, (o.Min.V + o.Max.V) / 2, 0));
                made.Add(new { v = v.Name, sheet = sh.SheetNumber, vp = vp.Id.Value });
            }
            t.Commit();
        }
        var path = Path.Combine(Path.GetDirectoryName(doc.PathName)!, "STRUCTURAL_CO_sheets.rvt");
        doc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
        return new { tb = tb?.Name, made, saved = path };
    }

    static object Tf(Transform t) => new { o = P(t.Origin), x = P(t.BasisX), y = P(t.BasisY), z = P(t.BasisZ) };

    static object Ifc(Document doc, Job job, string outDir)
    {
        var ver = job.Opt("ifcVersion", "IFC4RV");
        var folder = outDir; var name = "spike-" + ver;
        var opt = new IFCExportOptions
        {
            FileVersion = Enum.Parse<IFCVersion>(ver),
            ExportBaseQuantities = true,
            WallAndColumnSplitting = false,
            SpaceBoundaryLevel = 0,
            FilterViewId = ElementId.InvalidElementId
        };
        opt.AddOption("SitePlacement", job.Opt("sitePlacement", "Internal"));
        opt.AddOption("StoreIFCGUID", "true");
        opt.AddOption("ExportInternalRevitPropertySets", "false");
        foreach (var kv in job.Options ?? new()) if (kv.Key.StartsWith("ifc:")) opt.AddOption(kv.Key.Substring(4), kv.Value);
        bool ok;
        var guids = new List<object>();
        using (var t = new Transaction(doc, "FF IFC export"))
        {
            t.Start();
            ok = doc.Export(folder, name, opt);
            // sample elements + GUIDs while the stored parameter exists
            foreach (var bic in new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFoundation })
            {
                foreach (var e in new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().Take(3))
                {
                    var bb = e.get_BoundingBox(null);
                    XYZ? loc = (e.Location as LocationPoint)?.Point;
                    string? ifcParam = e.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString();
                    string? exportId = null;
                    try { exportId = Autodesk.Revit.DB.IFC.ExporterIFCUtils.CreateAlternateGUID(e); } catch { }
                    guids.Add(new
                    {
                        cat = bic.ToString(), id = e.Id.Value, uid = e.UniqueId, ifcGuidParam = ifcParam, alt = exportId,
                        loc = loc == null ? null : P(loc), bbMin = bb == null ? null : P(bb.Min), bbMax = bb == null ? null : P(bb.Max),
                        level = doc.GetElement(e.LevelId)?.Name
                    });
                }
            }
            t.RollBack();
        }
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(l => new { l.Name, l.Elevation }).ToList();
        return new { ok, ver, file = Path.Combine(folder, name + ".ifc"), guids, levels, options = job.Options };
    }
}
