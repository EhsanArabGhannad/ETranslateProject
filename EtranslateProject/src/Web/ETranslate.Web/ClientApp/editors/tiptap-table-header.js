import { closeHistory } from '@tiptap/pm/history';
import { selectedTable } from './tiptap-table-context.js';

// This command always targets the first row of one table, not the selected row.
// A selection extending outside that table must not silently modify either table.
export function firstTableRow(state) {
    const from = selectedTable(state); if (!from) return null;
    const row = from.node.firstChild;
    if (row?.type.name !== 'tableRow' || !row.childCount) return null;
    const cells = [];
    row.forEach((node, offset) => cells.push({ node, pos: from.pos + 2 + offset }));
    if (!cells.every(({ node }) => ['tableCell', 'tableHeader'].includes(node.type.name))) return null;
    return { cells, active: cells.every(({ node }) => node.type.name === 'tableHeader') };
}

export function toggleFirstTableRow(state) {
    const row = firstTableRow(state); if (!row) return null;
    const type = state.schema.nodes[row.active ? 'tableCell' : 'tableHeader']; if (!type) return null;
    const tr = closeHistory(state.tr);
    for (const { node, pos } of row.cells)
        if (node.type !== type) tr.setNodeMarkup(pos, type, node.attrs, node.marks);
    return tr.docChanged ? tr : null;
}
