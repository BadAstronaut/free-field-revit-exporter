import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { makeFixture } from '../fixture/make-fixture.js';
import { validateBundle } from '../validate.js';
import { toMarkdown } from '../report.js';
import { Zip } from '../zip.js';
import { IfcModel } from '../ifc.js';

const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'ffvalidator-test-'));
test.after(() => fs.rmSync(dir, { recursive: true, force: true }));

const failing = (r: Awaited<ReturnType<typeof validateBundle>>) =>
  [...r.checks.filter((c) => c.status === 'FAIL').map((c) => c.name), ...r.viewports.filter((v) => v.status === 'FAIL').map((v) => 'geometry ' + v.viewName)];

test('good bundle (IFC in metres) passes all checks', async () => {
  const r = await validateBundle(await makeFixture({ outPath: path.join(dir, 'good-m.ffbundle') }));
  assert.deepEqual(failing(r), []);
  assert.equal(r.ok, true);
  assert.equal(r.viewports.length, 1);
  const v = r.viewports[0];
  assert.equal(v.indexed, 5);
  assert.equal(v.inIfc, 4);          // the ghost gid is ignored
  assert.equal(v.withGeometry, 4);
  assert.ok(v.medianErrM! < 0.01, `median ${v.medianErrM}`);
  assert.ok(Math.abs(v.detRatio! - 1) < 1e-9, `det ratio ${v.detRatio}`);
  assert.match(toMarkdown(r), /\| PASS \| A101 \| Level 1 \|/);
});

test('good bundle with IFC in millimetres passes (linear unit scaling honoured)', async () => {
  const r = await validateBundle(await makeFixture({ outPath: path.join(dir, 'good-mm.ffbundle'), units: 'mm' }));
  assert.deepEqual(failing(r), []);
  assert.ok(r.viewports[0].medianErrM! < 0.01);
});

test('wrong transform (3 m shift) fails the geometric check only', async () => {
  const r = await validateBundle(await makeFixture({ outPath: path.join(dir, 'bad-shift.ffbundle'), shiftXM: 3 }));
  assert.equal(r.ok, false);
  assert.deepEqual(failing(r), ['geometry Level 1']);
  assert.ok(Math.abs(r.viewports[0].medianErrM! - 3) < 0.05);
});

test('sheet without a mapped viewport FAILS when includeUnmappedSheets=false', async () => {
  const r = await validateBundle(await makeFixture({ outPath: path.join(dir, 'unmapped-strict.ffbundle'), unmapped: { includeUnmappedSheets: false } }));
  assert.ok(failing(r).includes('sheet A101 has a mapped viewport'), failing(r).join(','));
});

test('unmapped sheet is accepted when includeUnmappedSheets=true', async () => {
  const r = await validateBundle(await makeFixture({ outPath: path.join(dir, 'unmapped-ok.ffbundle'), unmapped: { includeUnmappedSheets: true } }));
  assert.ok(!failing(r).some((n) => n.includes('has a mapped viewport')), failing(r).join(','));
});

test('storey name missing in IFC fails', async () => {
  const r = await validateBundle(await makeFixture({ outPath: path.join(dir, 'bad-storey.ffbundle'), wrongStorey: true }));
  assert.equal(r.ok, false);
  assert.ok(failing(r).some((n) => n.includes('storey')));
});

test('PDF page size mismatch fails', async () => {
  const r = await validateBundle(await makeFixture({ outPath: path.join(dir, 'bad-page.ffbundle'), wrongPageSize: true }));
  assert.equal(r.ok, false);
  assert.ok(failing(r).includes('pdf A101'));
});

test('PDF without embedded freefield-sheet.json fails', async () => {
  const r = await validateBundle(await makeFixture({ outPath: path.join(dir, 'no-att.ffbundle'), noAttachment: true }));
  assert.equal(r.ok, false);
  assert.ok(failing(r).includes('pdf A101 embedded json (pdf.js)'));
});

test('embedded payload is readable through pdf.js and self-sufficient', async () => {
  const r = await validateBundle(await makeFixture({ outPath: path.join(dir, 'att.ffbundle') }));
  const c = r.checks.find((x) => x.name === 'pdf A101 embedded json (pdf.js)');
  assert.equal(c?.status, 'PASS', c?.detail);
});

// Axis-convention guard: web-ifc emits Y-up (X, Z, -Y). Fixture walls have heights (z) far from their y, so using
// z (or +z) instead of -z for model Y would put every centre >20 m off. Expected values are the authored IFC XY.
test('IfcModel.xyCentres returns IFC Z-up XY (modelX = vx, modelY = -vz)', async () => {
  const bundle = await makeFixture({ outPath: path.join(dir, 'axis.ffbundle') });
  const zip = await Zip.open(bundle);
  const ifcPath = path.join(dir, 'axis.ifc');
  await zip.extract('models/arch.ifc', ifcPath); zip.close();
  const ifc = await IfcModel.open(ifcPath);
  try {
    const g = ifc.globalIds();
    const c = ifc.xyCentres([...g.values()]);
    const expect: Record<string, [number, number]> = {
      '3fixtureWallA00000000A': [15, 23.1], '3fixtureWallB00000000B': [18.1, 25.5],
      '3fixtureSlabS00000000S': [17, 27], '3fixtureWallC00000000C': [21.9, 25],
    };
    for (const [gid, [x, y]] of Object.entries(expect)) {
      const got = c.get(g.get(gid)!)!;
      assert.ok(Math.abs(got[0] - x) < 1e-3 && Math.abs(got[1] - y) < 1e-3, `${gid}: ${got} vs ${x},${y}`);
    }
  } finally { ifc.close(); }
});
