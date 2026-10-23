import { baselineDocument, paragraph, text, wordLikeHtml } from '../editor-engine-step1/samples.js';
export { wordLikeHtml };
export function flatText(node) { return node?.type === 'text' ? node.text : (node?.content ?? []).map(flatText).join(''); }
function marker(i) { return `MARK_${String(i).padStart(3, '0')}`; }
const cell = (value, header = false) => ({ type: header ? 'tableHeader' : 'tableCell', content: [paragraph(value, 'ltr')] });
export function sample(id) {
    const heading = { type: 'heading', attrs: { level: 2, dir: 'ltr' }, content: [text('SENTETİK BELGE / آزمایش ترجمه')] };
    switch (id) {
        case 'text': return { type: 'doc', content: [heading, ...Array.from({ length: 48 }, (_, i) => paragraph(i % 2
            ? `${marker(i + 1)} این متن ساختگی برای بررسی جهت فارسی، حرکت مکان‌نما و انتقال نوشته بین صفحه‌هاست. اطلاعات شخصی ندارد.`
            : `${marker(i + 1)} Türkçe çeviri deneyi; İstanbul, ı, İ, ş, ğ, ö, ü. Sayfalama metni veya kayıt geçmişini değiştirmemelidir.`, i % 2 ? 'rtl' : 'ltr'))] };
        case 'simple-table': return baselineDocument();
        case 'long-table': return { type: 'doc', content: [heading, { type: 'table', content: [
            { type: 'tableRow', content: [cell('Sıra', true), cell('Sentetik açıklama', true)] },
            ...Array.from({ length: 46 }, (_, i) => ({ type: 'tableRow', content: [cell(marker(i + 1)), cell('İstanbul - جدول ساختگی / Türkçe. '.repeat(3))] }))
        ] }, paragraph('TABLE_END_MARKER')] };
        case 'merged': return { type: 'doc', content: [heading, { type: 'table', content: [
            { type: 'tableRow', content: [{ ...cell('ROWSPAN_SAMPLE'), attrs: { rowspan: 2, colspan: 1 } }, cell('A')] },
            { type: 'tableRow', content: [cell('B')] }
        ] }, paragraph('MERGED_END_MARKER')] };
        case 'tall-cell': return { type: 'doc', content: [heading, { type: 'table', content: [
            { type: 'tableRow', content: [cell('TALL_CELL_START ' + 'Uzun hücre / خانه بلند. '.repeat(260) + ' TALL_CELL_END')] }
        ] }, paragraph('AFTER_TALL_CELL')] };
        case 'long-paragraph': return { type: 'doc', content: [heading, paragraph('LONG_PARA_START ' + 'İstanbul متن بلند برای صفحه‌بندی. '.repeat(260) + ' LONG_PARA_END')] };
        case 'word': return wordLikeHtml;
        default: throw new Error('Unknown synthetic sample');
    }
}

