// Usage: node shift-search.mjs <extracted-bundle-dir> <sheet> -> best (dx,dy) translation of element outlines onto PDF line segments
import fs from 'node:fs'; import path from 'node:path';
import { vertices } from './pdf-vertices.mjs';
const [dir, num] = process.argv.slice(2);
const man = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json'), 'utf8'));
const s = man.sheets.find(x => x.number === num); const idx = JSON.parse(fs.readFileSync(path.join(dir, s.data), 'utf8'));
const v = await vertices(path.join(dir, s.file));
const cell = 4, sg = new Map();
for (const q of v.segs) { const len = Math.hypot(q[2]-q[0], q[3]-q[1]); const n = Math.max(1, Math.ceil(len/(cell/2))); const seen = new Set(); for (let k = 0; k <= n; k++) { const x = q[0]+(q[2]-q[0])*k/n, y = q[1]+(q[3]-q[1])*k/n; for (let i = -1; i <= 1; i++) for (let j = -1; j <= 1; j++) { const key = (Math.floor(x/cell)+i)+','+(Math.floor(y/cell)+j); if (seen.has(key)) continue; seen.add(key); (sg.get(key) ?? sg.set(key, []).get(key)).push(q); } } }
const dseg = (x, y, q) => { const dx = q[2]-q[0], dy = q[3]-q[1], l = dx*dx+dy*dy; let t = l ? ((x-q[0])*dx+(y-q[1])*dy)/l : 0; t = Math.max(0, Math.min(1, t)); return Math.hypot(x-q[0]-t*dx, y-q[1]-t*dy); };
const on = (x, y) => { for (const q of sg.get(Math.floor(x/cell)+','+Math.floor(y/cell)) ?? []) if (dseg(x, y, q) <= 1.2) return true; return false; };
const pts = []; for (const e of idx.elements) if (e.outline) for (let i = 0; i < e.outline.length; i++) { const a = e.outline[i], b = e.outline[(i+1)%e.outline.length]; for (const t of [0, .5]) pts.push([a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t]); }
const sample = pts.filter((_, i) => i % Math.max(1, Math.floor(pts.length / 250)) === 0);
let best = [-1, 0, 0]; const base = sample.filter(p => on(p[0], p[1])).length;
for (let dx = -160; dx <= 160; dx += 1) for (let dy = -160; dy <= 160; dy += 1) { let c = 0; for (const p of sample) if (on(p[0]+dx, p[1]+dy)) c++; if (c > best[0]) best = [c, dx, dy]; }
console.log(num, 'samples', sample.length, 'cover at 0,0:', base, 'best shift', best[1], best[2], 'cover', best[0]);
