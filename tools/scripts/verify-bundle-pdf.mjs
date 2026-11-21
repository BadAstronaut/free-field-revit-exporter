// Usage: node verify-bundle-pdf.mjs <extracted-bundle-dir>
// Independent PDF-alignment check: for every indexed element bbox on a sheet, how many of its 4 page-space corners
// coincide (<=1.5pt) with a vertex of the vector content of that PDF? Prints per-sheet hit rate (plan/section columns, beams = rects).
// Outlines: 'vertex hits' = outline vertices on a PDF vertex; 'edge cover' = samples along outline EDGES (corners + 3 interior points per edge)
// within 1.5pt of PDF line segments. Elements the view does not draw (below the cut plane, hidden, centreline-only) legitimately score low;
// vertex hits alone under-count because Revit merges collinear edges into one long line (no vertex at the interior corners).
import fs from 'node:fs'; import path from 'node:path';
import { vertices } from './pdf-vertices.mjs';
const dir = process.argv[2];
const man = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json'), 'utf8'));
for (const s of man.sheets) {
  const idx = JSON.parse(fs.readFileSync(path.join(dir, s.data), 'utf8'));
  const v = await vertices(path.join(dir, s.file));
  // spatial hash
  const cell = 4, grid = new Map();
  for (const p of v.pts) { const k = Math.floor(p[0] / cell) + ',' + Math.floor(p[1] / cell); (grid.get(k) ?? grid.set(k, []).get(k)).push(p); }
  const dseg = (x, y, q) => { const dx = q[2]-q[0], dy = q[3]-q[1], l = dx*dx+dy*dy; let t = l ? ((x-q[0])*dx+(y-q[1])*dy)/l : 0; t = Math.max(0, Math.min(1, t)); return Math.hypot(x-q[0]-t*dx, y-q[1]-t*dy); };
  const sg = new Map(); for (const q of v.segs) { const x0 = Math.floor((Math.min(q[0],q[2])-2)/cell), x1 = Math.floor((Math.max(q[0],q[2])+2)/cell), y0 = Math.floor((Math.min(q[1],q[3])-2)/cell), y1 = Math.floor((Math.max(q[1],q[3])+2)/cell); if ((x1-x0+1)*(y1-y0+1) > 4000) continue; for (let i = x0; i <= x1; i++) for (let j = y0; j <= y1; j++) (sg.get(i+','+j) ?? sg.set(i+','+j, []).get(i+','+j)).push(q); }
  const onLine = (x, y) => { for (const q of sg.get(Math.floor(x/cell)+','+Math.floor(y/cell)) ?? []) if (dseg(x, y, q) <= 1.5) return true; return false; };
  let eHit = 0, eTot = 0;
  const near = (x, y, r) => { for (let i = -1; i <= 1; i++) for (let j = -1; j <= 1; j++) for (const p of grid.get((Math.floor(x / cell) + i) + ',' + (Math.floor(y / cell) + j)) ?? []) if (Math.hypot(p[0]-x, p[1]-y) <= r) return true; return false; };
  let hit = 0, tot = 0, elemsHit = 0, inPage = 0, oHit = 0, oTot = 0, oElems = 0, oMax = 0;
  for (const e of idx.elements) {
    if (e.outline) { oElems++; oMax = Math.max(oMax, e.outline.length); for (let i = 0; i < e.outline.length; i++) { const a = e.outline[i], b = e.outline[(i+1) % e.outline.length]; for (const t of [0, .25, .5, .75]) { const x = a[0]+(b[0]-a[0])*t, y = a[1]+(b[1]-a[1])*t; if (x < 0 || y < 0 || x > v.width || y > v.height) continue; eTot++; if (onLine(x, y)) eHit++; } }
    for (const [x, y] of e.outline) { if (x < 0 || y < 0 || x > v.width || y > v.height) continue; oTot++; if (near(x, y, 1.5)) oHit++; } }
    const [x0, y0, x1, y1] = e.bbox;
    if (x1 < 0 || y1 < 0 || x0 > v.width || y0 > v.height) continue; inPage++;
    let h = 0; for (const [x, y] of [[x0, y0], [x1, y0], [x1, y1], [x0, y1]]) { tot++; if (near(x, y, 1.5)) { hit++; h++; } }
    if (h >= 2) elemsHit++;
  }
  console.log(`${s.number.padEnd(28)} page ${s.page.widthPt.toFixed(0)}x${s.page.heightPt.toFixed(0)} elems(in page) ${inPage} corner hits ${hit}/${tot} (${(100*hit/Math.max(1,tot)).toFixed(0)}%) elems with >=2 corners ${elemsHit} | outlines ${oElems} (max pts ${oMax}) vertex hits ${oHit}/${oTot} edge cover ${eHit}/${eTot} (${(100*eHit/Math.max(1,eTot)).toFixed(0)}%)`);
}
