// Usage: node calib-view.mjs <spike-viewpdf.json> <vertices.json>
// Finds page = (864*px + tx, -864*py + ty) (px,py = model->projection coords in paper feet) by voting over grid endpoints.
import fs from 'node:fs';
const s = JSON.parse(fs.readFileSync(process.argv[2], 'utf8')); const v = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
const FT = 864, votes = new Map();
for (const p of s.pts) for (const q of v.pts) { const k = Math.round((q[0] - FT * p.proj[0]) * 2) + ',' + Math.round((q[1] + FT * p.proj[1]) * 2); votes.set(k, (votes.get(k) || 0) + 1); }
const cands = [...votes].sort((a, b) => b[1] - a[1]).slice(0, 400).map(x => x[0].split(',').map(n => n / 2));
const res = cands.map(([tx, ty]) => { let c = 0; for (const p of s.pts) { const x = FT * p.proj[0] + tx, y = -FT * p.proj[1] + ty; if (v.pts.some(q => Math.abs(q[0] - x) < .4 && Math.abs(q[1] - y) < .4)) c++; } return [c, tx, ty]; }).sort((a, b) => b[0] - a[0]);
console.log('top', JSON.stringify(res.slice(0, 4)));
const [c, tx, ty] = res[0];
console.log('page', v.width, v.height, 'matched', c, '/', s.pts.length, 'page centre -> proj ft', ((v.width / 2 - tx) / FT).toFixed(5), ((ty - v.height / 2) / FT).toFixed(5));
const xs = s.pts.map(p => p.proj[0]), ys = s.pts.map(p => p.proj[1]);
console.log('grid proj bbox', Math.min(...xs).toFixed(4), Math.min(...ys).toFixed(4), Math.max(...xs).toFixed(4), Math.max(...ys).toFixed(4));
