// Generates a tiny synthetic bundle: minimal IFC4 (3 walls + 1 slab at known coordinates), a matching single-page PDF
// (pdf-lib) and a manifest. Used by `npm test` and as a worked example of the coordinate contract.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import yazl from 'yazl';
import { PDFDocument, rgb } from 'pdf-lib';

export interface FixtureOptions {
  outPath: string;
  /** IFC file length unit. */
  units?: 'm' | 'mm';
  /** Shift pdfToModel by this many metres in X (control points are kept consistent), to provoke a geometric failure. */
  shiftXM?: number;
  /** Use a storey name in the manifest that does not exist in the IFC. */
  wrongStorey?: boolean;
  /** Declare a wrong page size in the manifest. */
  wrongPageSize?: boolean;
}

const BUILDING_OFFSET = [10, 20]; // metres; verifies placement chaining
const PAGE = { w: 1000, h: 800 };
const SCALE = 100; // 1:100
const S = (SCALE * 0.0254) / 72; // metres per PDF point
// pdfToModel: mx = S*px + C, my = -S*py + F  (reflection: PDF y-down -> model y-up)
const C = 5, F = 40;

interface El { name: string; gid: string; cls: 'IFCWALL' | 'IFCSLAB'; origin: [number, number, number]; refDir?: [number, number]; xdim: number; ydim: number; pos2d: [number, number]; depth: number;
  /** expected world XY centre in metres */ centre: [number, number]; /** world XY bbox */ bbox: [number, number, number, number] }

const ELS: El[] = [
  { name: 'Wall A', gid: '3fixtureWallA00000000A', cls: 'IFCWALL', origin: [2, 3, 0], xdim: 6, ydim: 0.2, pos2d: [3, 0.1], depth: 3, centre: [15, 23.1], bbox: [12, 23, 18, 23.2] },
  { name: 'Wall B', gid: '3fixtureWallB00000000B', cls: 'IFCWALL', origin: [8, 3, 0], xdim: 0.2, ydim: 5, pos2d: [0.1, 2.5], depth: 3, centre: [18.1, 25.5], bbox: [18, 23, 18.2, 28] },
  { name: 'Slab S', gid: '3fixtureSlabS00000000S', cls: 'IFCSLAB', origin: [2, 3, -0.3], xdim: 10, ydim: 8, pos2d: [5, 4], depth: 0.3, centre: [17, 27], bbox: [12, 23, 22, 31] },
  { name: 'Wall C', gid: '3fixtureWallC00000000C', cls: 'IFCWALL', origin: [12, 3, 0], refDir: [0, 1], xdim: 4, ydim: 0.2, pos2d: [2, 0.1], depth: 3, centre: [21.9, 25], bbox: [21.8, 23, 22, 27] },
];
const FAKE_GID = '3fixtureNotInIfc00000N';

