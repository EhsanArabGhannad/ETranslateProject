import { readPaperLayout, mmToPixels } from '../../src/Web/ETranslate.Web/ClientApp/editor-view-layout.js';

export const pageGap = 28;
export function geometry(layoutJson, headerMm = 0, footerMm = 0) {
    const layout = readPaperLayout(layoutJson);
    if (!layout || ![headerMm, footerMm].every(n => Number.isFinite(n) && n >= 0) || headerMm > 45 || footerMm > 35) return null;
    const headerReserve = headerMm > 0 ? Math.ceil(headerMm) + 4 : 0;
    const footerReserve = footerMm > 0 ? Math.ceil(footerMm) + 4 : 0;
    // Match the unchanged renderer's footer-number reserve, including last-page-only custom footers.
    const contentWidth = layout.width - layout.margins.left - layout.margins.right;
    const contentHeight = layout.height - layout.margins.top - layout.margins.bottom - headerReserve - footerReserve - 8;
    if (contentHeight < 60) return null;
    return { ...layout, headerReserve, footerReserve, contentWidth: mmToPixels(contentWidth), contentHeight: mmToPixels(contentHeight),
        pageWidth: mmToPixels(layout.width), pageHeight: mmToPixels(layout.height), pitch: mmToPixels(layout.width) + pageGap,
        left: mmToPixels(layout.margins.left), top: mmToPixels(layout.margins.top + headerReserve),
        columnGap: mmToPixels(layout.margins.left + layout.margins.right) + pageGap };
}
export function pageCount(scrollWidth, metrics) {
    if (!metrics || !Number.isFinite(scrollWidth) || scrollWidth < 0) return null;
    const count = Math.max(1, Math.ceil((scrollWidth + metrics.columnGap - 1) / metrics.pitch));
    return count <= 50 ? count : null;
}
export function pageAt(x, metrics) { return Math.max(1, Math.floor((x + 1) / metrics.pitch) + 1); }
