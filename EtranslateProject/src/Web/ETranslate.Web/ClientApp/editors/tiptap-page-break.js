import { TextSelection } from '@tiptap/pm/state';
import { closeHistory } from '@tiptap/pm/history';
import { isRichDocument } from '../document-schema.js';
import { bodyTextBlocks } from '../document-text-tools.js';
import { maximumPageStarts } from '../page-break.js';

function currentBlock(state) {
    const { selection } = state;
    return selection instanceof TextSelection && selection.empty && selection.$from.depth === 1 && ['paragraph', 'heading'].includes(selection.$from.parent.type.name)
        ? { node: selection.$from.parent, pos: selection.$from.before(1), index: selection.$from.index(0), offset: selection.$from.parentOffset } : null;
}
export function pageBreakState(state, command) {
    const block = currentBlock(state); if (!block) return false;
    if (command === 'removePageBreak') return block.node.attrs.pageBreakBefore === true;
    if (command !== 'insertPageBreak' || state.doc.content.content.filter(node => node.attrs.pageBreakBefore === true).length >= maximumPageStarts) return false;
    return block.offset > 0 || block.index > 0 && block.node.attrs.pageBreakBefore !== true;
}
export function pageBreakTransaction(state, command) {
    if (!pageBreakState(state, command)) return null;
    const { node, pos, offset } = currentBlock(state), tr = closeHistory(state.tr);
    if (command === 'removePageBreak' || offset === 0) {
        tr.setNodeMarkup(pos, undefined, { ...node.attrs, pageBreakBefore: command === 'removePageBreak' ? null : true });
    } else {
        const left = node.copy(node.content.cut(0, offset)), attrs = { ...node.attrs, pageBreakBefore: true };
        const type = node.type.name === 'heading' && offset === node.content.size ? state.schema.nodes.paragraph : node.type;
        if (type !== node.type) delete attrs.level;
        const right = type.create(attrs, node.content.cut(offset));
        tr.replaceWith(pos, pos + node.nodeSize, [left, right]);
        tr.setSelection(TextSelection.create(tr.doc, pos + left.nodeSize + 1));
    }
    const json = tr.doc.toJSON();
    return isRichDocument(json, 10) && JSON.stringify(json).length <= 2_000_000 && bodyTextBlocks(json).join('\n').length <= 1_000_000 ? tr.scrollIntoView() : null;
}
