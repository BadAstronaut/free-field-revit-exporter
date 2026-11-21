// Usage: node bundle-summary.mjs <extracted-bundle-dir> -> compact JSON summary used by the launch report
import fs from 'node:fs'; import path from 'node:path'; import { pathToFileURL, fileURLToPath } from 'node:url';
const dir = process.argv[2];
const here = path.dirname(fileURLToPath(import.meta.url));
const man = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json'), 'utf8'));
const pdfjs = await import(pathToFileURL(path.join(here, '..', 'bundle-validator', 'node_modules', 'pdfjs-dist', 'legacy', 'build', 'pdf.mjs')).href);
const out = { stats: man.stats, exportOptions: man.exportOptions, storeys: man.models[0].storeys.map(s => s.name), sheets: [], schedules: [], links: { total: 0, notIncluded: 0, byKind: {}, spot: [] } };
for (const s of man.sheets) {
  const idx = JSON.parse(fs.readFileSync(path.join(dir, s.data), 'utf8'));
  const d = await pdfjs.getDocument({ data: new Uint8Array(fs.readFileSync(path.join(dir, s.file))), verbosity: 0 }).promise;
  const att = await d.getAttachments(); const f = att && att['freefield-sheet.json'];
  const emb = f ? JSON.parse(Buffer.from(f.content).toString('utf8')) : null;
  out.sheets.push({ number: s.number, name: s.name, vps: s.viewports.map(v => ({ type: v.viewType, storey: v.storeyName ?? null, primary: !!v.primary, mapped: !!v.pdfToModel })), elements: idx.elements.length, links: idx.links.length,
    embeddedSchedules: emb?.schedules?.map(x => x.name) ?? [], embeddedOk: !!emb && emb.elements.length === idx.elements.length });
  for (const l of idx.links) { out.links.total++; if (l.targetIncluded === false) out.links.notIncluded++; out.links.byKind[l.kind] = (out.links.byKind[l.kind] ?? 0) + 1; if (!l.targetSheetNumber) out.links.noTarget = (out.links.noTarget ?? 0) + 1; }
}
const all = out.sheets.flatMap(s => []);
// spot links: first link of 5 different sheets
let n = 0; for (const s of man.sheets) { if (n >= 5) break; const idx = JSON.parse(fs.readFileSync(path.join(dir, s.data), 'utf8')); const l = idx.links[0]; if (l) { out.links.spot.push({ from: s.number, kind: l.kind, to: l.targetSheetNumber, included: l.targetIncluded !== false }); n++; } }
for (const e of man.schedules) { const t = JSON.parse(fs.readFileSync(path.join(dir, e.file), 'utf8')); out.schedules.push({ name: e.name, category: e.category, rows: t.rows.length, bound: t.rows.filter(r => r.gid).length, onSheets: out.sheets.filter(s => s.embeddedSchedules.includes(e.name)).map(s => s.number) }); }
console.log(JSON.stringify(out));
