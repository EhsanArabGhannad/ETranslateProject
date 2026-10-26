import { Editor, Extension, Node, Mark } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TableKit } from '@tiptap/extension-table';
import { Plugin } from '@tiptap/pm/state';
import { closeHistory } from '@tiptap/pm/history';
import { guid, isRichDocument } from '../document-schema.js';
import { editorCommands, richDocumentV2 } from './document-contract.js';
import { typographyFields, typographyCommands, validTypography, validTypographyValue, applyTypography } from '../typography.js';

export const tiptapProvider = Object.freeze({
    id: 'tiptap', format: richDocumentV2,
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
    const Typography = Mark.create({
        name:'typography', priority:110,
        addAttributes(){return Object.fromEntries(typographyFields.map(field=>[field,{default:null,rendered:false}]));},
        parseHTML(){return [{tag:'span[data-et-typography]',getAttrs:element=>{
            try{const attrs=JSON.parse(element.getAttribute('data-et-typography'));return validTypography(attrs)?attrs:false;}catch{return false;}
        }}];},
        renderHTML({mark}){
            const span=document.createElement('span');span.dataset.etTypography=JSON.stringify(mark.attrs);
            applyTypography(span,mark.attrs);return {dom:span,contentDOM:span};
        }
    });
    const PreservationPolicy = Extension.create({
        name: 'preservationPolicy',
        addProseMirrorPlugins() {
            return [new Plugin({ filterTransaction: transaction => {
                if (!transaction.docChanged || isRichDocument(transaction.doc.toJSON(),2)) return true;
                onRejectedChange(); return false;
            } })];
        }
    });
    try {
        editor = new Editor({
            element, injectCSS: false, editable, textDirection: 'auto',
            extensions: [StarterKit.configure({ code: false, codeBlock: false, link: false, horizontalRule: false,
                dropcursor: false, trailingNode: false }), ManagedImage, Typography,
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
    function typographyState(field){
        const {from,to,$from}=editor.state.selection,values=new Set();
        const read=marks=>values.add(marks.find(mark=>mark.type.name==='typography')?.attrs[field]??null);
        if(from===to)read(editor.state.storedMarks??$from.marks());
        else editor.state.doc.nodesBetween(from,to,node=>{if(node.isText)read(node.marks);});
        return {value:values.size===1?[...values][0]:null,mixed:values.size>1};
    }
    function removeEmptyTypography({tr}){
        const {from,to}=tr.selection;
        if(from===to){const mark=(tr.storedMarks??tr.selection.$from.marks()).find(mark=>mark.type.name==='typography');
            if(mark&&!validTypography(mark.attrs))tr.removeStoredMark(editor.schema.marks.typography);
        }else tr.doc.nodesBetween(from,to,(node,pos)=>{const mark=node.marks.find(mark=>mark.type.name==='typography');
            if(node.isText&&mark&&!validTypography(mark.attrs))tr.removeMark(Math.max(from,pos),Math.min(to,pos+node.nodeSize),editor.schema.marks.typography);});
        return true;
    }
    function getCommandState(command) {
        if (!editorCommands.includes(command) || editor.isDestroyed) return { enabled: false, active: false };
        if(Object.hasOwn(typographyCommands,command))return {enabled:editor.isEditable,active:false,...typographyState(typographyCommands[command])};
        if(command==='clearTextStyle')return {enabled:editor.isEditable,active:false};
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
        execute(command, value) {
            if (!getCommandState(command).enabled) return false;
            if(Object.hasOwn(typographyCommands,command)){
                const field=typographyCommands[command];if(!validTypographyValue(field,value))return false;
                return editor.chain().focus().command(({tr})=>{closeHistory(tr);return true;}).setMark('typography',{[field]:value}).command(removeEmptyTypography).run();
            }
            if(command==='clearTextStyle')return editor.chain().focus().command(({tr})=>{closeHistory(tr);return true;}).unsetMark('typography').run();
            return operations[command](editor.chain().focus()).run();
        },
        setEditable(value) { editor.setEditable(value, false); notify({ documentChanged: false }); },
        subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); },
        destroy() { listeners.clear(); editor.off('transaction', transaction); editor.destroy(); }
    };
}
