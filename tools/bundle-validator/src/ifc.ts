import fs from 'node:fs';
import { createRequire } from 'node:module';
import type * as WebIFCTypes from 'web-ifc';

const require = createRequire(import.meta.url);
const WebIFC = require('web-ifc') as typeof WebIFCTypes;

let apiPromise: Promise<WebIFCTypes.IfcAPI> | undefined;
async function getApi(): Promise<WebIFCTypes.IfcAPI> {
  apiPromise ??= (async () => {
    const api = new WebIFC.IfcAPI();
    await api.Init();
    api.SetLogLevel(6); // LOG_LEVEL_OFF: web-ifc is very chatty on imperfect real-world IFCs
    return api;
  })();
  return apiPromise;
}

export interface Storey { name: string; elevation: number | null; expressId: number }

/**
 * A model opened with web-ifc using COORDINATE_TO_ORIGIN:false, so coordinates are IFC world coordinates.
 * web-ifc (0.0.80, pinned) applies the IfcProject length unit (applyLinearScalingFactor, default true), so geometry is in metres
 * (verified with a millimetre fixture), but emits a Y-up frame: (X,Y,Z)_webifc = (X,Z,-Y)_ifc. We convert back to IFC Z-up XY.
 * The file is read through a callback (random access), so very large IFCs are not duplicated in JS memory.
 */
export class IfcModel {
  private fd = -1;
  private modelId = -1;
  private api!: WebIFCTypes.IfcAPI;
  private gidToExpress: Map<string, number> | undefined;
  schema = '';
  linearScale = 1;

  static async open(path: string): Promise<IfcModel> {
    const m = new IfcModel();
    m.api = await getApi();
    m.fd = fs.openSync(path, 'r');
    const fd = m.fd;
    const size = fs.fstatSync(fd).size;
    m.modelId = m.api.OpenModelFromCallback((offset: number, len: number) => {
      const n = Math.max(0, Math.min(len, size - offset));
      const buf = new Uint8Array(n);
      if (n > 0) fs.readSync(fd, buf, 0, n, offset);
      return buf;
    }, { COORDINATE_TO_ORIGIN: false });
    if (m.modelId < 0) { m.close(); throw new Error('web-ifc could not open model'); }
    try { m.schema = m.api.GetModelSchema(m.modelId); } catch { /* ignore */ }
    m.linearScale = readLinearScale(m.api, m.modelId);
    return m;
  }

  storeys(): Storey[] {
    const ids = this.api.GetLineIDsWithType(this.modelId, WebIFC.IFCBUILDINGSTOREY);
    const out: Storey[] = [];
    for (let i = 0; i < ids.size(); i++) {
      const id = ids.get(i);
      const l = this.api.GetLine(this.modelId, id);
      const name = l?.Name?.value ?? '';
      const elev = typeof l?.Elevation?.value === 'number' ? l.Elevation.value * this.linearScale : null;
      out.push({ name: String(name), elevation: elev, expressId: id });
    }
    return out;
  }

  /** Map IFC GlobalId -> expressID for all IfcProduct instances. */
  globalIds(): Map<string, number> {
    if (this.gidToExpress) return this.gidToExpress;
    const map = new Map<string, number>();
    const ids = this.api.GetLineIDsWithType(this.modelId, WebIFC.IFCPRODUCT, true);
    for (let i = 0; i < ids.size(); i++) {
      const id = ids.get(i);
      try {
        const l = this.api.GetLine(this.modelId, id, false);
        const g = l?.GlobalId?.value;
        if (typeof g === 'string') map.set(g, id);
      } catch { /* skip unreadable line */ }
    }
    this.gidToExpress = map;
    return map;
  }

  /**
   * IFC world-space (Z-up) XY bbox centre (metres) for each requested expressID, streaming meshes (no geometry kept).
   * Entries are absent for elements without triangle geometry.
   */
  xyCentres(expressIds: number[]): Map<number, [number, number]> {
    const res = new Map<number, [number, number]>();
    const CHUNK = 2000;
    for (let s = 0; s < expressIds.length; s += CHUNK) {
      const part = expressIds.slice(s, s + CHUNK);
      this.api.StreamMeshes(this.modelId, part, (mesh) => {
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        const geoms = mesh.geometries;
        for (let g = 0; g < geoms.size(); g++) {
          const pg = geoms.get(g);
          if (pg.geomType !== undefined && pg.geomType !== 0) continue;
          const geom = this.api.GetGeometry(this.modelId, pg.geometryExpressID);
          const verts = this.api.GetVertexArray(geom.GetVertexData(), geom.GetVertexDataSize());
          const t = pg.flatTransformation; // column-major 4x4
          for (let i = 0; i + 5 < verts.length; i += 6) {
            const x = verts[i], y = verts[i + 1], z = verts[i + 2];
            const wx = t[0] * x + t[4] * y + t[8] * z + t[12];
            // web-ifc emits Y-up coordinates: (X, Y, Z)_webifc = (X, Z, -Y)_ifc. Convert back to IFC world (Z-up) XY.
            const wz = t[2] * x + t[6] * y + t[10] * z + t[14];
            const wy = -wz;
            if (wx < minX) minX = wx; if (wx > maxX) maxX = wx;
            if (wy < minY) minY = wy; if (wy > maxY) maxY = wy;
          }
          geom.delete?.();
        }
        if (minX <= maxX) res.set(mesh.expressID, [(minX + maxX) / 2, (minY + maxY) / 2]);
      }, true);
    }
    return res;
  }

  close(): void {
    try { if (this.modelId >= 0) this.api.CloseModel(this.modelId); } catch { /* ignore */ }
    this.modelId = -1;
    if (this.fd >= 0) { try { fs.closeSync(this.fd); } catch { /* ignore */ } this.fd = -1; }
  }
}

const PREFIX: Record<string, number> = {
  EXA: 1e18, PETA: 1e15, TERA: 1e12, GIGA: 1e9, MEGA: 1e6, KILO: 1e3, HECTO: 1e2, DECA: 10,
  DECI: 1e-1, CENTI: 1e-2, MILLI: 1e-3, MICRO: 1e-6, NANO: 1e-9, PICO: 1e-12, FEMTO: 1e-15, ATTO: 1e-18,
};

/** Metres per file length unit from IfcProject.UnitsInContext (informational: web-ifc already applies it). */
function readLinearScale(api: WebIFCTypes.IfcAPI, modelId: number): number {
  try {
    const siScale = (u: any): number => (u?.Prefix?.value ? (PREFIX[u.Prefix.value] ?? 1) : 1);
    const projects = api.GetLineIDsWithType(modelId, WebIFC.IFCPROJECT);
    if (projects.size() > 0) {
      const proj = api.GetLine(modelId, projects.get(0), true);
      for (const u of proj?.UnitsInContext?.Units ?? []) {
        const unit = u?.value ?? u;
        if (unit?.UnitType?.value === 'LENGTHUNIT') {
          if (unit.ConversionFactor) {
            const mwu = unit.ConversionFactor;
            return Number(mwu.ValueComponent?.value ?? 1) * siScale(mwu.UnitComponent);
          }
          return siScale(unit);
        }
      }
    }
  } catch { /* fall through */ }
  return 1;
}
