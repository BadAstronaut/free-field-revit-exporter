// Usage: node check-page-map.mjs <spike-pdf.json> <pdf>   -> compares expected page points of grid endpoints with PDF vertices.
import fs from 'node:fs';
import { vertices } from './pdf-vertices.mjs';
const spike = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const v = await vertices(process.argv[3]);
const [minU, minV, maxU, maxV] = spike.outline, FT = 864;
const W = v.width, H = v.height;
const hyps = {
  'outline-min -> page bottom-left': (x, y) => [(x - minU) * FT, H - (y - minV) * FT],
  'outline-center -> page center': (x, y) => [W / 2 + (x - (minU + maxU) / 2) * FT, H / 2 - (y - (minV + maxV) / 2) * FT],
  'sheet origin -> page bottom-left': (x, y) => [x * FT, H - y * FT],
};
const near = (p) => { let d = 1e9; for (const q of v.pts) { const e = Math.hypot(q[0]-p[0], q[1]-p[1]); if (e < d) d = e; } return d; };
console.log('page', W.toFixed(2), H.toFixed(2), 'outline pt', ((maxU-minU)*FT).toFixed(2), ((maxV-minV)*FT).toFixed(2));
for (const [n, f] of Hyps()) {}
function* Hyps() {}
for (const [name, f] of Object.entries(hyps)) {
  const errs = [];
  for (const vp of spike.viewports) for (const g of vp.gridPoints) errs.push(near(f(g.sheet[0], g.sheet[1])));
  errs.sort((a, b) => a - b);
  console.log(name.padEnd(36), 'n', errs.length, 'median', errs[errs.length >> 1]?.toFixed(3), 'p90', errs[Math.floor(errs.length * .9)]?.toFixed(3), 'max', errs.at(-1)?.toFixed(3));
}
