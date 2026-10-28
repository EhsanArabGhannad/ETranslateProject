export const cellStyleFields = Object.freeze(['backgroundColor', 'borderColor', 'borderWidthPt', 'borderStyle']);
export function validCellStyleValue(field, value) {
    if (!cellStyleFields.includes(field)) return false;
    if (value === null) return true;
    if (field === 'borderWidthPt') return [0, .5, 1, 1.5, 2, 3].includes(value);
    if (field === 'borderStyle') return ['solid', 'dashed', 'dotted'].includes(value);
    return typeof value === 'string' && value.length === 7 && /^#[0-9a-f]{6}$/i.test(value);
}
export function validCellStyleChange(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 3 &&
        ['scope', 'field', 'value'].every(key => Object.hasOwn(value, key)) && ['selection', 'table'].includes(value.scope) &&
        (value.field === 'reset' ? value.value === null : validCellStyleValue(value.field, value.value));
}
export function applyCellStyle(element, attrs) {
    if (attrs?.backgroundColor != null) element.style.backgroundColor = attrs.backgroundColor;
    if (attrs?.borderColor != null) element.style.borderColor = attrs.borderColor;
    if (attrs?.borderStyle != null) element.style.borderStyle = attrs.borderStyle;
    if (attrs?.borderWidthPt != null) {
        element.style.borderWidth = `${attrs.borderWidthPt}pt`;
        // hidden wins the collapsed-border conflict, including the neighbouring edge.
        if (attrs.borderWidthPt === 0) element.style.borderStyle = 'hidden';
    }
}
