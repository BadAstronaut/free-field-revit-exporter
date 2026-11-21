// Usage: node verify-bundle-pdf.mjs <extracted-bundle-dir>
// Independent PDF-alignment check: for every indexed element bbox on a sheet, how many of its 4 page-space corners
// coincide (<=1.5pt) with a vertex of the vector content of that PDF? Prints per-sheet hit rate (plan/section columns, beams = rects).
// Outlines: 'vertex hits' = outline vertices on a PDF vertex; 'edge cover' = samples along outline EDGES (corners + 3 interior points per edge)
// within 1.5pt of PDF line segments. Elements the view does not draw (below the cut plane, hidden, centreline-only) legitimately score low;
// vertex hits alone under-count because Revit merges collinear edges into one long line (no vertex at the interior corners).
import fs from 'node:fs'; import path from 'node:path';
import { vertices } from './pdf-vertices.mjs';
const dir = process.argv[2];
const only = process.argv[3];   // optional sheet number: print per-category breakdown
const els = JSON.parse(fs.readFileSync(path.join(dir, 'data/elements.json'), 'utf8'));
const man = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json'), 'utf8'));
for (const s of man.sheets) {
  if (only && s.number !== only) continue;
  const idx = JSON.parse(fs.readFileSync(path.join(dir, s.data), 'utf8'));
  const v = await vertices(path.join(dir, s.file));
  // spatial hash
  const cell = 4, grid = new Map();
  for (const p of v.pts) { const k = Math.floor(p[0] / cell) + ',' + Math.floor(p[1] / cell); (grid.get(k) ?? grid.set(k, []).get(k)).push(p); }
  const dseg = (x, y, q) => { const dx = q[2]-q[0], dy = q[3]-q[1], l = dx*dx+dy*dy; let t = l ? ((x-q[0])*dx+(y-q[1])*dy)/l : 0; t = Math.max(0, Math.min(1, t)); return Math.hypot(x-q[0]-t*dx, y-q[1]-t*dy); };
  const sg = new Map(); for (const q of v.segs) { const len = Math.hypot(q[2]-q[0], q[3]-q[1]); const n = Math.max(1, Math.ceil(len / (cell / 2))); const seen = new Set(); for (let k = 0; k <= n; k++) { const x = q[0] + (q[2]-q[0]) * k / n, y = q[1] + (q[3]-q[1]) * k / n; for (let i = -1; i <= 1; i++) for (let j = -1; j <= 1; j++) { const key = (Math.floor(x/cell)+i) + ',' + (Math.floor(y/cell)+j); if (seen.has(key)) continue; seen.add(key); (sg.get(key) ?? sg.set(key, []).get(key)).push(q); } } }
  const onLine = (x, y) => { for (const q of sg.get(Math.floor(x/cell)+','+Math.floor(y/cell)) ?? []) if (dseg(x, y, q) <= 1.5) return true; return false; };
  let eHit = 0, eTot = 0;
  const near = (x, y, r) => { for (let i = -1; i <= 1; i++) for (let j = -1; j <= 1; j++) for (const p of grid.get((Math.floor(x / cell) + i) + ',' + (Math.floor(y / cell) + j)) ?? []) if (Math.hypot(p[0]-x, p[1]-y) <= r) return true; return false; };
  let hit = 0, tot = 0, elemsHit = 0, inPage = 0, oHit = 0, oTot = 0, oElems = 0, oMax = 0;
  const cat = new Map();
  const regions = new Map(s.viewports.map(vv => [vv.id, vv.pdfRegion]));   // samples outside the viewport crop are not drawn: ignore them
  for (const e of idx.elements) {
    if (e.outline) { let eh = 0, et = 0; oElems++; oMax = Math.max(oMax, e.outline.length); for (let i = 0; i < e.outline.length; i++) { const a = e.outline[i], b = e.outline[(i+1) % e.outline.length]; for (const t of [0, .25, .5, .75]) { const x = a[0]+(b[0]-a[0])*t, y = a[1]+(b[1]-a[1])*t; { const rg = regions.get(e.vp); if (rg && (x < rg[0] || x > rg[2] || y < rg[1] || y > rg[3])) continue; } if (x < 0 || y < 0 || x > v.width || y > v.height) continue; eTot++; et++; if (onLine(x, y)) { eHit++; eh++; } } }
      { const c = els[e.gid]?.category ?? '?'; const r = cat.get(c) ?? cat.set(c, [0, 0, 0]).get(c); r[0] += eh; r[1] += et; r[2]++; }
    for (const [x, y] of e.outline) { if (x < 0 || y < 0 || x > v.width || y > v.height) continue; oTot++; if (near(x, y, 1.5)) oHit++; } }
    const [x0, y0, x1, y1] = e.bbox;
    if (x1 < 0 || y1 < 0 || x0 > v.width || y0 > v.height) continue; inPage++;
    let h = 0; for (const [x, y] of [[x0, y0], [x1, y0], [x1, y1], [x0, y1]]) { tot++; if (near(x, y, 1.5)) { hit++; h++; } }
    if (h >= 2) elemsHit++;
  }
  console.log(`${s.number.padEnd(28)} page ${s.page.widthPt.toFixed(0)}x${s.page.heightPt.toFixed(0)} elems(in page) ${inPage} corner hits ${hit}/${tot} (${(100*hit/Math.max(1,tot)).toFixed(0)}%) elems with >=2 corners ${elemsHit} | outlines ${oElems} (max pts ${oMax}) vertex hits ${oHit}/${oTot} edge cover ${eHit}/${eTot} (${(100*eHit/Math.max(1,eTot)).toFixed(0)}%)`);
  if (only) for (const [c, r] of [...cat].sort((x, y) => y[1][1] - x[1][1])) console.log(`   ${c.padEnd(30)} elems ${String(r[2]).padStart(5)} edge cover ${r[0]}/${r[1]} (${(100*r[0]/Math.max(1,r[1])).toFixed(0)}%)`);

}
