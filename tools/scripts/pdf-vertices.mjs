// Usage: node pdf-vertices.mjs <pdf> [--json out.json]
// Dumps page size (pt) and all path vertices in page space (origin top-left, y down) using pdf.js.
// pdf.js is resolved from tools/bundle-validator/node_modules (npm i there first).
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const pdfjs = await import(pathToFileURL(path.join(here, '..', 'bundle-validator', 'node_modules', 'pdfjs-dist', 'legacy', 'build', 'pdf.mjs')).href);
const mul = (m, n) => [m[0]*n[0]+m[2]*n[1], m[1]*n[0]+m[3]*n[1], m[0]*n[2]+m[2]*n[3], m[1]*n[2]+m[3]*n[3], m[0]*n[4]+m[2]*n[5]+m[4], m[1]*n[4]+m[3]*n[5]+m[5]];
export async function vertices(file) {
  const doc = await pdfjs.getDocument({ data: new Uint8Array(fs.readFileSync(file)), verbosity: 0 }).promise;
  const page = await doc.getPage(1);
  const vp = page.getViewport({ scale: 1 });
  const ops = await page.getOperatorList();
  const O = pdfjs.OPS;
  let ctm = vp.transform.slice(); const stack = []; const pts = []; const segs = [];
  for (let i = 0; i < ops.fnArray.length; i++) {
    const fn = ops.fnArray[i], a = ops.argsArray[i];
    if (fn === O.save) stack.push(ctm.slice());
    else if (fn === O.restore) ctm = stack.pop() ?? ctm;
    else if (fn === O.transform) ctm = mul(ctm, a);
    else if (fn === O.constructPath) {
      const [subops, c] = a; let k = 0;
      // segs = straight line segments [x0,y0,x1,y1] (curves contribute only their end vertex): lets callers test "lies on linework",
      // since Revit merges collinear edges into one long line whose interior vertices do not exist in the PDF.
      let cur = null, start = null;
      const push = (x, y, line) => { const q = [ctm[0]*x+ctm[2]*y+ctm[4], ctm[1]*x+ctm[3]*y+ctm[5]]; pts.push(q); if (line && cur) segs.push([cur[0], cur[1], q[0], q[1]]); cur = q; return q; };
      for (const op of subops) {
        if (op === O.moveTo) { start = push(c[k], c[k+1], false); k += 2; }
        else if (op === O.lineTo) { push(c[k], c[k+1], true); k += 2; }
        else if (op === O.closePath) { if (cur && start) { segs.push([cur[0], cur[1], start[0], start[1]]); cur = start; } }
        else if (op === O.curveTo) { push(c[k+4], c[k+5]); k += 6; }
        else if (op === O.curveTo2 || op === O.curveTo3) { push(c[k+2], c[k+3]); k += 4; }
        else if (op === O.rectangle) { const x=c[k],y=c[k+1],w=c[k+2],h=c[k+3]; start = push(x,y,false); push(x+w,y,true); push(x+w,y+h,true); push(x,y+h,true); segs.push([cur[0], cur[1], start[0], start[1]]); cur = start; k += 4; }
      }
    }
  }
  return { width: vp.width, height: vp.height, view: page.view, rotate: page.rotate, pts, segs };
}
if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  const r = await vertices(process.argv[2]);
  const xs = r.pts.map(p => p[0]), ys = r.pts.map(p => p[1]);
  console.log(JSON.stringify({ width: r.width, height: r.height, view: r.view, rotate: r.rotate, n: r.pts.length,
    minX: Math.min(...xs), maxX: Math.max(...xs), minY: Math.min(...ys), maxY: Math.max(...ys) }));
  const i = process.argv.indexOf('--json'); if (i > 0) fs.writeFileSync(process.argv[i+1], JSON.stringify(r));
}
