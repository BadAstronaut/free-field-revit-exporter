import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Ajv2020 } from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import { Zip } from './zip.js';
import { readPdf } from './pdf.js';
import { IfcModel } from './ifc.js';
import type {
  BundleManifest, EmbeddedSheetPayload, ElementsFile, SheetElementIndex, DataTable, CheckResult, ViewportReport, ValidationReport, Status,
} from './types.js';

export interface Options {
  schemaPath?: string;
  toleranceM: number;     // per-element distance tolerance
  minFraction: number;    // fraction of elements that must be within tolerance
  maxMedianM: number;     // median distance limit
  skipGeometry?: boolean;
  log?: (msg: string) => void;
}

export const DEFAULTS: Options = { toleranceM: 0.5, minFraction: 0.9, maxMedianM: 0.25 };

const here = path.dirname(fileURLToPath(import.meta.url));
const SCHEMA_ID = 'https://schemas.freefield.app/ffbundle/1/ffbundle.schema.json';

function defaultSchemaPath(): string {
  // dist/validate.js -> tools/bundle-validator/dist -> repo root/schema
  for (const rel of ['../../../schema/ffbundle.schema.json', '../../../../schema/ffbundle.schema.json']) {
    const p = path.resolve(here, rel);
    if (fs.existsSync(p)) return p;
  }
  return path.resolve(here, '../../../schema/ffbundle.schema.json');
}

function median(a: number[]): number | null {
  if (!a.length) return null;
  const s = [...a].sort((x, y) => x - y);
  const m = s.length >> 1;
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
}
function quantile(a: number[], q: number): number | null {
  if (!a.length) return null;
  const s = [...a].sort((x, y) => x - y);
  return s[Math.min(s.length - 1, Math.floor(q * s.length))];
}

