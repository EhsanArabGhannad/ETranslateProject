export const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const blocks = ['paragraph', 'heading', 'bulletList', 'orderedList', 'image', 'blockquote'];
const marks = ['bold', 'italic', 'underline', 'strike'];

export function isRichDocument(root) {
    let remaining = 20000;
    function visit(node, parent, depth) {
        if (depth > 32 || --remaining < 0 || !object(node) || typeof node.type !== 'string') return false;
        const type = node.type;
        if (![...blocks, 'doc', 'text', 'hardBreak', 'listItem'].includes(type)) return false;
        if (parent === null ? type !== 'doc' : ['paragraph', 'heading'].includes(parent) ? !['text', 'hardBreak'].includes(type)
            : ['bulletList', 'orderedList'].includes(parent) ? type !== 'listItem'
            : ['doc', 'listItem', 'blockquote'].includes(parent) ? !blocks.includes(type) : true) return false;
        for (const [key, value] of Object.entries(node)) {
            if (key === 'type') continue;
            if (key === 'text' && type === 'text' && typeof value === 'string') continue;
            if (key === 'marks' && type === 'text' && Array.isArray(value) && new Set(value.map(mark => mark?.type)).size === value.length &&
                value.every(mark => object(mark) && Object.keys(mark).length === 1 && marks.includes(mark.type))) continue;
            if (key === 'attrs' && type !== 'text' && object(value) && Object.entries(value).every(([attr, val]) =>
                attr === 'dir' && ['auto', 'rtl', 'ltr'].includes(val) ||
                type === 'heading' && attr === 'level' && Number.isInteger(val) && val >= 1 && val <= 6 ||
                type === 'orderedList' && attr === 'start' && Number.isInteger(val) && val >= 1 && val <= 10000 ||
                type === 'orderedList' && attr === 'type' && [null, '1', 'a', 'A', 'i', 'I'].includes(val) ||
                type === 'image' && attr === 'assetId' && typeof val === 'string' && guid.test(val))) continue;
            if (key === 'content' && !['text', 'image', 'hardBreak'].includes(type) && Array.isArray(value)) continue;
            return false;
        }
        if (type === 'text') return typeof node.text === 'string' && (node.text.length > 0 || !node.marks?.length);
        if (type === 'image') return typeof node.attrs?.assetId === 'string' && guid.test(node.attrs.assetId);
        if (type === 'hardBreak') return true;
        if (type === 'heading' && !Number.isInteger(node.attrs?.level)) return false;
        if (!Object.hasOwn(node, 'content')) return type === 'paragraph' || type === 'heading';
        if (['bulletList', 'orderedList', 'listItem', 'blockquote'].includes(type) && !node.content.length) return false;
        if (type === 'listItem' && node.content[0]?.type !== 'paragraph') return false;
        return node.content.every(child => visit(child, type, depth + 1));
    }
    return visit(root, null, 0);
}

// Legacy plain-text drafts used empty text nodes. ProseMirror requires empty paragraphs instead.
export function normalizeEmptyText(node) {
    if (!node || typeof node !== 'object') return node;
    const result = { ...node };
    if (Array.isArray(node.content)) result.content = node.content
        .filter(child => !(child.type === 'text' && child.text === '' && !child.marks?.length)).map(normalizeEmptyText);
    return result;
}
