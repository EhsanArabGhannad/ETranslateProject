import { guid } from './document-schema.js';

export function assetUrl(workspace, assetId) {
    const { tenantId, templateId } = workspace.dataset;
    if (![assetId, tenantId, templateId].every(value => typeof value === 'string' && guid.test(value))) return null;
    return `/Workspace/Asset?tenantId=${tenantId}&templateId=${templateId}&assetId=${assetId}`;
}
export function createPreview(workspace) {
    const find = id => document.getElementById(id);
    let remaining, unsupported;
    function render(node, parent, depth = 0) {
        if (node == null || depth > 32 || --remaining < 0) { unsupported = true; return; }
        if (Array.isArray(node)) { for (const child of node) render(child, parent, depth + 1); return; }
        if (typeof node !== 'object') { unsupported = true; return; }
        if (node.type === 'text') {
            let leaf = document.createTextNode(typeof node.text === 'string' ? node.text : '');
            for (const mark of (Array.isArray(node.marks) ? node.marks : [])) {
                const tag = new Map([['bold', 'strong'], ['italic', 'em'], ['underline', 'u'], ['strike', 's']]).get(mark?.type);
                if (!tag) { unsupported = true; continue; }
                const wrapper = document.createElement(tag); wrapper.append(leaf); leaf = wrapper;
            }
            parent.append(leaf); return;
        }
        if (node.type === 'image') {
            const url = assetUrl(workspace, node.attrs?.assetId);
            if (!url) { unsupported = true; parent.append(document.createTextNode('[تصویر در دسترس نیست]')); return; }
            const image = document.createElement('img'); image.alt = 'تصویر قالب'; image.src = url;
            image.addEventListener('error', () => image.replaceWith(document.createTextNode('[تصویر در دسترس نیست]')));
            parent.append(image); return;
        }
        if (node.type === 'hardBreak') { parent.append(document.createElement('br')); return; }
        const tags = new Map([['paragraph', 'p'], ['heading', 'h3'], ['bulletList', 'ul'], ['orderedList', 'ol'], ['listItem', 'li'], ['blockquote', 'blockquote']]);
        let target = parent;
        if (tags.has(node.type)) {
            const tag = node.type === 'heading' && Number.isInteger(node.attrs?.level) && node.attrs.level >= 1 && node.attrs.level <= 6
                ? `h${node.attrs.level}` : tags.get(node.type);
            target = document.createElement(tag); parent.append(target);
        } else if (!['doc', 'header', 'footer'].includes(node.type)) unsupported = true;
        if (['auto', 'rtl', 'ltr'].includes(node.attrs?.dir)) target.dir = node.attrs.dir;
        if (node.type === 'orderedList') {
            if (Number.isInteger(node.attrs?.start) && node.attrs.start >= 1 && node.attrs.start <= 10000) target.start = node.attrs.start;
            if (['1', 'a', 'A', 'i', 'I'].includes(node.attrs?.type)) target.type = node.attrs.type;
        }
        if (Array.isArray(node.content)) for (const child of node.content) render(child, target, depth + 1);
    }
    function region(id, source) {
        const target = find(id); target.replaceChildren(); target.dir = 'auto';
        if (!source.trim()) return;
        remaining = 20000; render(JSON.parse(source), target);
    }
    return function update() {
        unsupported = false;
        try {
            region('preview-header', find('template-header').value);
            region('preview-body', find('document-json').value);
            region('preview-footer', find('template-footer').value);
            const watermark = find('template-watermark').value;
            find('preview-watermark').textContent = watermark ? String(JSON.parse(watermark).text ?? '') : '';
            find('preview-notice').textContent = unsupported ? 'بعضی عناصر در پیش‌نمایش پشتیبانی نمی‌شوند؛ ساختار اصلی حفظ شده است.' : '';
        } catch { find('preview-notice').textContent = 'ساختار JSON معتبر نیست؛ پیش‌نمایش کامل نمی‌شود.'; }
    };
}