function buildIfc(units: 'm' | 'mm'): string {
  const k = units === 'mm' ? 1000 : 1;
  const n = (v: number) => { const x = v * k; return Number.isInteger(x) ? `${x}.` : String(+x.toPrecision(12)); };
  const L: string[] = [];
  let id = 100;
  const add = (s: string): number => { L.push(`#${++id}=${s};`); return id; };
  const prj = add(`IFCPROJECT('3fixtureProject0000000',$,'Fixture',$,$,$,$,(#20),#10)`);
  L.push(`#10=IFCUNITASSIGNMENT((#11));`);
  L.push(`#11=IFCSIUNIT(*,.LENGTHUNIT.,${units === 'mm' ? '.MILLI.' : '$'},.METRE.);`);
  L.push(`#20=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05,#21,$);`);
  L.push(`#21=IFCAXIS2PLACEMENT3D(#22,$,$);`);
  L.push(`#22=IFCCARTESIANPOINT((0.,0.,0.));`);
  L.push(`#23=IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*,#20,$,.MODEL_VIEW.,$);`);
  L.push(`#24=IFCDIRECTION((0.,0.,1.));`);
  const sitePl = add(`IFCLOCALPLACEMENT($,#21)`);
  const site = add(`IFCSITE('3fixtureSite0000000000',$,'Site',$,$,#${sitePl},$,$,.ELEMENT.,$,$,$,$,$)`);
  const bPt = add(`IFCCARTESIANPOINT((${n(BUILDING_OFFSET[0])},${n(BUILDING_OFFSET[1])},0.))`);
  const bAx = add(`IFCAXIS2PLACEMENT3D(#${bPt},$,$)`);
  const bPl = add(`IFCLOCALPLACEMENT(#${sitePl},#${bAx})`);
  const bld = add(`IFCBUILDING('3fixtureBuilding00000',$,'Building',$,$,#${bPl},$,$,.ELEMENT.,$,$,$)`);
  const stPl = add(`IFCLOCALPLACEMENT(#${bPl},#21)`);
  const storey = add(`IFCBUILDINGSTOREY('3fixtureStorey1000000',$,'Level 1',$,$,#${stPl},$,$,.ELEMENT.,0.)`);
  add(`IFCRELAGGREGATES('3fixtureRel1000000000',$,$,$,#${prj},(#${site}))`);
  add(`IFCRELAGGREGATES('3fixtureRel2000000000',$,$,$,#${site},(#${bld}))`);
  add(`IFCRELAGGREGATES('3fixtureRel3000000000',$,$,$,#${bld},(#${storey}))`);
  const products: number[] = [];
  for (const e of ELS) {
    const pt = add(`IFCCARTESIANPOINT((${n(e.origin[0])},${n(e.origin[1])},${n(e.origin[2])}))`);
    let ax: number;
    if (e.refDir) {
      const rd = add(`IFCDIRECTION((${e.refDir[0]}.,${e.refDir[1]}.,0.))`);
      ax = add(`IFCAXIS2PLACEMENT3D(#${pt},$,#${rd})`);
    } else ax = add(`IFCAXIS2PLACEMENT3D(#${pt},$,$)`);
    const pl = add(`IFCLOCALPLACEMENT(#${stPl},#${ax})`);
    const p2 = add(`IFCCARTESIANPOINT((${n(e.pos2d[0])},${n(e.pos2d[1])}))`);
    const a2 = add(`IFCAXIS2PLACEMENT2D(#${p2},$)`);
    const prof = add(`IFCRECTANGLEPROFILEDEF(.AREA.,$,#${a2},${n(e.xdim)},${n(e.ydim)})`);
    const solid = add(`IFCEXTRUDEDAREASOLID(#${prof},#21,#24,${n(e.depth)})`);
    const rep = add(`IFCSHAPEREPRESENTATION(#23,'Body','SweptSolid',(#${solid}))`);
    const shape = add(`IFCPRODUCTDEFINITIONSHAPE($,$,(#${rep}))`);
    const pred = e.cls === 'IFCWALL' ? '.STANDARD.' : '.FLOOR.';
    products.push(add(`${e.cls}('${e.gid}',$,'${e.name}',$,$,#${pl},#${shape},$,${pred})`));
  }
  add(`IFCRELCONTAINEDINSPATIALSTRUCTURE('3fixtureRelC00000000',$,$,$,(${products.map((p) => '#' + p).join(',')}),#${storey})`);
  return [
    'ISO-10303-21;', 'HEADER;', "FILE_DESCRIPTION(('ViewDefinition [ReferenceView]'),'2;1');",
    "FILE_NAME('fixture.ifc','2026-01-01T00:00:00',(''),(''),'','','');", "FILE_SCHEMA(('IFC4'));", 'ENDSEC;', 'DATA;',
    ...L, 'ENDSEC;', 'END-ISO-10303-21;', '',
  ].join('\n');
}

