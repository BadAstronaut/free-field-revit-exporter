import yauzl from 'yauzl';
import fs from 'node:fs';
import { pipeline } from 'node:stream/promises';

/** Random-access zip reader on top of yauzl (never loads whole archive into memory). */
export class Zip {
  private constructor(private zf: yauzl.ZipFile, private entries: Map<string, yauzl.Entry>) {}

  static open(path: string): Promise<Zip> {
    return new Promise((resolve, reject) => {
      yauzl.open(path, { lazyEntries: true, autoClose: false }, (err, zf) => {
        if (err || !zf) return reject(err ?? new Error('cannot open zip'));
        const entries = new Map<string, yauzl.Entry>();
        zf.on('entry', (e: yauzl.Entry) => { entries.set(e.fileName, e); zf.readEntry(); });
        zf.on('end', () => resolve(new Zip(zf, entries)));
        zf.on('error', reject);
        zf.readEntry();
      });
    });
  }

  has(name: string): boolean { return this.entries.has(name); }
  names(): string[] { return [...this.entries.keys()]; }
  size(name: string): number | undefined { return this.entries.get(name)?.uncompressedSize; }

  private stream(name: string): Promise<NodeJS.ReadableStream> {
    const e = this.entries.get(name);
    if (!e) return Promise.reject(new Error(`missing entry ${name}`));
    return new Promise((resolve, reject) => {
      this.zf.openReadStream(e, (err, s) => (err || !s ? reject(err ?? new Error('no stream')) : resolve(s)));
    });
  }

  async read(name: string): Promise<Buffer> {
    const s = await this.stream(name);
    const chunks: Buffer[] = [];
    for await (const c of s) chunks.push(c as Buffer);
    return Buffer.concat(chunks);
  }

  async readJson<T = unknown>(name: string): Promise<T> {
    return JSON.parse((await this.read(name)).toString('utf8')) as T;
  }

  async extract(name: string, dest: string): Promise<void> {
    const s = await this.stream(name);
    await pipeline(s, fs.createWriteStream(dest));
  }

  close(): void { this.zf.close(); }
}
