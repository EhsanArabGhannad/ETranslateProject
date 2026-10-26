import { sample, flatText } from '../tiptap-pagination-free/samples.js';
export { flatText };
export const ids = ['text', 'letterhead-last', 'landscape', 'simple-table', 'long-table', 'long-table-multiline', 'table-no-header', 'table-mixed-header',
    'table-varied', 'table-after-text', 'table-after-text-header', 'table-multiple', 'table-landscape', 'table-letterhead-last', 'long-paragraph', 'tall-cell'];
const base = { pageSize: 'A4', orientation: 'Portrait', marginsMm: { top: 25, right: 20, bottom: 20, left: 20 } };
export function fixture(id) {
    if (!ids.includes(id)) throw new Error('Unknown synthetic fixture');
    const letterhead = ['letterhead-last', 'table-letterhead-last'].includes(id);
    const layout = ['landscape', 'table-landscape'].includes(id) ? { pageSize: 'A5', orientation: 'Landscape', marginsMm: { top: 10, right: 15, bottom: 20, left: 25 } }
        : { ...base, ...(letterhead ? { footerPlacement: 'LastPage' } : {}) };
    const body = sample(['landscape', 'letterhead-last'].includes(id) ? 'text'
        : id.startsWith('table-') || id === 'long-table-multiline' ? 'long-table' : id);
    if (id === 'long-table-multiline') {
        const header = body.content.find(node => node.type === 'table').content[0];
        for (const [i, cell] of header.content.entries()) cell.content = [{ type: 'paragraph', attrs: { dir: i ? 'rtl' : 'ltr' }, content: [
            { type: 'text', text: i ? 'عنوان آزمایشی جدول' : 'MULTILINE_HEADER' }, { type: 'hardBreak' },
            { type: 'text', text: i ? 'سطر دوم عنوان' : 'Second heading line' }, { type: 'hardBreak' },
            { type: 'text', text: i ? 'سطر سوم' : 'Third line' }
        ] }];
    }
    if (['table-no-header', 'table-varied', 'table-after-text', 'table-landscape', 'table-letterhead-last'].includes(id))
        for (const cell of body.content.find(node => node.type === 'table').content[0].content) cell.type = 'tableCell';
    if (id === 'table-mixed-header') body.content.find(node => node.type === 'table').content[0].content[1].type = 'tableCell';
    if (id === 'table-varied') {
        const rows = body.content.find(node => node.type === 'table').content;
        for (let i = 1; i < rows.length; i++) {
            const p = rows[i].content[1].content[0]; p.attrs.dir = i % 2 ? 'rtl' : 'ltr';
            p.content[0].text = (i % 2 ? 'جدول با ارتفاع متغیر و جهت فارسی. ' : 'İstanbul değişken satır deneyi. ').repeat(1 + i % 4);
        }
    }
    if (['table-after-text', 'table-after-text-header'].includes(id)) {
        const intro = sample('text').content.slice(1, 13);
        for (let i = 0; i < intro.length; i++) intro[i].content[0].text = intro[i].content[0].text.replace(/MARK_\d{3}/, `MARK_${101 + i}`);
        body.content.splice(1, 0, ...intro);
    }
    if (id === 'table-multiple') {
        const table = body.content[1], second = structuredClone(table);
        second.content = [second.content[0], ...second.content.slice(21)];
        for (const cell of second.content[0].content) cell.type = 'tableCell';
        table.content = table.content.slice(0, 21);
        table.content[0].content[0].content[0].content[0].text = 'PRIMARY_TABLE_HEADER';
        second.content[0].content[0].content[0].content[0].text = 'SECOND_TABLE_LABEL';
        body.content.splice(2, 0, { type: 'paragraph', attrs: { dir: 'ltr' }, content: [{ type: 'text', text: 'MARK_099 BETWEEN TABLES' }] }, second);
    }
    return { id, body, layout,
        header: letterhead ? ['SENTETİK TERCÜME BÜROSU', 'آزمایش سربرگ - بدون اطلاعات واقعی'] : [],
        footer: letterhead ? ['Yalnızca son sayfa - SENTETİK ALTBİLGİ'] : [] };
}
