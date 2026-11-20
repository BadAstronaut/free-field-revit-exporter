import type { ValidationReport } from './types.js';

const fmt = (x: number | null, d = 3) => (x == null ? '-' : x.toFixed(d));

export function toMarkdown(r: ValidationReport): string {
  const L: string[] = [];
  L.push(`# Bundle validation: ${r.bundle}`, '');
  L.push(`Result: **${r.ok ? 'PASS' : 'FAIL'}** (${(r.durationMs / 1000).toFixed(1)} s)`, '');
  L.push(`Thresholds: >= ${(r.thresholds.minFraction * 100).toFixed(0)}% of elements within ${r.thresholds.toleranceM} m, median < ${r.thresholds.maxMedianM} m.`, '');
  L.push('## Checks', '', '| Status | Check | Detail |', '|---|---|---|');
  for (const c of r.checks) L.push(`| ${c.status} | ${c.name} | ${(c.detail ?? '').replace(/\|/g, '\\|')} |`);
  L.push('', '## Geometric check per plan viewport', '');
  if (!r.viewports.length) L.push('_No plan viewports checked._');
  else {
    L.push('| Status | Sheet | Viewport | Storey | Indexed | In IFC | Geometry | Within tol | Fraction | Median (m) | P90 (m) | Max (m) | det ratio | Note |');
    L.push('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|');
    for (const v of r.viewports)
      L.push(`| ${v.status} | ${v.sheetNumber} | ${v.viewName} | ${v.storeyName} | ${v.indexed} | ${v.inIfc} | ${v.withGeometry} | ${v.within} | ${(v.fractionWithin * 100).toFixed(1)}% | ${fmt(v.medianErrM)} | ${fmt(v.p90ErrM)} | ${fmt(v.maxErrM)} | ${fmt(v.detRatio)} | ${v.detail.replace(/\|/g, '\\|')} |`);
    L.push('', '`det ratio` = |det(pdfToModel)| / (viewScale * 0.0254 / 72)^2, expected ~1.');
  }
  return L.join('\n') + '\n';
}

export function toConsole(r: ValidationReport): string {
  const L: string[] = [];
  for (const c of r.checks) L.push(`[${c.status}] ${c.name}${c.detail ? ' - ' + c.detail : ''}`);
  for (const v of r.viewports)
    L.push(`[${v.status}] geometry ${v.sheetNumber}/${v.viewName} (${v.storeyName}): ${v.within}/${v.withGeometry} within tol (${(v.fractionWithin * 100).toFixed(1)}%), median ${fmt(v.medianErrM)} m, det ratio ${fmt(v.detRatio)}${v.detail ? ' - ' + v.detail : ''}`);
  L.push(r.ok ? 'RESULT: PASS' : 'RESULT: FAIL');
  return L.join('\n');
}
