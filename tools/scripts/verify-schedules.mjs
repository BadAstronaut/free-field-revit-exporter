import fs from 'node:fs'; import path from 'node:path'; import { pathToFileURL } from 'node:url';
const o = process.argv[2];
const mcp = JSON.parse(fs.readFileSync(path.join(o, 'mcp-schedules.json'), 'utf8').replace(/^\uFEFF/, ''));
const els = JSON.parse(fs.readFileSync(path.join(o, 'x/data/elements.json'), 'utf8'));
const uids = new Set(Object.values(els).map(e => e.uid));
const man = JSON.parse(fs.readFileSync(path.join(o, 'x/manifest.json'), 'utf8'));
for (const [key, file] of [['framing', 'ff_structural_framing_schedule'], ['column', 'ff_structural_column_schedule']]) {
  const t = JSON.parse(fs.readFileSync(path.join(o, `x/data/schedules/${file}.json`), 'utf8'));
  const body = mcp[key].slice(1).filter(r => r.some(c => c !== ''));
  const ours = t.rows.map(r => r.cells.join('\u001f')); const theirs = body.map(r => r.join('\u001f'));
  const same = ours.length === theirs.length && ours.every((x, i) => x === theirs[i]);
  const inEl = t.rows.filter(r => els[r.gid]).length, uidOk = t.rows.filter(r => r.uid && r.gid).length;
  console.log(`${t.name}: rows ${t.rows.length}, MCP nonblank body rows ${body.length}, cells identical in order: ${same}, rows with gid ${uidOk}, gid in elements.json ${inEl}`);
  console.log('  sample:', t.rows[0].cells.join(' | '), '| gid', t.rows[0].gid);
}
const pdfjs = await import(pathToFileURL(path.join(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')).replace(/%20/g,' '), '..', 'bundle-validator', 'node_modules', 'pdfjs-dist', 'legacy', 'build', 'pdf.mjs')).href);
for (const s of man.sheets) {
  const d = await pdfjs.getDocument({ data: new Uint8Array(fs.readFileSync(path.join(o, 'x', s.file))), verbosity: 0 }).promise;
  const att = await d.getAttachments(); const f = att && att['freefield-sheet.json'];
  const p = f ? JSON.parse(Buffer.from(f.content).toString('utf8')) : null;
  console.log(s.number, 'embedded schedules:', p?.schedules ? p.schedules.map(x => `${x.name}(${x.rows.length} rows)`).join(', ') : '-');
}


