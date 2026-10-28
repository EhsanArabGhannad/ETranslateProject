export const imageFields = ['assetScope', 'widthPercent', 'align'];
export const validImageValue = (field, value) => value === null && imageFields.includes(field) ||
    field === 'assetScope' && value === 'document' ||
    field === 'widthPercent' && Number.isInteger(value) && value >= 5 && value <= 100 ||
    field === 'align' && ['left', 'center', 'right'].includes(value);
export const validImageLayout = value => value !== null && typeof value === 'object' && !Array.isArray(value) &&
    Object.keys(value).length === 2 && ['widthPercent', 'align'].every(field => Object.hasOwn(value, field) && value[field] !== null && validImageValue(field, value[field]));
export function applyBodyImage(image, attrs) {
    if (attrs.assetScope === 'document' || attrs.widthPercent != null || attrs.align != null) image.style.display = 'block';
    if (attrs.widthPercent != null || attrs.assetScope === 'document') Object.assign(image.style, { width: `${attrs.widthPercent ?? 50}%`, maxWidth: '100%', maxHeight: 'none', height: 'auto' });
    if (['left', 'center', 'right'].includes(attrs.align)) Object.assign(image.style, { marginLeft: attrs.align === 'left' ? '0' : 'auto', marginRight: attrs.align === 'right' ? '0' : 'auto' });
}
