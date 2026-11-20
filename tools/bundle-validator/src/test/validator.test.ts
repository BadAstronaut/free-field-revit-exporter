import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { makeFixture } from '../fixture/make-fixture.js';
import { validateBundle } from '../validate.js';
import { toMarkdown } from '../report.js';

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