export async function validateBundle(bundlePath: string, opts: Partial<Options> = {}): Promise<ValidationReport> {
  const o: Options = { ...DEFAULTS, ...opts };
  const log = o.log ?? (() => {});
  const t0 = Date.now();
  const checks: CheckResult[] = [];
  const vpReports: ViewportReport[] = [];
  const add = (name: string, status: Status, detail?: string) => checks.push({ name, status, detail });
  const report = (): ValidationReport => ({
    bundle: bundlePath,
    ok: !checks.some((c) => c.status === 'FAIL') && !vpReports.some((v) => v.status === 'FAIL'),
    checks, viewports: vpReports,
    thresholds: { toleranceM: o.toleranceM, minFraction: o.minFraction, maxMedianM: o.maxMedianM },
    durationMs: Date.now() - t0,
  });

  let zip: Zip;
  try { zip = await Zip.open(bundlePath); } catch (e) { add('open bundle (zip)', 'FAIL', String(e)); return report(); }
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'ffvalidate-'));
  try {
    // ---- manifest + schema
    if (!zip.has('manifest.json')) { add('manifest.json present', 'FAIL'); return report(); }
    let manifest: BundleManifest;
    try { manifest = await zip.readJson<BundleManifest>('manifest.json'); } catch (e) { add('manifest.json parses', 'FAIL', String(e)); return report(); }

    const ajv = new Ajv2020({ allErrors: true, strict: false });
    (addFormats as unknown as (a: Ajv2020) => void)(ajv);
    const schema = JSON.parse(fs.readFileSync(o.schemaPath ?? defaultSchemaPath(), 'utf8'));
    ajv.addSchema(schema);
    const fmt = (errs: { instancePath: string; message?: string }[] | null | undefined) =>
      (errs ?? []).slice(0, 8).map((e) => `${e.instancePath || '/'} ${e.message}`).join('; ');
    const vManifest = ajv.getSchema(SCHEMA_ID)!;
    if (vManifest(manifest)) add('manifest matches schema', 'PASS'); else add('manifest matches schema', 'FAIL', fmt(vManifest.errors));
    const vDef = (name: string) => ajv.getSchema(`${SCHEMA_ID}#/$defs/${name}`)!;

    // ---- referenced files exist
    const missing: string[] = [];
    const need = (f: string | undefined) => { if (f && !zip.has(f)) missing.push(f); };
    manifest.models?.forEach((m) => need(m.file));
    manifest.sheets?.forEach((s) => { need(s.file); need(s.data); });
    manifest.schedules?.forEach((s) => need(s.file));
    need(manifest.elementsFile);
    add('referenced files exist', missing.length ? 'FAIL' : 'PASS', missing.length ? `missing: ${missing.join(', ')}` : `${(manifest.models?.length ?? 0)} models, ${(manifest.sheets?.length ?? 0)} sheets`);

    // ---- data files vs schema
    let elements: ElementsFile = {};
    if (zip.has(manifest.elementsFile)) {
      try {
        elements = await zip.readJson<ElementsFile>(manifest.elementsFile);
        const v = vDef('ElementsFile');
        // validate a bounded sample for speed on huge files, plus all keys' gid consistency
        const keys = Object.keys(elements);
        const sample: ElementsFile = {};
        const step = Math.max(1, Math.floor(keys.length / 2000));
        for (let i = 0; i < keys.length; i += step) sample[keys[i]] = elements[keys[i]];
        const ok = v(sample);
        const badKey = keys.find((k) => elements[k].gid !== k);
        add('elements.json matches schema', ok && !badKey ? 'PASS' : 'FAIL', ok ? (badKey ? `key/gid mismatch at ${badKey}` : `${keys.length} elements`) : fmt(v.errors));
      } catch (e) { add('elements.json parses', 'FAIL', String(e)); }
    }
    const sheetIndex = new Map<string, SheetElementIndex>();
    for (const s of manifest.sheets ?? []) {
      if (!zip.has(s.data)) continue;
      try {
        const idx = await zip.readJson<SheetElementIndex>(s.data);
        sheetIndex.set(s.id, idx);
        const v = vDef('SheetElementIndex');
        if (!v(idx)) add(`sheet index ${s.number}`, 'FAIL', fmt(v.errors));
        else if (idx.sheetId !== s.id) add(`sheet index ${s.number}`, 'FAIL', 'sheetId mismatch');
      } catch (e) { add(`sheet index ${s.number}`, 'FAIL', String(e)); }
    }
    add('sheet index files valid', checks.some((c) => c.name.startsWith('sheet index ') && c.status === 'FAIL') ? 'FAIL' : 'PASS');
    let schedBad = 0;
    for (const s of manifest.schedules ?? []) {
      if (!zip.has(s.file)) continue;
      try { const t = await zip.readJson<DataTable>(s.file); const v = vDef('DataTable'); if (!v(t)) { schedBad++; add(`schedule ${s.name}`, 'FAIL', fmt(v.errors)); } }
      catch (e) { schedBad++; add(`schedule ${s.name}`, 'FAIL', String(e)); }
    }
    if ((manifest.schedules?.length ?? 0) > 0) add('schedule files valid', schedBad ? 'FAIL' : 'PASS');

    // ---- stats consistency
    const st = manifest.stats;
    if (st) {
      const vp = (manifest.sheets ?? []).reduce((n, s) => n + s.viewports.length, 0);
      const links = [...sheetIndex.values()].reduce((n, i) => n + i.links.length, 0);
      const bad: string[] = [];
      if (st.sheets !== manifest.sheets.length) bad.push('sheets');
      if (st.viewports !== vp) bad.push('viewports');
      if (st.links !== links) bad.push('links');
      if (st.elements !== Object.keys(elements).length) bad.push('elements');
      add('stats consistent', bad.length ? 'WARN' : 'PASS', bad.length ? `mismatch: ${bad.join(', ')}` : undefined);
    }

    // ---- PDFs
    for (const s of manifest.sheets ?? []) {
      if (!zip.has(s.file)) continue;
      const name = `pdf ${s.number}`;
      try {
        const info = await readPdf(await zip.read(s.file));
        const problems: string[] = [];
        if (info.pageCount !== 1) problems.push(`pageCount=${info.pageCount}`);
        if (Math.abs(info.widthPt - s.page.widthPt) > 0.5 || Math.abs(info.heightPt - s.page.heightPt) > 0.5)
          problems.push(`size ${info.widthPt.toFixed(2)}x${info.heightPt.toFixed(2)} != manifest ${s.page.widthPt}x${s.page.heightPt}`);
        add(name, problems.length ? 'FAIL' : 'PASS', problems.join('; ') || `1 page ${info.widthPt.toFixed(1)}x${info.heightPt.toFixed(1)} pt`);
        const ename = `${name} embedded json (pdf.js)`;
        if (!info.attachments.includes('freefield-sheet.json')) add(ename, 'FAIL', 'freefield-sheet.json not readable via pdf.js getAttachments()');
        else {
          const emb = info.embeddedSheetJson as EmbeddedSheetPayload;
          const vE = vDef('EmbeddedSheetPayload');
          const errs: string[] = [];
          if (!vE(emb)) errs.push(fmt(vE.errors));
          else {
            if (emb.sheetId !== s.id) errs.push('sheetId mismatch');
            if (emb.sheetNumber !== s.number) errs.push('sheetNumber mismatch');
            const noRec = emb.elements.filter((x) => !emb.elementRecords[x.gid] && elements[x.gid]);
            if (noRec.length) errs.push(`${noRec.length} indexed elements lack elementRecords`);
            const badLinks = emb.links.filter((l) => l.targetSheetId && !l.targetSheetNumber || l.targetSheetNumber && !l.targetSheetId);
            if (badLinks.length) errs.push(`${badLinks.length} links lack targetSheetId or targetSheetNumber`);
            const idx = sheetIndex.get(s.id);
            if (idx && idx.elements.length !== emb.elements.length) errs.push('elements differ from data/sheets index');
            const ids = new Set(emb.viewports.map((x) => x.id));
            if (s.viewports.some((x) => !ids.has(x.id))) errs.push('viewports missing');
          }
          add(ename, errs.length ? 'FAIL' : 'PASS', errs.join('; ') || `${emb.elements.length} elements, ${Object.keys(emb.elementRecords).length} records, ${emb.links.length} links`);
        }
      } catch (e) { add(name, 'FAIL', `unreadable PDF: ${e}`); }
    }

    // ---- viewport structure: control points vs pdfToModel, primary uniqueness
    for (const s of manifest.sheets ?? []) {
      const seen = new Map<string, number>();
      for (const v of s.viewports) {
        if (!v.pdfToModel) continue;
        const [a, b, c, d, e, f] = v.pdfToModel;
        let worst = 0;
        for (const cp of v.controlPoints ?? []) {
          const mx = a * cp.pdfX + b * cp.pdfY + c, my = d * cp.pdfX + e * cp.pdfY + f;
          worst = Math.max(worst, Math.hypot(mx - cp.modelX, my - cp.modelY));
        }
        const cps = v.controlPoints?.length ?? 0;
        add(`controlPoints ${s.number}/${v.viewName}`, cps >= 2 && worst < 1e-6 ? 'PASS' : 'FAIL', `${cps} points, max deviation ${worst.toExponential(2)} m`);
        if (v.primary) seen.set(`${v.modelId}|${v.storeyName}`, (seen.get(`${v.modelId}|${v.storeyName}`) ?? 0) + 1);
      }
      for (const [k, n] of seen) if (n > 1) add(`primary viewport ${s.number}`, 'WARN', `${n} primary viewports for ${k}`);
    }

    // ---- every exported sheet must carry a mapped viewport unless unmapped sheets were explicitly included
    if (manifest.exportOptions && manifest.exportOptions.includeUnmappedSheets === false) {
      for (const s of manifest.sheets ?? []) {
        const mapped = s.viewports.some((v) => v.pdfToModel && v.storeyName);
        add(`sheet ${s.number} has a mapped viewport`, mapped ? 'PASS' : 'FAIL', mapped ? 'ok' : 'no mapped floor-plan viewport but includeUnmappedSheets=false');
      }
    }

    // ---- models: storeys
    const planVps = (manifest.sheets ?? []).flatMap((s) => s.viewports.filter((v) => v.pdfToModel).map((v) => ({ s, v })));
    const modelIdsUsed = [...new Set(planVps.map((x) => x.v.modelId!).filter(Boolean))];
    const ifcs = new Map<string, IfcModel>();
    const ifcCentres = new Map<string, Map<string, [number, number]>>(); // modelId -> gid -> xy
    try {
      for (const m of manifest.models ?? []) {
        if (!modelIdsUsed.includes(m.id) && !(m.storeys?.length)) continue;
        if (!zip.has(m.file)) continue;
        const dest = path.join(tmp, `${m.id.replace(/[^A-Za-z0-9._-]/g, '_')}.ifc`);
        log(`extracting ${m.file} (${((zip.size(m.file) ?? 0) / 1e6).toFixed(1)} MB)`);
        await zip.extract(m.file, dest);
        try {
          log(`opening ${m.id} in web-ifc`);
          const ifc = await IfcModel.open(dest);
          ifcs.set(m.id, ifc);
          const names = ifc.storeys();
          const nameSet = new Set(names.map((x) => x.name));
          const absent = (m.storeys ?? []).filter((x) => !nameSet.has(x.name)).map((x) => x.name);
          add(`model ${m.id} storeys`, absent.length ? 'FAIL' : 'PASS', absent.length ? `manifest storeys not in IFC: ${absent.join(', ')} (IFC has: ${[...nameSet].join(', ')})` : `${names.length} IfcBuildingStorey, schema ${ifc.schema}`);
          for (const x of m.storeys ?? []) {
            const hit = names.find((n) => n.name === x.name);
            if (hit?.elevation != null && Math.abs(hit.elevation - x.elevation) > 0.01)
              add(`model ${m.id} storey ${x.name} elevation`, 'WARN', `manifest ${x.elevation} vs IFC ${hit.elevation.toFixed(3)}`);
          }
        } catch (e) { add(`model ${m.id} opens`, 'FAIL', String(e)); }
      }
      const vpStoreyFail = new Set<string>();
      for (const { s, v } of planVps) {
        const ifc = ifcs.get(v.modelId ?? '');
        if (!ifc) { add(`viewport ${s.number}/${v.viewName} model`, 'FAIL', `model '${v.modelId}' not available`); vpStoreyFail.add(v.id); continue; }
        const ok = ifc.storeys().some((x) => x.name === v.storeyName);
        add(`viewport ${s.number}/${v.viewName} storey`, ok ? 'PASS' : 'FAIL', ok ? v.storeyName! : `storey '${v.storeyName}' not in IFC`);
      }

      // ---- geometric check
      if (!o.skipGeometry) {
        for (const { s, v } of planVps) {
          const ifc = ifcs.get(v.modelId ?? '');
          const idx = sheetIndex.get(s.id);
          const rep: ViewportReport = {
            sheetNumber: s.number, viewportId: v.id, viewName: v.viewName, modelId: v.modelId ?? '', storeyName: v.storeyName ?? '',
            indexed: 0, inIfc: 0, withGeometry: 0, within: 0, fractionWithin: 0, medianErrM: null, p90ErrM: null, maxErrM: null,
            detRatio: null, status: 'SKIP', detail: '',
          };
          vpReports.push(rep);
          const [a, b, c, d, e, f] = v.pdfToModel!;
          const det = a * e - b * d;
          const expected = Math.pow((v.scale * 0.0254) / 72, 2);
          rep.detRatio = Math.abs(det) / expected;
          if (!ifc || !idx) { rep.status = 'FAIL'; rep.detail = 'model or sheet index unavailable'; continue; }
          const els = idx.elements.filter((x) => x.vp === v.id);
          rep.indexed = els.length;
          const gmap = ifc.globalIds();
          let cache = ifcCentres.get(v.modelId!);
          if (!cache) { cache = new Map(); ifcCentres.set(v.modelId!, cache); }
          const want = [...new Set(els.map((x) => x.gid))].filter((g) => gmap.has(g) && !cache!.has(g));
          if (want.length) {
            log(`geometry for ${want.length} elements (${s.number}/${v.viewName})`);
            const byExpress = ifc.xyCentres(want.map((g) => gmap.get(g)!));
            for (const g of want) { const c2 = byExpress.get(gmap.get(g)!); if (c2) cache.set(g, c2); }
          }
          const dists: number[] = [];
          for (const el of els) {
            if (!gmap.has(el.gid)) continue;
            rep.inIfc++;
            const ctr = cache.get(el.gid);
            if (!ctr) continue;
            rep.withGeometry++;
            const px = (el.bbox[0] + el.bbox[2]) / 2, py = (el.bbox[1] + el.bbox[3]) / 2;
            const mx = a * px + b * py + c, my = d * px + e * py + f;
            dists.push(Math.hypot(mx - ctr[0], my - ctr[1]));
          }
          rep.within = dists.filter((x) => x <= o.toleranceM).length;
          rep.fractionWithin = dists.length ? rep.within / dists.length : 0;
          rep.medianErrM = median(dists);
          rep.p90ErrM = quantile(dists, 0.9);
          rep.maxErrM = dists.length ? Math.max(...dists) : null;
          if (rep.indexed === 0) { rep.status = 'WARN'; rep.detail = 'no indexed elements for this viewport'; }
          else if (rep.withGeometry === 0) { rep.status = 'FAIL'; rep.detail = 'no indexed element could be matched to IFC geometry'; }
          else {
            const pass = rep.fractionWithin >= o.minFraction && (rep.medianErrM ?? Infinity) < o.maxMedianM;
            rep.status = pass ? 'PASS' : 'FAIL';
            rep.detail = pass ? '' : `need >=${(o.minFraction * 100).toFixed(0)}% within ${o.toleranceM} m and median < ${o.maxMedianM} m`;
          }
          if (rep.detRatio != null && Math.abs(rep.detRatio - 1) > 0.02) {
            rep.detail += `${rep.detail ? '; ' : ''}|det| ratio ${rep.detRatio.toFixed(3)} (expected ~1 for scale 1:${v.scale})`;
            if (rep.status === 'PASS') rep.status = 'WARN';
          }
        }
      }
    } finally {
      for (const m of ifcs.values()) m.close();
    }
  } finally {
    zip.close();
    try { fs.rmSync(tmp, { recursive: true, force: true }); } catch { /* ignore */ }
  }
  return report();
}
