import { closeHistory } from '@tiptap/pm/history';
import { selectedTable } from './tiptap-table-context.js';
import { cellStyleFields, validCellStyleChange } from '../cell-style.js';

export function selectedCells(state, scope = 'selection') {
    const table = selectedTable(state); if (!table || !['selection', 'table'].includes(scope)) return [];
    const cells = new Map(), cell = node => ['tableCell', 'tableHeader'].includes(node.type.name);
    if (scope === 'table') table.node.descendants((node, offset) => { if (cell(node)) { cells.set(table.pos + 1 + offset, node); return false; } });
    else for (const range of state.selection.ranges) {
        const from = range.$from.pos, to = range.$to.pos;
        if (from === to) {
            for (let depth = range.$from.depth; depth > 0; depth--) if (cell(range.$from.node(depth))) { cells.set(range.$from.before(depth), range.$from.node(depth)); break; }
        } else state.doc.nodesBetween(from, to, (node, pos) => { if (cell(node) && pos < to && pos + node.nodeSize > from) { cells.set(pos, node); return false; } });
    }
    return [...cells].map(([pos, node]) => ({ pos, node }));
}
export function cellStyleState(state, scope) {
    const cells = selectedCells(state, scope), values = {}, mixed = {};
    for (const field of cellStyleFields) { const set = new Set(cells.map(({ node }) => node.attrs[field] ?? null)); values[field] = set.size === 1 ? [...set][0] : null; mixed[field] = set.size > 1; }
    return { count: cells.length, values, mixed, custom: cells.some(({ node }) => cellStyleFields.some(field => node.attrs[field] != null)) };
}
export function changeCellStyle(state, change) {
    if (!validCellStyleChange(change)) return null;
    const cells = selectedCells(state, change.scope); if (!cells.length) return null;
    const tr = closeHistory(state.tr);
    for (const { pos, node } of cells) {
        const attrs = { ...node.attrs };
        if (change.field === 'reset') for (const field of cellStyleFields) attrs[field] = null;
        else attrs[change.field] = change.value;
        if (!node.hasMarkup(node.type, attrs)) tr.setNodeMarkup(pos, undefined, attrs, node.marks);
    }
    return tr.docChanged && JSON.stringify(tr.doc.toJSON()).length <= 2_000_000 ? tr : null;
}
