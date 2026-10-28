import { closeHistory } from '@tiptap/pm/history';
import { selectedTable } from './tiptap-table-context.js';
import { validColumnWidths } from '../table-widths.js';
import { columnCount } from '../table-spans.js';

export function setTableColumnWidths(state, widths) {
    const table = selectedTable(state);
    if (!table || !validColumnWidths(widths, columnCount(table.node.firstChild.content.content))) return null;
    if (JSON.stringify(table.node.attrs.columnWidths ?? null) === JSON.stringify(widths)) return null;
    return closeHistory(state.tr).setNodeMarkup(table.pos, undefined, { ...table.node.attrs, columnWidths: widths?.slice() ?? null });
}
// Part of the same column-edit transaction: do not leave an invalid length or
// silently invent percentages for the user's new column arrangement.
export function resetChangedColumnCounts({ tr }) {
    tr.doc.descendants((node, pos) => {
        if (node.type.name === 'table' && Array.isArray(node.attrs.columnWidths) && node.attrs.columnWidths.length !== columnCount(node.firstChild.content.content))
            tr.setNodeMarkup(pos, undefined, { ...node.attrs, columnWidths: null });
    });
    return true;
}
