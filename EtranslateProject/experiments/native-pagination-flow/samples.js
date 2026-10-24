import { sample, flatText } from '../tiptap-pagination-free/samples.js';
export { flatText };
export const ids = ['text', 'letterhead-last', 'landscape', 'simple-table', 'long-table', 'long-paragraph', 'tall-cell'];
const base = { pageSize: 'A4', orientation: 'Portrait', marginsMm: { top: 25, right: 20, bottom: 20, left: 20 } };
export function fixture(id) {
    if (!ids.includes(id)) throw new Error('Unknown synthetic fixture');
    const layout = id === 'landscape' ? { pageSize: 'A5', orientation: 'Landscape', marginsMm: { top: 10, right: 15, bottom: 20, left: 25 } }
        : { ...base, ...(id === 'letterhead-last' ? { footerPlacement: 'LastPage' } : {}) };
    return { id, body: sample(['landscape', 'letterhead-last'].includes(id) ? 'text' : id), layout,
        header: id === 'letterhead-last' ? ['SENTETİK TERCÜME BÜROSU', 'آزمایش سربرگ - بدون اطلاعات واقعی'] : [],
        footer: id === 'letterhead-last' ? ['Yalnızca son sayfa - SENTETİK ALTBİLGİ'] : [] };
}
