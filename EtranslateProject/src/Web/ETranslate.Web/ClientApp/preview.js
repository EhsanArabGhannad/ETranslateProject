import ui from '../Resources/interface.tr.json' with { type: 'json' };
import { guid, isRichDocument } from './document-schema.js';
import { footerPolicy, showFooter } from './footer-policy.js';
import { supportsStoredContent, storedDocumentFormat } from './editors/document-contract.js';
import { applyTypography } from './typography.js';
import { applyParagraph } from './paragraph.js';
import { scriptMarks, applyInlineScript, orderedInlineMarks } from './inline-script.js';
import { appendColumnGroup } from './table-widths.js';
import { applyCellStyle } from './cell-style.js';
import { applyBodyImage } from './body-image.js';

export function assetUrl(workspace, assetId, scope = null) {
    const { tenantId, templateId, jobId } = workspace.dataset;
    if (scope === 'document') return [assetId, tenantId, jobId].every(value => typeof value === 'string' && guid.test(value))
        ? `/Workspace/BodyImage?tenantId=${tenantId}&jobId=${jobId}&assetId=${assetId}` : null;
    if (![assetId, tenantId, templateId].every(value => typeof value === 'string' && guid.test(value))) return null;
    return `/Workspace/Asset?tenantId=${tenantId}&templateId=${templateId}&assetId=${assetId}`;
}
export function createPreview(workspace, resolveImage = (id, scope) => assetUrl(workspace, id, scope)) {
    const find = id => document.getElementById(id);
    const imagePool = new Map(), imageUses = new Map();
    let remaining, unsupported, contentVersion;
    function render(node, parent, depth = 0) {
        if (node == null || depth > 32 || --remaining < 0) { unsupported = true; return; }
        if (Array.isArray(node)) { for (const child of node) render(child, parent, depth + 1); return; }
        if (typeof node !== 'object') { unsupported = true; return; }
        if (node.type === 'text') {
            let leaf = document.createTextNode(typeof node.text === 'string' ? node.text : '');
            const marks=Array.isArray(node.marks)?node.marks:[];
            for (const mark of contentVersion>=4?orderedInlineMarks(marks):marks) {
                if(scriptMarks.includes(mark?.type)){
                    if(contentVersion<4){unsupported=true;continue;}
                    const wrapper=document.createElement(mark.type==='superscript'?'sup':'sub');applyInlineScript(wrapper,mark.type);
                    wrapper.append(leaf);leaf=wrapper;continue;
                }
                if(mark?.type==='typography'){
                    if(contentVersion<2){unsupported=true;continue;}
                    const wrapper=document.createElement('span');if(!applyTypography(wrapper,mark.attrs)){unsupported=true;continue;}
                    wrapper.append(leaf);leaf=wrapper;continue;
                }
                const tag = new Map([['bold', 'strong'], ['italic', 'em'], ['underline', 'u'], ['strike', 's']]).get(mark?.type);
                if (!tag) { unsupported = true; continue; }
                const wrapper = document.createElement(tag); wrapper.append(leaf); leaf = wrapper;
            }
            parent.append(leaf); return;
        }
        if (node.type === 'image') {
            const url = resolveImage(node.attrs?.assetId, node.attrs?.assetScope);
            if (!url) { unsupported = true; parent.append(document.createTextNode(ui.GorselKullanilamiyor)); return; }
            const index = imageUses.get(url) ?? 0; imageUses.set(url, index + 1);
            let images = imagePool.get(url); if (!images) { images = []; imagePool.set(url, images); }
            let image = images[index];
            if (!image) {
                image = document.createElement('img'); image.src = url; images.push(image);
                image.addEventListener('error', () => { image.dataset.previewFailed = 'true'; image.replaceWith(document.createTextNode(ui.GorselKullanilamiyor)); });
            }
            if (image.dataset.previewFailed) { unsupported = true; parent.append(document.createTextNode(ui.GorselKullanilamiyor)); return; }
            // Keep the decoded private element across text edits; no-store images must not
            // be downloaded again for every keystroke. Each occurrence owns one element.
            image.className = ''; while (image.style.length) image.style.removeProperty(image.style.item(0));
            image.alt = node.attrs?.assetScope === 'document' ? ui.EditorBodyImage : ui.SablonGorseli;
            if (node.attrs?.layout === 'banner') image.classList.add('template-banner');
            if (['left', 'center', 'right'].includes(node.attrs?.align)) image.classList.add(`image-${node.attrs.align}`);
            if (contentVersion >= 9) applyBodyImage(image, node.attrs);
            parent.append(image); return;
        }
        if (node.type === 'hardBreak') { parent.append(document.createElement('br')); return; }
        const tags = new Map([['paragraph', 'p'], ['heading', 'h3'], ['bulletList', 'ul'], ['orderedList', 'ol'], ['listItem', 'li'], ['blockquote', 'blockquote'], ['table', 'table'], ['tableRow', 'tr'], ['tableCell', 'td'], ['tableHeader', 'th']]);
        let target = parent;
        if (tags.has(node.type)) {
            const tag = node.type === 'heading' && Number.isInteger(node.attrs?.level) && node.attrs.level >= 1 && node.attrs.level <= 6
                ? `h${node.attrs.level}` : tags.get(node.type);
            target = document.createElement(tag); parent.append(target);
        } else if (!['doc', 'header', 'footer'].includes(node.type)) unsupported = true;
        if (['auto', 'rtl', 'ltr'].includes(node.attrs?.dir)) target.dir = node.attrs.dir;
        if (contentVersion >= 7 && ['tableCell', 'tableHeader'].includes(node.type)) applyCellStyle(target, node.attrs);
        if (contentVersion >= 8 && ['tableCell', 'tableHeader'].includes(node.type)) target.colSpan = node.attrs?.colspan ?? 1;
        if (node.type === 'table' && contentVersion >= 6) { appendColumnGroup(target, node.attrs?.columnWidths);
            if (Array.isArray(node.attrs?.columnWidths)) target.dataset.etColumnWidths = JSON.stringify(node.attrs.columnWidths); }
        if (['paragraph', 'heading'].includes(node.type) && ['left', 'center', 'right'].includes(node.attrs?.textAlign)) target.style.textAlign = node.attrs.textAlign;
        if(contentVersion>=3&&['paragraph','heading'].includes(node.type))applyParagraph(target,node.attrs);
        if (node.type === 'orderedList') {
            if (Number.isInteger(node.attrs?.start) && node.attrs.start >= 1 && node.attrs.start <= 10000) target.start = node.attrs.start;
            if (['1', 'a', 'A', 'i', 'I'].includes(node.attrs?.type)) target.type = node.attrs.type;
        }
        if (Array.isArray(node.content)) for (const child of node.content) render(child, target, depth + 1);
    }
    function region(id, source) {
        const target = find(id); target.replaceChildren(); target.dir = 'auto';
        if (!source.trim()) return;
        const body = id === 'preview-body';
        const format=body?(workspace.dataset.workingContentFormat??workspace.dataset.contentFormat):workspace.dataset.templateContentFormat;
        const version=body?(workspace.dataset.workingContentSchemaVersion??workspace.dataset.contentSchemaVersion):workspace.dataset.templateContentSchemaVersion;
        if (!supportsStoredContent(format,version)) {
            unsupported = true; target.append(document.createTextNode(ui.BelgeIcerikSurumuDesteklenmiyor)); return;
        }
        const root = JSON.parse(source);
        contentVersion=storedDocumentFormat(format,version).version;
        if (id === 'preview-body' && !isRichDocument(root,contentVersion)) {
            unsupported = true;
            target.append(document.createTextNode(ui.GovdeYapisiOnizlemeIcinDesteklenmiyorOzgunJSON));
            return;
        }
        remaining = 20000; render(root, target);
    }
    function update() {
        unsupported = false; imageUses.clear();
        try {
            region('preview-header', find('template-header').value);
            region('preview-body', find('document-json').value);
            region('preview-footer', find('template-footer').value);
            const watermark = find('template-watermark').value;
            find('preview-watermark').textContent = watermark ? String(JSON.parse(watermark).text ?? '') : '';
            find('preview-notice').textContent = unsupported ? ui.BaziOgelerOnizlemedeDesteklenmiyorOzgunYapiKorundu : '';
            const policy = footerPolicy(find('template-layout')?.value);
            find('preview-footer').hidden = !showFooter(policy, find('preview-page-role')?.value);
            if (policy === 'LastPage') find('preview-notice').textContent += ui.AltbilgiYalnizcaCevirininSonSayfasindadirBuGorunum;
            if (policy === null) find('preview-notice').textContent += ui.AltbilgiAyarlariBilinmiyorKonumunuBuOnizlemedenCikarmayin;
        } catch { find('preview-notice').textContent = ui.JSONYapisiGecersizOnizlemeTamamlanamiyor; }
        finally { for (const [url, images] of imagePool) { const used = imageUses.get(url) ?? 0; if (!used) imagePool.delete(url); else images.length = used; } }
    }
    find('preview-page-role')?.addEventListener('change', update);
    return update;
}
