// View-only geometry. Keep the accepted layout vocabulary aligned with DocumentPageLayout;
// these values never enter the document, editor history, save request or PDF renderer.
const sizes = Object.freeze({ A4: [210, 297], A5: [148, 210], Letter: [215.9, 279.4], Legal: [215.9, 355.6] });
const fields = (value, names) => value && !Array.isArray(value) && typeof value === 'object' &&
    Object.keys(value).sort().join(',') === [...names].sort().join(',');

export function readPaperLayout(source) {
    try {
        if (source == null || source === '') source = '{"pageSize":"A4","orientation":"Portrait","marginsMm":{"top":25,"right":20,"bottom":20,"left":20}}';
        if (typeof source !== 'string' || source.length > 10000) return null;
        const value = JSON.parse(source);
        const names = ['pageSize', 'orientation', 'marginsMm'];
        if (!fields(value, names) && !fields(value, [...names, 'footerPlacement'])) return null;
        if (typeof value.pageSize !== 'string' || !Object.hasOwn(sizes, value.pageSize) || !['Portrait', 'Landscape'].includes(value.orientation) ||
            Object.hasOwn(value, 'footerPlacement') && !['AllPages', 'LastPage'].includes(value.footerPlacement)) return null;
        const margins = value.marginsMm;
        if (!fields(margins, ['top', 'right', 'bottom', 'left']) ||
            !Object.values(margins).every(number => typeof number === 'number' && Number.isFinite(number) && number >= 5 && number <= 50)) return null;
        const [width, height] = value.orientation === 'Landscape' ? [...sizes[value.pageSize]].reverse() : sizes[value.pageSize];
        if (width - margins.left - margins.right < 60 || height - margins.top - margins.bottom < 80) return null;
        return Object.freeze({ pageSize: value.pageSize, orientation: value.orientation, width, height, margins: Object.freeze(margins) });
    } catch { return null; }
}

export const mmToPixels = mm => mm * 96 / 25.4;
export function paperScale(mode, availableWidth, paperWidth) {
    if (!Number.isFinite(availableWidth) || availableWidth <= 0 || !Number.isFinite(paperWidth) || paperWidth <= 0) return 1;
    if (mode === 'fit') return Math.max(0.05, Math.min(1, availableWidth / paperWidth));
    return new Map([['75', 0.75], ['100', 1], ['125', 1.25], ['150', 1.5]]).get(mode) ?? 1;
}