export async function makeFixture(opts: FixtureOptions): Promise<string> {
  const units = opts.units ?? 'm';
  const shift = opts.shiftXM ?? 0;
  const aff: [number, number, number, number, number, number] = [S, 0, C + shift, 0, -S, F];
  const toPdf = (mx: number, my: number): [number, number] => [(mx - C) / S, (F - my) / S]; // true inverse (no shift)
  const sheetId = 'sheet-uid-1', vpId = 'vp-uid-1';

  // PDF
  const doc = await PDFDocument.create();
  const page = doc.addPage([PAGE.w, PAGE.h]);
  const elemsIdx = ELS.map((e) => {
    const a = toPdf(e.bbox[0], e.bbox[3]); // top-left in PDF (max model y)
    const b = toPdf(e.bbox[2], e.bbox[1]);
    const bbox = [Math.min(a[0], b[0]), Math.min(a[1], b[1]), Math.max(a[0], b[0]), Math.max(a[1], b[1])];
    page.drawRectangle({ x: bbox[0], y: PAGE.h - bbox[3], width: bbox[2] - bbox[0], height: bbox[3] - bbox[1], borderColor: rgb(0, 0, 0), borderWidth: 0.5 });
    return { gid: e.gid, vp: vpId, bbox };
  });
  elemsIdx.push({ gid: FAKE_GID, vp: vpId, bbox: [10, 10, 20, 20] }); // not in IFC: must be ignored by the geometric check
  const region = [100, 100, 900, 700];
  const cp = (px: number, py: number) => ({ pdfX: px, pdfY: py, modelX: aff[0] * px + aff[1] * py + aff[2], modelY: aff[3] * px + aff[4] * py + aff[5] });
  const elements: Record<string, unknown> = {};
  for (const e of [...ELS.map((x) => ({ gid: x.gid, name: x.name })), { gid: FAKE_GID, name: 'ghost' }])
    elements[e.gid] = { uid: 'uid-' + e.gid, eid: 1, gid: e.gid, category: 'Walls', bic: 'OST_Walls', family: '', type: e.name, level: 'Level 1', modelId: 'arch', params: { Mark: { v: e.name, d: e.name, t: false, k: 'ALL_MODEL_MARK' } } };
  const index = { sheetId, elements: elemsIdx, links: [] };
  doc.attach(Buffer.from(JSON.stringify({ schema: 'freefield.sheet/1', sheetId, index, elements })), 'freefield-sheet.json', { mimeType: 'application/json', description: 'fixture' });
  const pdfBytes = await doc.save();

  const manifest = {
    schema: 'freefield.bundle/1',
    generator: { name: 'FreeField Exporter', version: '0.1.0', host: 'fixture', hostVersion: '1' },
    exportedAt: '2026-01-01T00:00:00.000Z',
    project: { name: 'Fixture', number: '0', sourceFile: 'fixture' },
    units: { length: 'm', pdf: 'pt' },
    coordinates: { modelSpace: 'ifc', ifcPlacement: 'internal', trueNorthRad: 0 },
    models: [{ id: 'arch', file: 'models/arch.ifc', name: 'Arch', discipline: 'architecture', storeys: [{ name: 'Level 1', elevation: 0, levelUniqueId: 'lvl', ifcGlobalId: '3fixtureStorey1000000' }] }],
    sheets: [{
      id: sheetId, number: 'A101', name: 'First Floor Plan', file: 'sheets/A101.pdf', data: 'data/sheets/A101.json',
      page: opts.wrongPageSize ? { widthPt: 1200, heightPt: 800 } : { widthPt: PAGE.w, heightPt: PAGE.h },
      viewports: [{
        id: vpId, viewId: 'view-1', viewName: 'Level 1', viewType: 'FloorPlan', scale: SCALE, modelId: 'arch',
        storeyName: opts.wrongStorey ? 'Level 99' : 'Level 1', primary: true, pdfRegion: region, pdfToModel: aff,
        controlPoints: [cp(region[0], region[1]), cp(region[2], region[3])],
      }],
    }],
    schedules: [],
    elementsFile: 'data/elements.json',
    stats: { elements: Object.keys(elements).length, sheets: 1, viewports: 1, links: 0 },
  };

  fs.mkdirSync(path.dirname(path.resolve(opts.outPath)), { recursive: true });
  const zip = new yazl.ZipFile();
  zip.addBuffer(Buffer.from(JSON.stringify(manifest, null, 2)), 'manifest.json');
  zip.addBuffer(Buffer.from(buildIfc(units)), 'models/arch.ifc');
  zip.addBuffer(Buffer.from(pdfBytes), 'sheets/A101.pdf');
  zip.addBuffer(Buffer.from(JSON.stringify(elements)), 'data/elements.json');
  zip.addBuffer(Buffer.from(JSON.stringify(index)), 'data/sheets/A101.json');
  await new Promise<void>((resolve, reject) => {
    const out = fs.createWriteStream(opts.outPath);
    out.on('close', resolve); out.on('error', reject);
    zip.outputStream.pipe(out);
    zip.end();
  });
  return opts.outPath;
}

// CLI: node dist/fixture/make-fixture.js [outDir]
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const dir = process.argv[2] ?? path.join(os.tmpdir(), 'ffbundle-fixtures');
  await makeFixture({ outPath: path.join(dir, 'good-m.ffbundle') });
  await makeFixture({ outPath: path.join(dir, 'good-mm.ffbundle'), units: 'mm' });
  await makeFixture({ outPath: path.join(dir, 'bad-shift.ffbundle'), shiftXM: 3 });
  console.log('fixtures written to', dir);
}
