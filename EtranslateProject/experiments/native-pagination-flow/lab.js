import { createEditorSession } from '../../src/Web/ETranslate.Web/ClientApp/editors/editor-session.js';
import { defaultEditorProvider } from '../../src/Web/ETranslate.Web/ClientApp/editors/default-provider.js';
import { paperScale, mmToPixels } from '../../src/Web/ETranslate.Web/ClientApp/editor-view-layout.js';
import { fixture, flatText } from './samples.js';
import { geometry, pageCount, pageAt } from './geometry.js';

const find = id => document.getElementById(id);
let session, current, metrics, originalText, initialJson, changes = 0, active = 1, pages = 1, scale = 1, pending = 0, issue = null, ready = false;
let layoutEpoch = 0, lastLayout = 0, maximumObservedPages = 1;
const viewport = find('viewport'), world = find('world'), host = find('editor'), measure = find('measure');
const dom = () => host.querySelector('[role=textbox]');
function region(lines, type) {
    const node = document.createElement('div'); node.className = `doc-format ${type}`;
    for (const text of lines) { const p = document.createElement('p'); p.dir = 'auto'; p.textContent = text; node.append(p); }
    return node;
}
function headerMetrics() {
    const width = geometry(JSON.stringify(current.layout)).contentWidth;
    const regions = [region(current.header, 'letterhead'), region(current.footer, 'letterfoot')];
    for (const node of regions) { node.style.width = `${width}px`; measure.append(node); }
    const values = regions.map((node, i) => (i === 0 ? current.header : current.footer).length ? node.getBoundingClientRect().height * 25.4 / 96 : 0);
    measure.replaceChildren(); return values;
}
function measureRows() {
    const clone = dom().cloneNode(true); clone.removeAttribute('contenteditable'); clone.removeAttribute('role'); clone.removeAttribute('tabindex');
    clone.style.width = `${metrics.contentWidth}px`; measure.replaceChildren(clone);
    let maximum = 0;
    for (const table of clone.querySelectorAll('table')) {
        const rows = [...table.querySelectorAll('tr')];
        const header = rows[0] && [...rows[0].children].every(cell => cell.tagName === 'TH') ? rows.shift().getBoundingClientRect().height : 0;
        maximum = Math.max(maximum, header, ...rows.map(row => header + row.getBoundingClientRect().height));
    }
    measure.replaceChildren(); return maximum;
}
function markerPages() {
    const result = {}, root = dom(), origin = root.getBoundingClientRect().left;
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        for (const match of node.data.matchAll(/MARK_\d{3}|LONG_PARA_START|LONG_PARA_END|TABLE_END_MARKER|TALL_CELL_START|TALL_CELL_END/g)) {
            const range = document.createRange(); range.setStart(node, match.index); range.setEnd(node, match.index + match[0].length);
            const rect = [...range.getClientRects()].find(rect => rect.width > 0);
            if (rect) result[match[0]] = issue ? null : pageAt((rect.left - origin) / scale, metrics);
        }
    }
    return result;
}
function selectionReport() {
    const selection = getSelection();
    let page = null;
    if (selection?.focusNode && dom().contains(selection.focusNode)) {
        const range = document.createRange(); range.setStart(selection.focusNode, selection.focusOffset); range.collapse(true);
        const rect = [...range.getClientRects()].find(rect => rect.height > 0);
        if (rect && !issue) page = pageAt((rect.left - dom().getBoundingClientRect().left) / scale, metrics);
    }
    const parent = selection?.focusNode?.nodeType === Node.ELEMENT_NODE ? selection.focusNode : selection?.focusNode?.parentElement;
    return { text: selection?.toString(), anchorOffset: selection?.anchorOffset, focusOffset: selection?.focusOffset,
        page, paragraphPrefix: parent?.closest('p,h2')?.textContent.slice(0, 100) ?? null };
}
function followCaret() {
    if (!ready || issue) return;
    const n = selectionReport().page;
    if (Number.isInteger(n) && n !== active && n <= pages) jump(n);
}
function report() {
    const json = JSON.parse(session.getDocumentJson());
    return { sample: current.id, pages: issue ? null : pages, activePage: issue ? null : active, layoutIssue: issue, maximumObservedPages,
        geometry: metrics, zoom: scale, changes, layoutEpoch, pending: !!pending, millisecondsSinceLayout: performance.now() - lastLayout,
        sourceTextPreserved: flatText(json) === originalText, jsonUnchanged: session.getDocumentJson() === initialJson,
        format: session.format, markers: markerPages(), selection: selectionReport(), readonly: !session.getCommandState('bold').enabled,
        tableHeaderGroups: dom().querySelectorAll('thead').length,
        bodyDomIsSingle: host.querySelectorAll('[role=textbox]').length === 1,
        productionApproved: false, realWordClipboardTested: false, realDocxTested: false };
}
function refreshNavigation() {
    const select = find('page');
    const count = issue ? 0 : pages;
    if (select.options.length !== count) {
        select.replaceChildren();
        for (let n = 1; n <= count; n++) { const option = document.createElement('option'); option.value = String(n); option.textContent = String(n); select.append(option); }
    }
    select.value = String(active); select.disabled = !!issue;
    find('previous').disabled = !!issue || active <= 1; find('next').disabled = !!issue || active >= pages;
    find('status').textContent = issue ? 'Sayfalama durduruldu; metin korundu.' : `Sayfa ${active} / ${pages} · %${Math.round(scale * 100)}`;
}
function jump(n) {
    if (issue || !Number.isInteger(n) || n < 1 || n > pages) return false;
    active = n; viewport.scrollLeft = (n - 1) * metrics.pitch * scale; viewport.scrollTop = 0; refreshNavigation(); return true;
}
function sheetNodes(count, height) {
    find('sheets').replaceChildren();
    for (let i = 0; i < count; i++) {
        const sheet = document.createElement('article'); sheet.className = 'sheet';
        Object.assign(sheet.style, { left: `${i * metrics.pitch}px`, width: `${metrics.pageWidth}px`, height: `${height}px` });
        const mark = document.createElement('div'); mark.className = 'draft-mark'; mark.textContent = 'DRAFT - UNSIGNED'; sheet.append(mark);
        if (current.header.length) {
            const header = region(current.header, 'letterhead');
            Object.assign(header.style, { left: `${metrics.left}px`, top: `${mmToPixels(metrics.margins.top)}px`, width: `${metrics.contentWidth}px` }); sheet.append(header);
        }
        if (current.footer.length && (current.layout.footerPlacement !== 'LastPage' || i === count - 1)) {
            const footer = region(current.footer, 'letterfoot');
            Object.assign(footer.style, { left: `${metrics.left}px`, bottom: `${mmToPixels(metrics.margins.bottom + 8)}px`, width: `${metrics.contentWidth}px` }); sheet.append(footer);
        }
        const label = document.createElement('div'); label.className = 'sheet-number'; label.textContent = issue ? 'SAYFALAMA KAPALI' : `DRAFT - UNSIGNED | Sayfa ${i + 1} / ${count}`;
        sheet.append(label); find('sheets').append(sheet);
    }
}
function layout() {
    pending = 0;
    if (!session) return;
    issue = measureRows() > metrics.contentHeight + 0.5 ? 'tall-row-exceeds-printable-page' : null;
    world.dataset.mode = issue ? 'continuous' : 'columns';
    const measured = issue ? 1 : pageCount(dom().scrollWidth, metrics);
    if (measured === null) { issue = 'more-than-50-pages'; world.dataset.mode = 'continuous'; }
    pages = measured ?? 1; maximumObservedPages = Math.max(maximumObservedPages, pages);
    active = Math.min(active, pages);
    scale = paperScale(find('zoom').value, viewport.clientWidth - 32, metrics.pageWidth);
    const height = issue ? dom().getBoundingClientRect().height / (Number(world.dataset.scale) || 1) + metrics.top + mmToPixels(metrics.margins.bottom + 8) : metrics.pageHeight;
    const width = pages * metrics.pageWidth + (pages - 1) * 28;
    Object.assign(world.style, { width: `${width}px`, height: `${height}px`, transform: `scale(${scale})` }); world.dataset.scale = String(scale);
    Object.assign(find('stage').style, { width: `${width * scale}px`, height: `${height * scale}px` });
    sheetNodes(pages, height); refreshNavigation();
    find('issues').hidden = !issue && !dom().querySelector('table');
    find('issues').textContent = issue ? 'Bu içerik yazdırılabilir sayfaya sığmıyor. Sayfalama kapatıldı; metin silinmedi ve örnek başarısız olarak işaretlendi.'
        : dom().querySelector('table') ? 'Tablo üstbilgisi tekrarları ve PDF sınırları ayrıca doğrulanmalıdır. Bu örnek üretim için onaylanmadı.' : '';
    for (const button of document.querySelectorAll('[data-command]')) button.disabled = !session.getCommandState(button.dataset.command).enabled;
    layoutEpoch++; lastLayout = performance.now(); find('report').textContent = JSON.stringify(report(), null, 2);
}
function schedule() { if (!pending) pending = requestAnimationFrame(layout); }
async function load(id) {
    ready = false; session?.destroy(); if (pending) cancelAnimationFrame(pending); pending = 0; host.replaceChildren();
    current = fixture(id); const [header, footer] = headerMetrics(); metrics = geometry(JSON.stringify(current.layout), header, footer);
    if (!metrics) throw new Error('Unsupported page geometry');
    changes = 0; active = 1; maximumObservedPages = 1; issue = null;
    for (const [name, value] of Object.entries({ 'content-width': metrics.contentWidth, 'content-height': metrics.contentHeight, 'column-gap': metrics.columnGap })) world.style.setProperty(`--${name}`, `${value}px`);
    Object.assign(host.style, { top: `${metrics.top}px`, left: `${metrics.left}px` });
    session = createEditorSession({ provider: defaultEditorProvider, element: host, contentJson: JSON.stringify(current.body), editable: true,
        labels: { text: 'Sentetik sayfalı çeviri', image: 'Sentetik', missingImage: 'Görsel yok' } });
    dom().classList.add('doc-format'); initialJson = session.getDocumentJson(); originalText = flatText(JSON.parse(initialJson));
    session.onDocumentChange(() => { changes++; schedule(); }); session.onStateChange(schedule);
    find('sample').value = id; find('readonly').setAttribute('aria-pressed', 'false');
    layout(); viewport.scrollTop = 0; viewport.scrollLeft = 0; ready = true; return report();
}
async function start() {
    await Promise.all(['11pt "Noto Sans"', 'bold 11pt "Noto Sans"', 'italic 11pt "Noto Sans"', '11pt "Noto Sans Arabic"'].map(font => document.fonts.load(font)));
    await load('text');
    find('load').addEventListener('click', () => load(find('sample').value));
    find('zoom').addEventListener('change', schedule);
    find('page').addEventListener('change', () => jump(Number(find('page').value)));
    for (const [id, step] of [['previous', -1], ['next', 1]]) {
        find(id).addEventListener('mousedown', event => event.preventDefault()); find(id).addEventListener('click', () => jump(active + step));
    }
    find('readonly').addEventListener('click', () => { const readonly = find('readonly').getAttribute('aria-pressed') !== 'true'; find('readonly').setAttribute('aria-pressed', String(readonly)); session.setEditable(!readonly); schedule(); });
    for (const button of document.querySelectorAll('[data-command]')) {
        button.addEventListener('mousedown', event => event.preventDefault()); button.addEventListener('click', () => session.execute(button.dataset.command));
    }
    for (const event of ['paste', 'drop']) host.addEventListener(event, e => e.preventDefault());
    document.addEventListener('selectionchange', followCaret);
    // Native/editor key handling can finish after the selection notification. Synchronize the
    // view again on navigation keyup; never move the selection or issue a content transaction.
    host.addEventListener('keyup', event => {
        if (!event.isComposing && ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageUp', 'PageDown'].includes(event.key)) followCaret();
    });
    new ResizeObserver(schedule).observe(viewport);
    window.nativeFlowLab = { load, report, ready: () => ready && !pending, json: () => session.getDocumentJson(), fixture: () => current,
        selection: selectionReport,
        undo: () => session.execute('undo'), redo: () => session.execute('redo'), jump };
}
start().catch(error => { find('issues').hidden = false; find('issues').textContent = `Deney açılamadı: ${error.message}`; console.error(error); });
