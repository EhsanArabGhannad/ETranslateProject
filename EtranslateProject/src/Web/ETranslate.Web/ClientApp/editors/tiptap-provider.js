import { Editor, Extension, Node } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TableKit } from '@tiptap/extension-table';
import { Plugin } from '@tiptap/pm/state';
import { guid, isRichDocument } from '../document-schema.js';
import { editorCommands, richDocumentV1 } from './document-contract.js';

export const tiptapProvider = Object.freeze({
    id: 'tiptap', format: richDocumentV1,
    capabilities: Object.freeze({ livePagination: false, docxImport: false, faithfulWordPaste: false, mergedCells: false }),
    create: createTiptapAdapter
});

function createTiptapAdapter({ element, content, editable, resolveAsset, labels, onRejectedChange }) {
    let editor;
    const listeners = new Set();
    const notify = event => { for (const listener of listeners) listener(event); };
    const ManagedImage = Node.create({
        name: 'image', group: 'block', atom: true, draggable: false,
        addAttributes() {
            return { assetId: { default: null, parseHTML: element => element.getAttribute('data-asset-id'), renderHTML: attrs =>
                guid.test(attrs.assetId ?? '') ? { 'data-asset-id': attrs.assetId } : {} } };
        },
        parseHTML() { return [{ tag: 'img[data-asset-id]', getAttrs: element => guid.test(element.getAttribute('data-asset-id') ?? '') ? {} : false }]; },
        renderHTML({ node }) {
            const url = resolveAsset(node.attrs.assetId);
            return url ? ['img', { src: url, alt: labels.image, 'data-asset-id': node.attrs.assetId }]
                : ['span', { class: 'missing-asset' }, labels.missingImage];
        }
    });
    const PreservationPolicy = Extension.create({
        name: 'preservationPolicy',
        addProseMirrorPlugins() {
            return [new Plugin({ filterTransaction: transaction => {
                if (!transaction.docChanged || isRichDocument(transaction.doc.toJSON())) return true;
                onRejectedChange(); return false;
            } })];
        }
    });
    try {
        editor = new Editor({
            element, injectCSS: false, editable, textDirection: 'auto',
            extensions: [StarterKit.configure({ code: false, codeBlock: false, link: false, horizontalRule: false,
                dropcursor: false, trailingNode: false }), ManagedImage,
                TableKit.configure({ table: { resizable: false, View: null } }), PreservationPolicy],
            content: { type: 'doc', content: [{ type: 'paragraph' }] },
            editorProps: { attributes: { role: 'textbox', 'aria-label': labels.text, 'aria-multiline': 'true', spellcheck: 'false' } }
        });
        const value = editor.schema.nodeFromJSON(content); value.check();
        // Initial content must not be undoable into an empty document or mark a mounted revision dirty.
        editor.chain().setMeta('addToHistory', false)
            .setContent(value, { emitUpdate: false, errorOnInvalidContent: true }).run();
    } catch (error) { editor?.destroy(); throw error; }

    const tableCommands = ['addRowAfter', 'addColumnAfter', 'deleteRow', 'deleteColumn', 'deleteTable'];
    function tableSize() {
        const position = editor.state.selection.$from;
        for (let depth = position.depth; depth > 0; depth--) {
            const node = position.node(depth);
            if (node.type.name === 'table') return { rows: node.childCount, columns: node.firstChild.childCount };
        }
        return null;
    }
    function allowedTable(command) {
        const size = tableSize();
        if (command === 'insertTable') return !size && editor.state.selection.$from.depth === 1;
        return !!size && (command !== 'addRowAfter' || size.rows < 50) && (command !== 'addColumnAfter' || size.columns < 12);
    }
    const operations = {
        bold: chain => chain.toggleBold(), italic: chain => chain.toggleItalic(), underline: chain => chain.toggleUnderline(),
        paragraph: chain => chain.setParagraph(), heading: chain => chain.toggleHeading({ level: 2 }),
        bulletList: chain => chain.toggleBulletList(), orderedList: chain => chain.toggleOrderedList(),
        rtl: chain => chain.setTextDirection('rtl'), ltr: chain => chain.setTextDirection('ltr'), auto: chain => chain.setTextDirection('auto'),
        undo: chain => chain.undo(), redo: chain => chain.redo(),
        insertTable: chain => chain.insertTable({ rows: 3, cols: 3, withHeaderRow: true }),
        addRowAfter: chain => chain.addRowAfter(), addColumnAfter: chain => chain.addColumnAfter(),
        deleteRow: chain => chain.deleteRow(), deleteColumn: chain => chain.deleteColumn(), deleteTable: chain => chain.deleteTable()
    };
    function getCommandState(command) {
        if (!editorCommands.includes(command) || editor.isDestroyed) return { enabled: false, active: false };
        const active = ['rtl', 'ltr', 'auto'].includes(command)
            ? editor.isActive({ dir: command })
            : editor.isActive(command, command === 'heading' ? { level: 2 } : undefined);
        const tableAllowed = ![...tableCommands, 'insertTable'].includes(command) || allowedTable(command);
        return { enabled: editor.isEditable && tableAllowed && operations[command](editor.can().chain()).run(), active };
    }
    const transaction = ({ transaction }) => notify({ documentChanged: transaction.docChanged });
    editor.on('transaction', transaction);
    return {
        getDocument: () => editor.getJSON(),
        getCommandState,
        execute(command) {
            if (!getCommandState(command).enabled) return false;
            return operations[command](editor.chain().focus()).run();
        },
        setEditable(value) { editor.setEditable(value, false); notify({ documentChanged: false }); },
        subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); },
        destroy() { listeners.clear(); editor.off('transaction', transaction); editor.destroy(); }
    };
}
