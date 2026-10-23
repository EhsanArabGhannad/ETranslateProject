export const text = value => ({ type: 'text', text: value });
export const paragraph = (value, dir = 'auto') => ({ type: 'paragraph', attrs: { dir }, content: [text(value)] });
const cell = (value, header = false) => ({ type: header ? 'tableHeader' : 'tableCell', content: [paragraph(value)] });

export function baselineDocument() {
    return { type: 'doc', content: [
        { type: 'heading', attrs: { level: 2, dir: 'ltr' }, content: [text('SENTETİK ÇEVİRİ / آزمایش ترجمه')] },
        paragraph('Bu belge yalnızca yerel araştırma içindir. Gerçek kişi veya resmi belge içermez.', 'ltr'),
        { type: 'table', content: [
            { type: 'tableRow', content: [cell('Alan / عنوان', true), cell('Değer / مقدار', true)] },
            { type: 'tableRow', content: [cell('Belge türü'), cell('Sentetik örnek')] }
        ] },
        ...Array.from({ length: 32 }, (_, i) => paragraph(i % 2
            ? `بند ${i + 1}: این متن ساختگی برای بررسی حفظ نویسه‌های فارسی، جهت نوشتن و جابه‌جایی صفحه‌هاست. هیچ دادهٔ واقعی در این نمونه نیست.`
            : `Paragraf ${i + 1}: Türkçe çeviri örneği; İstanbul, ı, İ, ş, ğ, ö, ü. Sayfalama deneyi metni değiştirmemelidir.`, i % 2 ? 'rtl' : 'ltr'))
    ] };
}
// Deliberately fixed HTML, not a real DOCX or a claim about the complete Word clipboard format.
export const wordLikeHtml = `<h2 style="font-family:'Times New Roman';font-size:18pt;color:#b00020;text-align:center">WORD_STYLE_SAMPLE</h2>
<p dir="rtl" style="font-family:Arial;font-size:16pt;background-color:#ffff00;line-height:2">متن فارسی ساختگی <strong>پررنگ</strong> و <em>مورب</em> - Türkçe örnek</p>
<p style="text-align:right">Right aligned in the source</p>`;
export const mergedTableHtml = '<table><tbody><tr><td colspan="2"><p>MERGED_CELL_SAMPLE</p></td></tr><tr><td><p>A</p></td><td><p>B</p></td></tr></tbody></table>';
export function oversizedDocument() { return { type: 'doc', content: [paragraph('OVERSIZED_BLOCK ' + 'Uzun paragraf / متن بلند. '.repeat(700))] }; }

export function baselineStyleGaps(json) {
    const attrs = [], types = [];
    function visit(node) {
        types.push(node.type); if (node.attrs) attrs.push(node.attrs);
        for (const mark of node.marks ?? []) { types.push(mark.type); if (mark.attrs) attrs.push(mark.attrs); }
        for (const child of node.content ?? []) visit(child);
    }
    visit(json);
    return [['fontFamily', 'Yazı tipi'], ['fontSize', 'Yazı boyutu'], ['color', 'Yazı rengi'],
        ['textAlign', 'Paragraf hizalaması'], ['lineHeight', 'Satır aralığı']]
        .filter(([key]) => !attrs.some(a => a[key] != null)).map(([, label]) => label)
        .concat(types.includes('highlight') ? [] : ['Vurgu rengi']);
}
