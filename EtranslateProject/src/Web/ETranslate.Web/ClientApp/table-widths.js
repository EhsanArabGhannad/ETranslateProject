// Application percentages, independent of a vendor's pixel-based cell colwidth.
export function validColumnWidths(value, columns) {
    return value === null || Array.isArray(value) && value.length >= 1 && value.length <= 12 &&
        (columns === undefined || value.length === columns) &&
        value.every(width => Number.isInteger(width) && width >= 1 && width <= 100) && value.reduce((sum, width) => sum + width, 0) === 100;
}
export function equalColumnPercentages(columns) {
    const base = Math.floor(100 / columns);
    return Array.from({ length: columns }, (_, index) => base + (index >= columns - 100 % columns ? 1 : 0));
}
export function appendColumnGroup(table, widths) {
    if (!Array.isArray(widths) || !validColumnWidths(widths)) return;
    const group = document.createElement('colgroup');
    for (const width of widths) { const col = document.createElement('col'); col.style.width = `${width}%`; group.append(col); }
    table.append(group);
}
