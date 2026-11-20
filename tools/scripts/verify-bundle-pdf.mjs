// Usage: node verify-bundle-pdf.mjs <extracted-bundle-dir>
// Independent PDF-alignment check: for every indexed element bbox on a sheet, how many of its 4 page-space corners
// coincide (<=1.5pt) with a vertex of the vector content of that PDF? Prints per-sheet hit rate (plan/section columns, beams = rects).
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
  const near = (x, y, r) => { for (let i = -1; i <= 1; i++) for (let j = -1; j <= 1; j++) for (const p of grid.get((Math.floor(x / cell) + i) + ',' + (Math.floor(y / cell) + j)) ?? []) if (Math.hypot(p[0]-x, p[1]-y) <= r) return true; return false; };
  let hit = 0, tot = 0, elemsHit = 0, inPage = 0;
  for (const e of idx.elements) {
    const [x0, y0, x1, y1] = e.bbox;
    if (x1 < 0 || y1 < 0 || x0 > v.width || y0 > v.height) continue; inPage++;
    let h = 0; for (const [x, y] of [[x0, y0], [x1, y0], [x1, y1], [x0, y1]]) { tot++; if (near(x, y, 1.5)) { hit++; h++; } }
    if (h >= 2) elemsHit++;
  }
  console.log(`${s.number.padEnd(28)} page ${s.page.widthPt.toFixed(0)}x${s.page.heightPt.toFixed(0)} elems(in page) ${inPage} corner hits ${hit}/${tot} (${(100*hit/Math.max(1,tot)).toFixed(0)}%) elems with >=2 corners ${elemsHit}`);
}
