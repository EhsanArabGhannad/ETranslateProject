import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TableKit } from '@tiptap/extension-table';
import { isRichDocument } from '../../src/Web/ETranslate.Web/ClientApp/document-schema.js';
import { baselineDocument, oversizedDocument, wordLikeHtml, mergedTableHtml, baselineStyleGaps } from './samples.js';
import { paginateBlocks } from './paginate.js';

const find = id => document.getElementById(id);
let selected = 'baseline', result = null, loading = true, scheduled;
const editor = new Editor({ element: find('editor'), injectCSS: false, textDirection: 'auto',
    extensions: [StarterKit.configure({ code:false, codeBlock:false, link:false, horizontalRule:false, dropcursor:false, trailingNode:false }),
        TableKit.configure({ table: { resizable:false, View:null } })],
    content: baselineDocument(), editorProps: {
        attributes: { role:'textbox', 'aria-label':'Sentetik deney metni', spellcheck:'false' },
        // The baseline import test uses known HTML constants. This is not an unreviewed importer for real documents.
        handlePaste: () => { find('status').textContent = 'Bu laboratuvar yalnızca hazır sentetik içe aktarım örneklerini test eder.'; return true; },
        handleDrop: () => true
    }, onUpdate: () => { if (!loading) schedule(); } });

function measure() {
    const before = JSON.stringify(editor.getJSON());
    const pagination = paginateBlocks(editor.view.dom, find('pages'));
    const json = editor.getJSON(), compatible = isRichDocument(json);
    result = { sample:selected, productionCompatible:compatible, pages:pagination.pages, issue:pagination.issue,
        styleGaps:selected === 'word' ? baselineStyleGaps(json) : [], jsonUnchanged:before === JSON.stringify(json),
        previewTextPreserved:pagination.issue === null && pagination.previewText === editor.view.dom.textContent,
        wordImportScope:'Fixed synthetic HTML; not a live Microsoft Word clipboard or DOCX fidelity test.',
        paginationScope:'Read-only whole-block measurement. No editable pages, paragraph/table fragmentation, template reserves or PDF parity guarantee.' };
    find('status').textContent = `${compatible ? 'Mevcut kayıt yapısı ile uyumlu' : 'Mevcut üretim kayıt yapısı bu içeriği kabul etmez'} | ${pagination.pages == null ? 'Sayfalama sınırı saptandı' : `${pagination.pages} ölçüm sayfası`} | JSON: ${result.jsonUnchanged ? 'korundu' : 'DEĞİŞTİ'}`;
    find('gaps').textContent = result.styleGaps.length ? `Bu temel motorda korunmayan özellikler: ${result.styleGaps.join(', ')}.` : '';
    find('report').textContent = JSON.stringify(result, null, 2);
    find('page-nav').replaceChildren();
    for(let i = 1; i <= (pagination.pages ?? 0); i++) {
        const button = document.createElement('button'); button.type = 'button'; button.textContent = String(i);
        button.setAttribute('aria-label', `Örnek sayfa ${i}`);
        button.addEventListener('click', () => find(`page-${i}`).scrollIntoView({ block:'nearest' })); find('page-nav').append(button);
    }
    return result;
}
function schedule() { clearTimeout(scheduled); scheduled = setTimeout(measure, 120); }
async function loadSample(id) {
    if (!['baseline','word','merged','oversized'].includes(id)) throw new Error('Unknown synthetic fixture');
    selected = id; find('sample').value = id; loading = true;
    editor.commands.setContent(id === 'word' ? wordLikeHtml : id === 'merged' ? mergedTableHtml : id === 'oversized' ? oversizedDocument() : baselineDocument());
    loading = false; await document.fonts.ready; return measure();
}
find('load').addEventListener('click', () => loadSample(find('sample').value));
find('reflow').addEventListener('click', measure);
find('zoom').addEventListener('change', () => { find('pages').style.zoom = find('zoom').value; });
// The test hook exposes only this disposable in-memory experiment, never production state or credentials.
window.editorStep1 = { loadSample, report:() => structuredClone(result), json:() => editor.getJSON(), reflow:measure };
document.fonts.ready.then(() => { loading = false; measure(); });
