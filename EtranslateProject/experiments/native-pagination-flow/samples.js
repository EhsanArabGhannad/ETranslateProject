import { sample, flatText } from '../tiptap-pagination-free/samples.js';
export { flatText };
export const ids = ['text', 'letterhead-last', 'landscape', 'simple-table', 'long-table', 'long-table-multiline', 'table-no-header', 'table-mixed-header', 'long-paragraph', 'tall-cell'];
const base = { pageSize: 'A4', orientation: 'Portrait', marginsMm: { top: 25, right: 20, bottom: 20, left: 20 } };
export function fixture(id) {
    if (!ids.includes(id)) throw new Error('Unknown synthetic fixture');
    const layout = id === 'landscape' ? { pageSize: 'A5', orientation: 'Landscape', marginsMm: { top: 10, right: 15, bottom: 20, left: 25 } }
        : { ...base, ...(id === 'letterhead-last' ? { footerPlacement: 'LastPage' } : {}) };
    const body = sample(['landscape', 'letterhead-last'].includes(id) ? 'text'
        : ['long-table-multiline', 'table-no-header', 'table-mixed-header'].includes(id) ? 'long-table' : id);
    if (id === 'long-table-multiline') {
        const header = body.content.find(node => node.type === 'table').content[0];
        for (const [i, cell] of header.content.entries()) cell.content = [{ type: 'paragraph', attrs: { dir: i ? 'rtl' : 'ltr' }, content: [
            { type: 'text', text: i ? 'عنوان آزمایشی جدول' : 'MULTILINE_HEADER' }, { type: 'hardBreak' },
            { type: 'text', text: i ? 'سطر دوم عنوان' : 'Second heading line' }, { type: 'hardBreak' },
            { type: 'text', text: i ? 'سطر سوم' : 'Third line' }
        ] }];
    }
    if (id === 'table-no-header') for (const cell of body.content.find(node => node.type === 'table').content[0].content) cell.type = 'tableCell';
    if (id === 'table-mixed-header') body.content.find(node => node.type === 'table').content[0].content[1].type = 'tableCell';
    return { id, body, layout,
        header: id === 'letterhead-last' ? ['SENTETİK TERCÜME BÜROSU', 'آزمایش سربرگ - بدون اطلاعات واقعی'] : [],
        footer: id === 'letterhead-last' ? ['Yalnızca son sayfa - SENTETİK ALTBİLGİ'] : [] };
}
