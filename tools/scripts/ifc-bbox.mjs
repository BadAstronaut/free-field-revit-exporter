// Usage: node ifc-bbox.mjs <file.ifc> <GlobalId>...  -> world bbox (metres, IFC Z-up) of element geometry via web-ifc (COORDINATE_TO_ORIGIN:false)
import fs from 'node:fs'; import path from 'node:path'; import { createRequire } from 'node:module'; import { fileURLToPath } from 'node:url';
const here = path.dirname(fileURLToPath(import.meta.url));
const req = createRequire(path.join(here, '..', 'bundle-validator', 'package.json'));
const WebIFC = req('web-ifc');
const api = new WebIFC.IfcAPI(); await api.Init(); api.SetLogLevel(6);
const m = api.OpenModel(new Uint8Array(fs.readFileSync(process.argv[2])), { COORDINATE_TO_ORIGIN: false });
const want = new Set(process.argv.slice(3));
const types = [WebIFC.IFCCOLUMN, WebIFC.IFCBEAM, WebIFC.IFCSLAB, WebIFC.IFCWALL, WebIFC.IFCWALLSTANDARDCASE, WebIFC.IFCFOOTING, WebIFC.IFCBUILDINGELEMENTPROXY, WebIFC.IFCMEMBER, WebIFC.IFCPLATE];
for (const t of types) for (const id of api.GetLineIDsWithType(m, t)) {
  const l = api.GetLine(m, id); const g = l.GlobalId?.value; if (!want.has(g)) continue;
  const fm = api.GetFlatMesh(m, id); const mn = [1e9,1e9,1e9], mx = [-1e9,-1e9,-1e9];
  for (let i = 0; i < fm.geometries.size(); i++) { const pg = fm.geometries.get(i); const geo = api.GetGeometry(m, pg.geometryExpressID); const v = api.GetVertexArray(geo.GetVertexData(), geo.GetVertexDataSize()); const T = pg.flatTransformation;
    for (let k = 0; k < v.length; k += 6) { const x=v[k],y=v[k+1],z=v[k+2]; const X=T[0]*x+T[4]*y+T[8]*z+T[12], Y=T[1]*x+T[5]*y+T[9]*z+T[13], Z=T[2]*x+T[6]*y+T[10]*z+T[14];
      // web-ifc is Y-up: (X,Y,Z)=(x, z, -y)_ifc
      const w = [X, -Z, Y]; for (let a = 0; a < 3; a++) { mn[a] = Math.min(mn[a], w[a]); mx[a] = Math.max(mx[a], w[a]); } } }
  console.log(g, JSON.stringify({ min: mn.map(x => +x.toFixed(4)), max: mx.map(x => +x.toFixed(4)) }));
}
