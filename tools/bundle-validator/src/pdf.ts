import { PDFDocument, PDFName, PDFDict, PDFArray, PDFHexString, PDFString, PDFRef, PDFStream, decodePDFRawStream, PDFRawStream } from 'pdf-lib';

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
  const attachments: string[] = [];
  let embeddedSheetJson: unknown;
  try {
    const names = doc.catalog.lookupMaybe(PDFName.of('Names'), PDFDict);
    const ef = names?.lookupMaybe(PDFName.of('EmbeddedFiles'), PDFDict);
    const arr = ef?.lookupMaybe(PDFName.of('Names'), PDFArray);
    if (arr) {
      for (let i = 0; i + 1 < arr.size(); i += 2) {
        const key = arr.lookup(i);
        const name = key instanceof PDFHexString || key instanceof PDFString ? key.decodeText() : String(key);
        attachments.push(name);
        if (name === attachmentName) {
          const spec = arr.lookup(i + 1, PDFDict);
          const stream = spec.lookup(PDFName.of('EF'), PDFDict).lookup(PDFName.of('F'));
          if (stream instanceof PDFRawStream) {
            const data = decodePDFRawStream(stream).decode();
            embeddedSheetJson = JSON.parse(Buffer.from(data).toString('utf8'));
          }
        }
      }
    }
  } catch { /* attachments are best effort */ }
  void PDFRef; void PDFStream;
  return { pageCount, widthPt: w, heightPt: h, attachments, embeddedSheetJson };
}
