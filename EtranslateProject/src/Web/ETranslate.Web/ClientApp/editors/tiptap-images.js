import { NodeSelection } from '@tiptap/pm/state';
import { closeHistory } from '@tiptap/pm/history';
import { isRichDocument } from '../document-schema.js';
import { bodyTextBlocks } from '../document-text-tools.js';
import { validImageLayout } from '../body-image.js';

export const selectedImage = state => state.selection instanceof NodeSelection && state.selection.node.type.name === 'image' && state.selection.$from.depth === 0 ? state.selection.node : null;
export const canInsertImage = state => state.selection.empty && state.selection.$from.depth === 1 && ['paragraph', 'heading'].includes(state.selection.$from.parent.type.name) &&
    state.doc.content.content.filter(node => node.type.name === 'image').length < 10;

export function imageTransaction(state, command, value, bookmark) {
    const tr = closeHistory(state.tr);
    if (command === 'insertImage') {
        if (!bookmark) return null;
        try { tr.setSelection(bookmark.resolve(tr.doc)); } catch { return null; }
        if (!canInsertImage({ doc: tr.doc, selection: tr.selection })) return null;
        const node = state.schema.nodes.image.create({ assetId: value.assetId, assetScope: 'document', widthPercent: 50, align: 'center' });
        const cursor = tr.selection.$from;
        if (cursor.parent.attrs.pageBreakBefore === true) {
            // A page start belongs to its paragraph. Retain that block (even if empty)
            // before the image and never clone the forced break onto the remaining text.
            const parent = cursor.parent, pos = cursor.before(1);
            tr.replaceWith(pos, pos + parent.nodeSize, [parent.copy(parent.content.cut(0, cursor.parentOffset)), node,
                parent.type.create({ ...parent.attrs, pageBreakBefore: null }, parent.content.cut(cursor.parentOffset))]);
        } else tr.replaceSelectionWith(node, false);
        let position;
        tr.doc.forEach((child, offset) => { if (child === node) position = offset; });
        if (position == null) return null;
        if (position + node.nodeSize === tr.doc.content.size) tr.insert(tr.doc.content.size, state.schema.nodes.paragraph.create());
        tr.setSelection(NodeSelection.create(tr.doc, position));
    } else {
        const node = selectedImage(state); if (!node) return null;
        if (command === 'deleteImage') tr.deleteSelection();
        else if (command === 'imageLayout' && validImageLayout(value)) {
            const attrs = { ...node.attrs, ...value };
            if (node.hasMarkup(node.type, attrs)) return null;
            tr.setNodeMarkup(state.selection.from, undefined, attrs);
        } else return null;
    }
    const json = tr.doc.toJSON();
    return isRichDocument(json, 10) && JSON.stringify(json).length <= 2_000_000 && bodyTextBlocks(json).join('\n').length <= 1_000_000 ? tr.scrollIntoView() : null;
}
