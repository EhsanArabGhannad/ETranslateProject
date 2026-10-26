import { readPaperLayout, mmToPixels } from './editor-view-layout.js';
export const pageGap = 28;
export function geometry(source, headerMm = 0, footerMm = 0) {
    const layout = readPaperLayout(source);
    if (!layout || ![headerMm, footerMm].every(n => Number.isFinite(n) && n >= 0) || headerMm > 45 || footerMm > 35) return null;
    const headerReserve = headerMm > 0 ? Math.ceil(headerMm) + 4 : 0, footerReserve = footerMm > 0 ? Math.ceil(footerMm) + 4 : 0;
    const height = layout.height - layout.margins.top - layout.margins.bottom - headerReserve - footerReserve - 8;
    if (height < 60) return null;
    return { ...layout, headerReserve, footerReserve, contentWidth: mmToPixels(layout.width - layout.margins.left - layout.margins.right),
        contentHeight: mmToPixels(height), pageWidth: mmToPixels(layout.width), pageHeight: mmToPixels(layout.height),
        left: mmToPixels(layout.margins.left), top: mmToPixels(layout.margins.top + headerReserve),
        columnGap: mmToPixels(layout.margins.left + layout.margins.right) + pageGap, pitch: mmToPixels(layout.width) + pageGap };
}
export function pageAt(x, metrics) { return Math.max(1, Math.floor((x + 1) / metrics.pitch) + 1); }
