// Local copy of the relevant bundle types (see schema/ffbundle.d.ts, which is the shared contract).
export type {
  BundleManifest, SheetEntry, ViewportEntry, ModelEntry, ElementsFile, SheetElementIndex, DataTable,
} from '../../../schema/ffbundle.js';

export type Status = 'PASS' | 'FAIL' | 'WARN' | 'SKIP';

export interface CheckResult {
  name: string;
  status: Status;
  detail?: string;
}

export interface ViewportReport {
  sheetNumber: string;
  viewportId: string;
  viewName: string;
  modelId: string;
  storeyName: string;
  indexed: number;      // indexed elements for this viewport
  inIfc: number;        // ... whose gid exists in the IFC
  withGeometry: number; // ... that have usable geometry (denominator)
  within: number;       // distance <= tolerance
  fractionWithin: number;
  medianErrM: number | null;
  p90ErrM: number | null;
  maxErrM: number | null;
  detRatio: number | null; // |det(pdfToModel)| / (scale*0.0254/72)^2
  status: Status;
  detail: string;
}

export interface ValidationReport {
  bundle: string;
  ok: boolean;
  checks: CheckResult[];
  viewports: ViewportReport[];
  thresholds: { toleranceM: number; minFraction: number; maxMedianM: number };
  durationMs: number;
}
