import { Fragment } from '@tiptap/pm/model';
import { Selection } from '@tiptap/pm/state';
import { closeHistory } from '@tiptap/pm/history';
import { selectedTable } from './tiptap-table-context.js';
import { selectedCells } from './tiptap-cell-style.js';
import { isRichDocument } from '../document-schema.js';
import { bodyTextBlocks } from '../document-text-tools.js';

export function horizontalCells(state, command) {
    const table = selectedTable(state); if (!table) return null;
    const selected = selectedCells(state).sort((a, b) => a.pos - b.pos); if (!selected.length) return null;
    let rowCells;
    table.node.forEach((row, rowOffset) => {
        const cells = []; row.forEach((node, cellOffset) => cells.push({ node, pos: table.pos + 2 + rowOffset + cellOffset }));
        if (cells.some(cell => cell.pos === selected[0].pos)) rowCells = cells;
    });
    if (!rowCells) return null;
    const start = rowCells.findIndex(cell => cell.pos === selected[0].pos);
    if (command === 'splitCellInRow') return selected.length === 1 && selected[0].node.attrs.colspan > 1 ? selected : null;
    const cells = command === 'mergeNextCell' ? selected.length === 1 ? rowCells.slice(start, start + 2) : [] : selected;
    if (cells.length < 2 || !cells.every((cell, index) => rowCells[start + index]?.pos === cell.pos)) return null;
    const first = cells[0].node;
    // Never silently discard the other cells' type, direction, or custom paint.
    if (!cells.every(({ node }) => node.attrs.rowspan === 1 && node.hasMarkup(first.type, { ...first.attrs, colspan: node.attrs.colspan }, first.marks))) return null;
    return cells;
}
export function changeHorizontalCells(state, command) {
    if (!['mergeNextCell', 'mergeCellsInRow', 'splitCellInRow'].includes(command)) return null;
    const cells = horizontalCells(state, command); if (!cells) return null;
    const first = cells[0], tr = closeHistory(state.tr);
    if (command === 'splitCellInRow') {
        const attrs = { ...first.node.attrs, colspan: 1 };
        const nodes = [first.node.type.create(attrs, first.node.content, first.node.marks)];
        for (let i = 1; i < first.node.attrs.colspan; i++) nodes.push(first.node.type.createAndFill(attrs));
        if (nodes.some(node => !node)) return null;
        tr.replaceWith(first.pos, first.pos + first.node.nodeSize, nodes);
    } else {
        const content = [];
        for (const { node } of cells) node.forEach(child => content.push(child));
        const span = cells.reduce((sum, { node }) => sum + node.attrs.colspan, 0);
        const merged = first.node.type.create({ ...first.node.attrs, colspan: span }, Fragment.fromArray(content), first.node.marks);
        const last = cells.at(-1); tr.replaceWith(first.pos, last.pos + last.node.nodeSize, merged);
    }
    const json = tr.doc.toJSON();
    if (!isRichDocument(json, 9) || JSON.stringify(json).length > 2_000_000 || bodyTextBlocks(json).join('\n').length > 1_000_000) return null;
    return tr.setSelection(Selection.near(tr.doc.resolve(first.pos + 1))).scrollIntoView();
}
