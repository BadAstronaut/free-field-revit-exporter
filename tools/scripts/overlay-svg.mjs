// Usage: node overlay-svg.mjs <extracted-bundle-dir> <sheetNumber> <out.svg>   -> PDF line segments (grey) + element outlines (red) + viewport regions (blue)
import fs from 'node:fs'; import path from 'node:path';
import { vertices } from './pdf-vertices.mjs';
const [dir, num, out] = process.argv.slice(2);
const man = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json'), 'utf8'));
const s = man.sheets.find(x => x.number === num); const idx = JSON.parse(fs.readFileSync(path.join(dir, s.data), 'utf8'));
const v = await vertices(path.join(dir, s.file));
let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${v.width}" height="${v.height}" viewBox="0 0 ${v.width} ${v.height}"><rect width="100%" height="100%" fill="white"/>`;
svg += '<g stroke="#888" stroke-width="0.6" fill="none">' + v.segs.map(q => `<line x1="${q[0].toFixed(1)}" y1="${q[1].toFixed(1)}" x2="${q[2].toFixed(1)}" y2="${q[3].toFixed(1)}"/>`).join('') + '</g>';
svg += '<g stroke="red" stroke-width="0.8" fill="none">' + idx.elements.filter(e => e.outline).map(e => `<polygon points="${e.outline.map(p => p.join(',')).join(' ')}"/>`).join('') + '</g>';
svg += '<g stroke="blue" stroke-width="1.5" fill="none">' + s.viewports.map(vp => `<rect x="${vp.pdfRegion[0]}" y="${vp.pdfRegion[1]}" width="${vp.pdfRegion[2]-vp.pdfRegion[0]}" height="${vp.pdfRegion[3]-vp.pdfRegion[1]}"/>`).join('') + '</g></svg>';
fs.writeFileSync(out, svg);
console.log(v.width, v.height, 'segs', v.segs.length, 'outlines', idx.elements.filter(e => e.outline).length);
