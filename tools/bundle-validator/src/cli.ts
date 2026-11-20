#!/usr/bin/env node
import fs from 'node:fs';
import { validateBundle, DEFAULTS } from './validate.js';
import { toMarkdown, toConsole } from './report.js';

function usage(): never {
  console.error('Usage: node dist/cli.js <bundle.ffbundle> [--report out.md] [--json out.json] [--schema file] [--tolerance 0.5] [--fraction 0.9] [--median 0.25] [--skip-geometry] [-v]');
  process.exit(2);
}

const args = process.argv.slice(2);
if (!args.length || args.includes('-h') || args.includes('--help')) usage();
let bundle = '';
let reportPath: string | undefined, jsonPath: string | undefined, schemaPath: string | undefined;
let toleranceM = DEFAULTS.toleranceM, minFraction = DEFAULTS.minFraction, maxMedianM = DEFAULTS.maxMedianM;
let skipGeometry = false, verbose = false;
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  const next = () => args[++i] ?? usage();
  if (a === '--report') reportPath = next();
  else if (a === '--json') jsonPath = next();
  else if (a === '--schema') schemaPath = next();
  else if (a === '--tolerance') toleranceM = Number(next());
  else if (a === '--fraction') minFraction = Number(next());
  else if (a === '--median') maxMedianM = Number(next());
  else if (a === '--skip-geometry') skipGeometry = true;
  else if (a === '-v' || a === '--verbose') verbose = true;
  else if (a.startsWith('-')) usage();
  else bundle = a;
}
if (!bundle) usage();

const report = await validateBundle(bundle, {
  schemaPath, toleranceM, minFraction, maxMedianM, skipGeometry,
  log: verbose ? (m) => console.error(m) : undefined,
});
console.log(toConsole(report));
if (reportPath) fs.writeFileSync(reportPath, toMarkdown(report));
if (jsonPath) fs.writeFileSync(jsonPath, JSON.stringify(report, null, 2));
process.exit(report.ok ? 0 : 1);
