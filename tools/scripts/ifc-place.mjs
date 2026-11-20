// Usage: node ifc-place.mjs <file.ifc> <GlobalId> [...]  -> world placement origin of entities (metres) + storey names.
import fs from 'node:fs';
const txt = fs.readFileSync(process.argv[2], 'latin1');
const ents = new Map();
for (const m of txt.matchAll(/#(\d+)\s*=\s*(\w+)\((.*?)\);\s*$/gms)) ents.set(+m[1], { t: m[2], a: m[3] });
const num = s => s.split(',').map(Number);
const ref = s => +s.trim().slice(1);
function args(a) { const out = []; let d = 0, cur = ''; for (const ch of a) { if (ch === '(') d++; if (ch === ')') d--; if (ch === ',' && d === 0) { out.push(cur.trim()); cur = ''; } else cur += ch; } out.push(cur.trim()); return out; }
const pt = id => num(ents.get(id).a.replace(/[()]/g, ''));
const dir = id => num(ents.get(id).a.replace(/[()]/g, ''));
function axis(id) { const a = args(ents.get(id).a); const o = pt(ref(a[0])); const z = a[1] && a[1] !== '$' ? dir(ref(a[1])) : [0, 0, 1]; const x = a[2] && a[2] !== '$' ? dir(ref(a[2])) : [1, 0, 0];
  const y = [z[1]*x[2]-z[2]*x[1], z[2]*x[0]-z[0]*x[2], z[0]*x[1]-z[1]*x[0]]; return { o: [o[0], o[1], o[2] ?? 0], x, y, z }; }
const apply = (T, p) => [0,1,2].map(i => T.o[i] + T.x[i]*p[0] + T.y[i]*p[1] + T.z[i]*p[2]);
const compose = (P, C) => ({ o: apply(P, C.o), x: [0,1,2].map(i => P.x[i]*C.x[0]+P.y[i]*C.x[1]+P.z[i]*C.x[2]), y: [0,1,2].map(i => P.x[i]*C.y[0]+P.y[i]*C.y[1]+P.z[i]*C.y[2]), z: [0,1,2].map(i => P.x[i]*C.z[0]+P.y[i]*C.z[1]+P.z[i]*C.z[2]) });
const ID = { o: [0,0,0], x: [1,0,0], y: [0,1,0], z: [0,0,1] };
function world(id) { const a = args(ents.get(id).a); const rel = axis(ref(a[1])); return a[0] === '$' ? rel : compose(world(ref(a[0])), rel); }
export function placement(guid) {
  for (const [id, e] of ents) if (e.a.startsWith("'" + guid + "'")) { const a = args(e.a); return { type: e.t, id, name: a[2], origin: world(ref(a[5])).o }; }
}
if (process.argv[1].endsWith('ifc-place.mjs')) {
  for (const g of process.argv.slice(3)) console.log(g, JSON.stringify(placement(g)));
  const storeys = [...ents].filter(([, e]) => e.t === 'IFCBUILDINGSTOREY').map(([id, e]) => { const a = args(e.a); return `${a[2]}@z=${world(ref(a[5])).o[2].toFixed(3)}`; });
  console.log('storeys', storeys.join(' | '));
  const site = [...ents].filter(([, e]) => e.t === 'IFCSITE').map(([id, e]) => world(ref(args(e.a)[5])).o); console.log('site origin', JSON.stringify(site));
  console.log('header', txt.match(/FILE_SCHEMA\(\(.*?\)\)/)?.[0], txt.match(/FILE_NAME\([^;]*;/s)?.[0].replace(/\s+/g,' ').slice(0,250));
}
