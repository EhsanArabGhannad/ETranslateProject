import { Fragment, Slice } from '@tiptap/pm/model';
import { AllSelection, Selection, TextSelection } from '@tiptap/pm/state';
import { closeHistory } from '@tiptap/pm/history';
import { isRichDocument } from '../document-schema.js';
import { bodyTextBlocks } from '../document-text-tools.js';
import { importClipboard } from '../clipboard-import.js';

const internalType = 'application/x-etranslate-local-slice';
export function clipboardTransaction(state, content, warnings = []) {
    if (!(state.selection instanceof TextSelection || state.selection instanceof AllSelection)) return null;
    const imported = state.schema.nodeFromJSON(content); imported.check();
    const nodes = [...imported.content.content], selection = state.selection, { $from, $to } = selection;
    let tr = closeHistory(state.tr);
    const inline = nodes.length === 1 && nodes[0].type.name === 'paragraph' && !nodes[0].attrs.pageBreakBefore;
    if (inline && $from.sameParent($to) && $from.parent.isTextblock && $from.parent.content.size > 0) {
        if (Object.entries(nodes[0].attrs).some(([key, value]) => value != null && key !== 'dir' || key === 'dir' && value !== 'auto' && value != null)) warnings.push('paragraphContext');
        tr.replaceSelection(new Slice(nodes[0].content, 0, 0));
    } else if ($from.depth === 1 && $from.sameParent($to) && $from.parent.isTextblock) {
        const original = $from.parent, pos = $from.before(1), left = original.copy(original.content.cut(0, $from.parentOffset));
        const right = original.type.create({ ...original.attrs, pageBreakBefore: null }, original.content.cut($to.parentOffset));
        const values = [];
        if (left.content.size) values.push(left);
        else if (original.attrs.pageBreakBefore === true) {
            if (['paragraph', 'heading'].includes(nodes[0].type.name)) nodes[0] = nodes[0].type.create({ ...nodes[0].attrs, pageBreakBefore: true }, nodes[0].content);
            else values.push(left);
        }
        values.push(...nodes); const end = pos + values.reduce((n, node) => n + node.nodeSize, 0);
        if (right.content.size) values.push(right);
        tr.replaceWith(pos, pos + original.nodeSize, values).setSelection(Selection.near(tr.doc.resolve(end), -1));
    } else tr.replaceSelection(new Slice(Fragment.fromArray(nodes), 0, 0));
    tr.setStoredMarks(null);
    const json = tr.doc.toJSON();
    return isRichDocument(json, 10) && JSON.stringify(json).length <= 2_000_000 && bodyTextBlocks(json).join('\n').length <= 1_000_000 ? tr.scrollIntoView().setMeta('paste', true).setMeta('uiEvent', 'paste') : null;
}

export function createTiptapClipboard(getEditor, notify) {
    let mode = 'formatted', report = null, shiftPaste = false, localCopy = null;
    const publish = value => { report = value; notify({ documentChanged: false }); };
    const commit = tr => {
        if (!tr) { publish({ outcome: 'rejected', error: 'context', warnings: [] }); return; }
        const editor = getEditor(); editor.view.dispatch(tr); editor.view.dispatch(closeHistory(editor.state.tr));
    };
    function copy(view, event, cut) {
        if (!event.clipboardData || view.state.selection.empty) return false;
        event.preventDefault();
        const slice = view.state.selection.content(), serialized = view.serializeForClipboard(slice), token = crypto.randomUUID();
        event.clipboardData.clearData(); event.clipboardData.setData('text/html', serialized.dom.innerHTML); event.clipboardData.setData('text/plain', serialized.text);
        event.clipboardData.setData(internalType, token); localCopy = { token, slice };
        if (cut && getEditor().isEditable) commit(closeHistory(view.state.tr).deleteSelection());
        return true;
    }
    return {
        getState: () => ({ supported: true, mode, report }),
        setMode(value) { if (!['formatted', 'plain'].includes(value)) return false; mode = value; notify({ documentChanged: false }); return true; },
        destroy() { localCopy = null; report = null; },
        events: {
            keydown(_view, event) { shiftPaste = (event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === 'v'; return false; },
            keyup() { shiftPaste = false; return false; },
            copy: (view, event) => copy(view, event, false), cut: (view, event) => copy(view, event, true),
            paste(view, event) {
                event.preventDefault(); const plain = shiftPaste || mode === 'plain'; shiftPaste = false;
                const editor = getEditor(); if (!editor.isEditable) return true;
                const data = event.clipboardData;
                if (!data) { publish({ outcome: 'rejected', error: 'empty', warnings: [] }); return true; }
                try {
                    if (!plain && localCopy && data.getData(internalType) === localCopy.token) {
                        const tr = closeHistory(view.state.tr).replaceSelection(localCopy.slice), json = tr.doc.toJSON();
                        if (!isRichDocument(json, 10) || JSON.stringify(json).length > 2_000_000 || bodyTextBlocks(json).join('\n').length > 1_000_000) throw new Error();
                        commit(tr.scrollIntoView().setMeta('paste', true).setMeta('uiEvent', 'paste')); publish({ outcome: 'inserted', warnings: [], plain: false, internal: true }); return true;
                    }
                    const bodyContext = view.state.selection instanceof AllSelection || view.state.selection.$from.depth === 1;
                    const result = importClipboard({ html: data.getData('text/html'), text: data.getData('text/plain'), mode: plain ? 'plain' : 'formatted' }, view.dom.ownerDocument,
                        { allowTables: bodyContext, allowStarts: bodyContext });
                    if (data.files?.length) result.warnings.push('images');
                    if (result.error) { publish({ outcome: 'rejected', error: result.error, warnings: result.warnings }); return true; }
                    const tr = clipboardTransaction(view.state, result.content, result.warnings);
                    if (tr) { commit(tr); publish({ outcome: 'inserted', warnings: [...new Set(result.warnings)], plain: result.plain }); }
                    else publish({ outcome: 'rejected', error: 'context', warnings: [] });
                } catch { publish({ outcome: 'rejected', error: 'context', warnings: [] }); }
                return true;
            }
        }
    };
}
