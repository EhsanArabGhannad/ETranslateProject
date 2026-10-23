import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { PaginationPlus } from 'tiptap-pagination-plus';
import { TableKitPlus } from 'tiptap-table-plus';
import { isRichDocument } from '../../src/Web/ETranslate.Web/ClientApp/document-schema.js';
import { baselineStyleGaps } from '../editor-engine-step1/samples.js';
import { sample, flatText } from './samples.js';
async function start() {
const find = id => document.getElementById(id);
let editor, selected = 'text', sourceText = '', revision = 0, changes = 0, lastChanged = 0, pending, snapshot;
let layoutIssue = null, maximumObservedPages = 0;
const mm = value => value * 96 / 25.4;
// Header/footer text is fixed here, never user HTML. Vendor writes it with innerHTML.
const pageOptions = { pageWidth:mm(210), pageHeight:mm(297), marginLeft:mm(20), marginRight:mm(20),
    marginTop:mm(25) - 18, marginBottom:mm(28) - 18, contentMarginTop:0, contentMarginBottom:0,
    pageGap:28, pageBreakBackground:'#dfe7e2', headerLeft:'YEREL DENEY - Resmi belge değildir', headerRight:'',
    footerLeft:'SENTETİK', footerRight:'Sayfa {page}', pageGapBorderColor:'#c2d3c9' };
function breaks() { return [...editor.view.dom.querySelectorAll('[data-rm-pagination] > .rm-page-break')]; }
function visiblePage(y) {
    const list = breaks();
    const i = list.findIndex(el => y < el.querySelector('.breaker').getBoundingClientRect().top);
    return i < 0 ? list.length : i + 1;
}
function markerPages() {
    const result = {};
    editor.state.doc.descendants((node, pos) => {
        if (!node.isText) return;
        for (const match of node.text.matchAll(/MARK_\d{3}/g)) {
            const at = editor.view.coordsAtPos(pos + match.index);
            result[match[0]] = visiblePage(at.top + (at.bottom - at.top) / 2);
        }
    });
    return result;
}
function report() {
    const json = editor.getJSON();
    const scrollCells = [...editor.view.dom.querySelectorAll('td,th,.table-row-group')].filter(el => el.scrollHeight > el.clientHeight + 2);
    const types = new Set(); const attrs = new Set();
    (function visit(n) { types.add(n.type); for (const k of Object.keys(n.attrs ?? {})) attrs.add(`${n.type}.${k}`); for (const c of n.content ?? []) visit(c); })(json);
    return { sample:selected, engine:'tiptap-pagination-plus@3.1.0', tableEngine:'tiptap-table-plus@3.1.0',
        productionCompatible:isRichDocument(json), pages:breaks().length, sourceTextPreserved:flatText(json) === sourceText,
        layoutIssue, maximumObservedPages, scrollClippedCells:scrollCells.length, types:[...types], attrs:[...attrs], markers:markerPages(),
        styleGaps:selected === 'word' ? baselineStyleGaps(json) : [], revision, changes,
        millisecondsSinceChange:performance.now() - lastChanged,
        scope:'Synthetic local-only editable pagination trial; no Word clipboard, DOCX, production template or saved-document integration.' };
}
function refresh() {
    if (!editor || editor.isDestroyed) return;
    const r = report();
    find('status').textContent = `${layoutIssue ? 'Sayfalama durduruldu' : `${r.pages} sayfa`} | ${r.productionCompatible ? 'Mevcut kayıt yapısıyla uyumlu' : 'Mevcut kayıt yapısıyla UYUMLU DEĞİL'} | ${r.changes} belge işlemi`;
    const warnings = [];
    if (layoutIssue) warnings.push('Kararsız sayfalama algılandı: deney koruması sayfalamayı durdurdu; metin silinmedi. Bu örnek başarısızdır.');
    if (r.scrollClippedCells) warnings.push('Uzun hücre/grup iç kaydırma kullanıyor: içerik tek sayfada tamamen görünmüyor. Üretim için kabul edilmez.');
    if (!r.productionCompatible) warnings.push('JSON yapısı değişmiş: bu örnek mevcut üretim API/PDF işlemine gönderilmemelidir.');
    if (r.styleGaps.length) warnings.push(`Korunmayan biçimler: ${r.styleGaps.join(', ')}.`);
    find('issues').hidden = warnings.length === 0; find('issues').textContent = warnings.join(' ');
    find('report').textContent = JSON.stringify(r, null, 2);
    find('page-nav').replaceChildren();
    breaks().forEach((el, i) => { const button = document.createElement('button'); button.type = 'button'; button.textContent = String(i + 1);
        button.setAttribute('aria-label', `Sayfa ${i + 1}`); button.addEventListener('click', () => jump(i + 1)); find('page-nav').append(button); });
}
function changed({ transaction }) { revision++; lastChanged = performance.now(); if (transaction.docChanged) changes++; clearTimeout(pending); pending = setTimeout(refresh, 120); }
function jump(n) {
    if (!Number.isInteger(n) || n < 1 || n > breaks().length) return false;
    const frame = find('paper-scroll'), dom = editor.view.dom, first = dom.getBoundingClientRect().top;
    const top = n === 1 ? first : breaks()[n - 2].querySelector('.rm-page-header').getBoundingClientRect().top;
    frame.scrollTop += top - frame.getBoundingClientRect().top - 20;
    return true;
}
function mount(value) {
    editor?.destroy(); document.querySelectorAll('style[data-rm-pagination-style]').forEach(el => el.remove());
    find('editor').replaceChildren(); revision = 0; changes = 0; layoutIssue = null; maximumObservedPages = 0;
    editor = new Editor({ element:find('editor'), injectCSS:false, textDirection:'auto', content:value,
        extensions:[StarterKit.configure({ code:false, codeBlock:false, link:false, horizontalRule:false, dropcursor:false, trailingNode:false }), TableKitPlus, PaginationPlus.configure(pageOptions)],
        editorProps:{ attributes:{ role:'textbox', 'aria-label':'Sentetik sayfalı belge', 'aria-multiline':'true', spellcheck:'false' },
            handlePaste:() => { find('status').textContent = 'Bu deneyde gerçek belge yapıştırmayın; yalnızca sabit örnekler kullanılır.'; return true; }, handleDrop:() => true },
        onTransaction:changed });
    lastChanged = performance.now();
}
async function settleLayout() {
    // Vendor onCreate applies CSS after mount. Trigger its measured layout only after those styles exist.
    await new Promise(requestAnimationFrame); await new Promise(requestAnimationFrame);
    editor.view.dispatch(editor.state.tr.setMeta('freeLabLayoutReady', true).setMeta('addToHistory', false));
}
async function load(id) { const value = sample(id); selected = id; find('sample').value = id;
    sourceText = typeof value === 'string' ? '' : flatText(value); mount(value);
    if (typeof value === 'string') sourceText = flatText(editor.getJSON());
    await settleLayout(); find('paper-scroll').scrollTop = 0; find('paper-scroll').scrollLeft = 0; refresh(); return report(); }
find('load').addEventListener('click', () => load(find('sample').value));
find('zoom').addEventListener('change', () => { find('editor').style.zoom = find('zoom').value; editor.view.dispatch(editor.state.tr.setMeta('labZoom', true)); refresh(); });
find('pagination').addEventListener('click', () => { editor.commands.togglePagination(); refresh(); });
find('reload').addEventListener('click', async () => { const value = editor.getJSON(); snapshot = JSON.stringify(value); mount(value); await settleLayout(); refresh(); });
for (const button of document.querySelectorAll('[data-command]')) {
    button.addEventListener('mousedown', e => e.preventDefault());
    button.addEventListener('click', () => { const chain = editor.chain().focus();
        ({ bold:() => chain.toggleBold(), italic:() => chain.toggleItalic(), rtl:() => chain.setTextDirection('rtl'), ltr:() => chain.setTextDirection('ltr'), undo:() => chain.undo(), redo:() => chain.redo() })[button.dataset.command]().run(); });
}
await Promise.all(['LabNoto','LabArabic'].map(name => document.fonts.load(`14px ${name}`)));
// Independent watchdog: do not debounce this on each vendor update, since a runaway layout
// would otherwise postpone the guard indefinitely. This is a lab safeguard, not an engine fix.
setInterval(() => {
    if (!editor || editor.isDestroyed) return;
    const count = breaks().length; maximumObservedPages = Math.max(maximumObservedPages, count);
    if (count > 50) {
        layoutIssue = 'page-count-runaway'; editor.commands.disablePagination(); refresh();
    }
}, 50);
await load('text');
// Test hooks are confined to the synthetic in-memory editor; no production API/state is reachable.
window.freePaginationLab = { load, report, json:() => editor.getJSON(), flat:() => flatText(editor.getJSON()), jump,
    append:(value) => editor.chain().focus().insertContentAt(editor.state.doc.content.size, value).run(),
    prepend:(value) => editor.chain().focus().insertContentAt(0, value).run(),
    undo:() => editor.commands.undo(), redo:() => editor.commands.redo(),
    roundTrip:async () => { const value = editor.getJSON(); snapshot = JSON.stringify(value); mount(value); await settleLayout(); return snapshot; },
    snapshotMatches:() => snapshot === JSON.stringify(editor.getJSON()),
    replaceAll:(value) => editor.commands.setContent(value),
    selection:() => ({ from:editor.state.selection.from, to:editor.state.selection.to }),
    caret:(pos) => editor.commands.setTextSelection(pos), enabled:(value) => value ? editor.commands.enablePagination() : editor.commands.disablePagination() };
}
start().catch(error => { document.getElementById('status').textContent = `Deney başlatılamadı: ${error.message}`; console.error(error); });
