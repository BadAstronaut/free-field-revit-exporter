import { PDFDocument, PDFName } from 'pdf-lib';

export interface PdfInfo {
  pageCount: number;
  widthPt: number;
  heightPt: number;
  attachments: string[];
  embeddedSheetJson?: unknown;
}

/** Page 1 size as a viewer shows it: CropBox clipped to MediaBox, else MediaBox; swapped for /Rotate 90/270. */
export async function readPdf(bytes: Uint8Array, attachmentName = 'freefield-sheet.json'): Promise<PdfInfo> {
  const doc = await PDFDocument.load(bytes, { updateMetadata: false, ignoreEncryption: true });
  const pageCount = doc.getPageCount();
  let w = 0, h = 0;
  if (pageCount > 0) {
    const page = doc.getPage(0);
    const m = page.getMediaBox();
    let x0 = m.x, y0 = m.y, x1 = m.x + m.width, y1 = m.y + m.height;
    if (page.node.has(PDFName.of('CropBox'))) {
      const c = page.getCropBox();
      const cx0 = Math.max(x0, c.x), cy0 = Math.max(y0, c.y);
      const cx1 = Math.min(x1, c.x + c.width), cy1 = Math.min(y1, c.y + c.height);
      if (cx1 > cx0 && cy1 > cy0) { x0 = cx0; y0 = cy0; x1 = cx1; y1 = cy1; }
    }
    w = x1 - x0; h = y1 - y0;
    const rot = ((page.getRotation().angle % 360) + 360) % 360;
    if (rot === 90 || rot === 270) [w, h] = [h, w];
  }
  const { attachments, embeddedSheetJson } = await readAttachmentsWithPdfJs(bytes, attachmentName);
  return { pageCount, widthPt: w, heightPt: h, attachments, embeddedSheetJson };
}

/** Reads attachments the way the FreeField viewer does: pdf.js `getAttachments()`. */
async function readAttachmentsWithPdfJs(bytes: Uint8Array, attachmentName: string): Promise<{ attachments: string[]; embeddedSheetJson?: unknown; error?: string }> {
  try {
    const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
    const task = pdfjs.getDocument({ data: new Uint8Array(bytes), useSystemFonts: false, isEvalSupported: false, verbosity: 0 });
    const doc = await task.promise;
    try {
      const att = (await doc.getAttachments()) as Record<string, { filename: string; content: Uint8Array }> | null;
      const attachments = Object.values(att ?? {}).map((a) => a.filename);
      const hit = Object.values(att ?? {}).find((a) => a.filename === attachmentName);
      let embeddedSheetJson: unknown;
      if (hit) embeddedSheetJson = JSON.parse(Buffer.from(hit.content).toString('utf8'));
      return { attachments, embeddedSheetJson };
    } finally { await doc.destroy(); }
  } catch (e) {
    return { attachments: [], error: String(e) };
  }
}
