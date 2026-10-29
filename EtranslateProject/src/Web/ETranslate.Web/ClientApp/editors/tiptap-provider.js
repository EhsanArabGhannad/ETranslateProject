import { Editor, Extension, Node, Mark } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TableKit, Table, TableCell, TableHeader } from '@tiptap/extension-table';
import Paragraph from '@tiptap/extension-paragraph';
import Heading from '@tiptap/extension-heading';
import { Plugin } from '@tiptap/pm/state';
import { closeHistory } from '@tiptap/pm/history';
import { guid, isRichDocument } from '../document-schema.js';
import { editorCommands, richDocumentV10 } from './document-contract.js';
import { typographyFields, typographyCommands, validTypography, validTypographyValue, applyTypography } from '../typography.js';
import { paragraphFields, paragraphCommands, paragraphActions, validParagraphValue, applyParagraph } from '../paragraph.js';
import { scriptMarks, applyInlineScript } from '../inline-script.js';
import {createTiptapSearch} from './tiptap-search.js';
import { tableOptions, tableLimits } from '../table-options.js';
import { firstTableRow, toggleFirstTableRow } from './tiptap-table-header.js';
import { selectedTable } from './tiptap-table-context.js';
import { setTableColumnWidths, resetChangedColumnCounts } from './tiptap-table-widths.js';
import { validColumnWidths, appendColumnGroup } from '../table-widths.js';
import { cellStyleFields, validCellStyleValue, applyCellStyle } from '../cell-style.js';
import { cellStyleState, changeCellStyle, selectedCells } from './tiptap-cell-style.js';
import { columnCount } from '../table-spans.js';
import { horizontalCells, changeHorizontalCells } from './tiptap-horizontal-merge.js';
import { imageFields, validImageValue, applyBodyImage } from '../body-image.js';
import { selectedImage, canInsertImage, imageTransaction } from './tiptap-images.js';
import { applyPageStart, pageBreakCommands } from '../page-break.js';
import { pageBreakState, pageBreakTransaction } from './tiptap-page-break.js';
import { createTiptapClipboard } from './tiptap-clipboard.js';

export const tiptapProvider = Object.freeze({
    id: 'tiptap', format: richDocumentV10,
    capabilities: Object.freeze({ livePagination: false, docxImport: false, faithfulWordPaste: false, boundedFormattedPaste: true, mergedCells: false, horizontalCellMerge: true, privateBodyImages: true, manualPageBreaks: true, literalSearch:true }),
    create: createTiptapAdapter
});

function createTiptapAdapter({ element, content, editable, resolveAsset, labels, onRejectedChange }) {
    let editor;
    const imageBookmarks = new Map(); let imageBookmarkId = 0;
    const search=createTiptapSearch();
    const widthContexts = new WeakMap(); let nextWidthContext = 0;
    function styledCell(Base, tag) { return Base.extend({
        addAttributes() { return { ...this.parent?.(), ...Object.fromEntries(cellStyleFields.map(field => [field, { default: null, rendered: false, parseHTML: element => {
            try { const attrs = JSON.parse(element.getAttribute('data-et-cell-style')); return attrs && validCellStyleValue(field, attrs[field]) ? attrs[field] : null; } catch { return null; }
        } }])) }; },
        renderHTML(properties) {
            const attrs = properties.node.attrs;
            if (!cellStyleFields.some(field => attrs[field] != null)) return this.parent(properties);
            const cell = document.createElement(tag); cell.dir = attrs.dir ?? 'auto'; cell.colSpan = attrs.colspan ?? 1; cell.rowSpan = 1;
            cell.dataset.etCellStyle = JSON.stringify(Object.fromEntries(cellStyleFields.map(field => [field, attrs[field] ?? null])));
            applyCellStyle(cell, attrs); return { dom: cell, contentDOM: cell };
        }
    }); }
    const StyledCell = styledCell(TableCell, 'td'), StyledHeader = styledCell(TableHeader, 'th');
    const WidthTable = Table.extend({
        addAttributes() { return { ...this.parent?.(), columnWidths: { default: null, rendered: false, parseHTML: element => {
            try { const widths = JSON.parse(element.getAttribute('data-et-column-widths')); return validColumnWidths(widths) ? widths : null; } catch { return null; }
        } } }; },
        renderHTML(properties) {
            const widths = properties.node.attrs.columnWidths;
            if (widths == null) return this.parent(properties);
            const table = document.createElement('table'), body = document.createElement('tbody');
            table.dir = properties.node.attrs.dir ?? 'auto'; table.style.width = '100%';
            table.dataset.etColumnWidths = JSON.stringify(widths); appendColumnGroup(table, widths); table.append(body);
            return { dom: table, contentDOM: body };
        }
    });
    const listeners = new Set();
    const notify = event => { for (const listener of listeners) listener(event); };
    const clipboard = createTiptapClipboard(() => editor, notify);
    function textBlock(Base,tag){return Base.extend({
        addAttributes(){return {...this.parent?.(),pageBreakBefore:{default:null,rendered:false,keepOnSplit:false,parseHTML:element=>element.getAttribute('data-et-page-start')==='true'?true:null},...Object.fromEntries(paragraphFields.map(field=>[field,{default:null,rendered:false,parseHTML:element=>{
            try{const value=JSON.parse(element.getAttribute('data-et-paragraph'));return value&&typeof value==='object'&&!Array.isArray(value)&&Object.entries(value).every(([key,item])=>validParagraphValue(key,item,5))?value[field]??null:null;}catch{return null;}
        }}]))};},
        renderHTML({node}){
            const element=document.createElement(tag??`h${node.attrs.level}`);element.dir=['auto','rtl','ltr'].includes(node.attrs.dir)?node.attrs.dir:'auto';
            element.dataset.etParagraph=JSON.stringify(Object.fromEntries(paragraphFields.map(field=>[field,node.attrs[field]??null])));
            applyParagraph(element,node.attrs);applyPageStart(element,node.attrs,labels.pageStart);return {dom:element,contentDOM:element};
        }
    });}
    const StyledParagraph=textBlock(Paragraph,'p'),StyledHeading=textBlock(Heading);
    const scripts=scriptMarks.map(name=>Mark.create({
        name,priority:105,excludes:scriptMarks.join(' '),
        parseHTML(){return [{tag:name==='superscript'?'sup':'sub'}];},
        renderHTML(){const element=document.createElement(name==='superscript'?'sup':'sub');applyInlineScript(element,name);return {dom:element,contentDOM:element};}
    }));
    const ManagedImage = Node.create({
        name: 'image', group: 'block', atom: true, draggable: false,
        addAttributes() {
            return { assetId: { default: null, parseHTML: element => element.getAttribute('data-asset-id'), renderHTML: attrs =>
                guid.test(attrs.assetId ?? '') ? { 'data-asset-id': attrs.assetId } : {} }, ...Object.fromEntries(imageFields.map(field => [field, { default: null, rendered: false, parseHTML: element => {
                    try { const attrs = JSON.parse(element.getAttribute('data-et-image')); return validImageValue(field, attrs?.[field]) ? attrs[field] : null; } catch { return null; }
                } }])) };
        },
        parseHTML() { return [{ tag: 'img[data-asset-id]', getAttrs: element => guid.test(element.getAttribute('data-asset-id') ?? '') ? {} : false }]; },
        renderHTML({ node }) {
            const url = resolveAsset(node.attrs.assetId, node.attrs.assetScope);
            if (!url) return ['span', { class: 'missing-asset' }, labels.missingImage];
            const image = document.createElement('img'); image.src = url; image.alt = node.attrs.assetScope === 'document' ? labels.bodyImage ?? labels.image : labels.image;
            image.dataset.assetId = node.attrs.assetId;
            image.dataset.etImage = JSON.stringify(Object.fromEntries(imageFields.map(field => [field, node.attrs[field] ?? null])));
            applyBodyImage(image, node.attrs); return { dom: image };
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
            return [search.plugin,new Plugin({ filterTransaction: transaction => {
                if (!transaction.docChanged || isRichDocument(transaction.doc.toJSON(),10)) return true;
                onRejectedChange(); return false;
            } })];
        }
    });
    const PageBreakKeys = Extension.create({
        name: 'manualPageBreakKeys', priority: 200,
        addKeyboardShortcuts() { return {
            'Mod-Enter': () => { changePageBreak('insertPageBreak'); return true; },
            Enter: () => {
                const { selection } = editor.state;
                if (!editor.isEditable || selection.$from.depth !== 1 || !selection.$from.sameParent(selection.$to) || selection.$from.parent.attrs.pageBreakBefore !== true) return false;
                // The vendor's middle-of-paragraph split clones attrs even when keepOnSplit
                // is false. Clear only the right block's page start in the same history step.
                const applied = editor.chain().command(({ tr }) => { closeHistory(tr); return true; }).splitBlock().command(({ tr }) => {
                    const cursor = tr.selection.$from;
                    if (cursor.depth === 1 && cursor.parent.attrs.pageBreakBefore === true) tr.setNodeMarkup(cursor.before(1), undefined, { ...cursor.parent.attrs, pageBreakBefore: null });
                    return true;
                }).run();
                if (applied) editor.view.dispatch(closeHistory(editor.state.tr)); return applied;
            },
            Backspace: () => editor.state.selection.$from.parentOffset === 0 && changePageBreak('removePageBreak')
        }; }
    });
    try {
        editor = new Editor({
            element, injectCSS: false, editable, textDirection: 'auto',
            extensions: [StarterKit.configure({ code: false, codeBlock: false, link: false, horizontalRule: false,
                dropcursor: false, trailingNode: false, paragraph:false,heading:false }), StyledParagraph,StyledHeading,ManagedImage, Typography,...scripts,
                TableKit.configure({ table: false, tableCell: false, tableHeader: false }), WidthTable.configure({ resizable: false, View: null }), StyledCell, StyledHeader, PageBreakKeys, PreservationPolicy],
            content: { type: 'doc', content: [{ type: 'paragraph' }] },
            editorProps: { handleDOMEvents: clipboard.events, attributes: { role: 'textbox', tabindex:'0', 'aria-label': labels.text, 'aria-multiline': 'true', spellcheck: 'false' } }
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
            if (node.type.name === 'table') return { rows: node.childCount, columns: columnCount(node.firstChild.content.content) };
        }
        return null;
    }
    function allowedTable(command) {
        const size = tableSize();
        if (command === 'insertTable') return !size && editor.state.selection.$from.depth === 1;
        if (command === 'deleteColumn' && selectedCells(editor.state).some(({node})=>node.attrs.colspan>1)) return false;
        return !!size && (command !== 'addRowAfter' || size.rows < tableLimits.rows) && (command !== 'addColumnAfter' || size.columns < tableLimits.columns);
    }
    const operations = {
        bold: chain => chain.toggleBold(), italic: chain => chain.toggleItalic(), underline: chain => chain.toggleUnderline(),
        strike: chain => chain.toggleStrike(),
        superscript: chain => chain.unsetMark('subscript').toggleMark('superscript'),
        subscript: chain => chain.unsetMark('superscript').toggleMark('subscript'),
        paragraph: chain => chain.setParagraph(), heading: chain => chain.toggleHeading({ level: 2 }),
        bulletList: chain => chain.toggleBulletList(), orderedList: chain => chain.toggleOrderedList(),
        rtl: chain => chain.setTextDirection('rtl'), ltr: chain => chain.setTextDirection('ltr'), auto: chain => chain.setTextDirection('auto'),
        undo: chain => chain.undo(), redo: chain => chain.redo(),
        insertTable: chain => chain.insertTable({ rows: 3, cols: 3, withHeaderRow: true }),
        addRowAfter: chain => chain.addRowAfter(), addColumnAfter: chain => chain.addColumnAfter().command(resetChangedColumnCounts),
        deleteRow: chain => chain.deleteRow(), deleteColumn: chain => chain.deleteColumn().command(resetChangedColumnCounts), deleteTable: chain => chain.deleteTable()
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
    function selectedBlocks(state){
        const blocks=new Map();
        for(const range of state.selection.ranges){const from=range.$from.pos,to=range.$to.pos;
            if(from===to){for(let depth=range.$from.depth;depth>0;depth--){const node=range.$from.node(depth);if(['paragraph','heading'].includes(node.type.name)){blocks.set(range.$from.before(depth),node);break;}}}
            else state.doc.nodesBetween(from,to,(node,pos)=>{if(['paragraph','heading'].includes(node.type.name)&&pos<to&&pos+node.nodeSize>from)blocks.set(pos,node);});
        }return [...blocks].map(([pos,node])=>({pos,node}));
    }
    function paragraphState(field){const values=new Set(selectedBlocks(editor.state).map(({node})=>node.attrs[field]??null));return {value:values.size===1?[...values][0]:null,mixed:values.size>1};}
    function updateParagraph(command,value){return editor.chain().focus().command(({tr})=>{
        const blocks=selectedBlocks({doc:tr.doc,selection:tr.selection});if(!blocks.length)return false;
        closeHistory(tr);
        for(const {pos,node} of blocks){const attrs={...node.attrs};
            if(command==='clearParagraphStyle')for(const field of paragraphFields)attrs[field]=null;
            else if(command==='increaseIndent'||command==='decreaseIndent')attrs.indentLevel=Math.max(0,Math.min(8,(attrs.indentLevel??0)+(command==='increaseIndent'?1:-1)));
            else attrs[paragraphCommands[command]]=value;
            if(!node.hasMarkup(node.type,attrs))tr.setNodeMarkup(pos,undefined,attrs);
        }return true;
    }).run();}
    function changeBlock(command){return editor.chain().focus().command(({tr})=>{
        const blocks=selectedBlocks({doc:tr.doc,selection:tr.selection});if(!blocks.length)return false;
        const heading=command==='heading'&&!blocks.every(({node})=>node.type.name==='heading'&&node.attrs.level===2);
        closeHistory(tr);
        for(const {pos,node} of blocks){const attrs={...node.attrs};if(heading)attrs.level=2;else delete attrs.level;
            const type=editor.schema.nodes[heading?'heading':'paragraph'];
            if(!node.hasMarkup(type,attrs))tr.setNodeMarkup(pos,type,attrs);
        }return true;
    }).run();}
    function getCommandState(command) {
        if (!editorCommands.includes(command) || editor.isDestroyed) return { enabled: false, active: false };
        if (['bulletList', 'orderedList'].includes(command) && selectedBlocks(editor.state).some(({ node }) => node.attrs.pageBreakBefore === true)) return { enabled: false, active: false };
        if (pageBreakCommands.includes(command)) return { enabled: editor.isEditable && pageBreakState(editor.state, command), active: false };
        if (command === 'insertImage') {
            let selection = editor.state.selection;
            try { if (imageBookmarks.size) selection = imageBookmarks.values().next().value.resolve(editor.state.doc); } catch { return { enabled: false, active: false }; }
            return { enabled: editor.isEditable && canInsertImage({ doc: editor.state.doc, selection }), active: false };
        }
        if (command === 'imageLayout' || command === 'deleteImage') {
            const node = selectedImage(editor.state);
            return { enabled: editor.isEditable && !!node, active: !!node, context: node ? `${editor.state.selection.from}:${node.attrs.assetId}` : null,
                widthPercent: node?.attrs.widthPercent ?? null, align: node?.attrs.align ?? null };
        }
        if (['mergeNextCell', 'mergeCellsInRow', 'splitCellInRow'].includes(command)) return { enabled: editor.isEditable && horizontalCells(editor.state, command) !== null, active: false };
        if (command === 'tableCellStyle') {
            const selection = cellStyleState(editor.state, 'selection'), table = cellStyleState(editor.state, 'table');
            return { enabled: editor.isEditable && selection.count > 0, active: selection.custom, selection, table };
        }
        if (command === 'tableColumnWidths') {
            const table = selectedTable(editor.state), widths = table?.node.attrs.columnWidths ?? null;
            if (table && !widthContexts.has(table.node)) widthContexts.set(table.node, `table-${++nextWidthContext}`);
            return { enabled: editor.isEditable && table !== null, active: widths !== null, columns: table ? columnCount(table.node.firstChild.content.content) : 0, value: widths?.slice() ?? null,
                context: table ? widthContexts.get(table.node) : null };
        }
        if (command === 'toggleTableHeaderRow') {
            const row = firstTableRow(editor.state);
            return { enabled: editor.isEditable && row !== null, active: row?.active === true };
        }
        // Tiptap's generic can().setMark rejects an opposite excluding mark. These commands
        // deliberately replace that mark in the same transaction, including at the caret.
        if(scriptMarks.includes(command))return {enabled:editor.isEditable&&selectedBlocks(editor.state).some(({node})=>node.type.allowsMarkType(editor.schema.marks[command])),active:editor.isActive(command)};
        if(Object.hasOwn(paragraphCommands,command)||paragraphActions.includes(command)){
            const blocks=selectedBlocks(editor.state);let enabled=editor.isEditable&&blocks.length>0;
            if(command==='increaseIndent')enabled&&=blocks.some(({node})=>(node.attrs.indentLevel??0)<8);
            if(command==='decreaseIndent')enabled&&=blocks.some(({node})=>(node.attrs.indentLevel??0)>0);
            return {enabled,active:false,...(Object.hasOwn(paragraphCommands,command)?paragraphState(paragraphCommands[command]):{})};
        }
        if(Object.hasOwn(typographyCommands,command))return {enabled:editor.isEditable,active:false,...typographyState(typographyCommands[command])};
        if(command==='clearTextStyle')return {enabled:editor.isEditable,active:false};
        const active = ['rtl', 'ltr', 'auto'].includes(command)
            ? editor.isActive({ dir: command })
            : editor.isActive(command, command === 'heading' ? { level: 2 } : undefined);
        const tableAllowed = ![...tableCommands, 'insertTable'].includes(command) || allowedTable(command);
        return { enabled: editor.isEditable && tableAllowed && operations[command](editor.can().chain()).run(), active };
    }
    const transaction = ({ transaction }) => {
        for (const [id, bookmark] of imageBookmarks) imageBookmarks.set(id, bookmark.map(transaction.mapping));
        notify({ documentChanged: transaction.docChanged });
    };
    function changePageBreak(command) {
        if (!getCommandState(command).enabled) return false;
        editor.view.focus(); const tr = pageBreakTransaction(editor.state, command); if (!tr) return false;
        editor.view.dispatch(tr); editor.view.dispatch(closeHistory(editor.state.tr)); return true;
    }
    editor.on('transaction', transaction);
    return {
        getDocument: () => editor.getJSON(),
        getPasteState: clipboard.getState,
        setPasteMode: clipboard.setMode,
        captureImageInsertion() { if (!editor.isEditable || !canInsertImage(editor.state)) return null; imageBookmarks.clear(); const id = `image-${++imageBookmarkId}`; imageBookmarks.set(id, editor.state.selection.getBookmark()); return id; },
        releaseImageInsertion(id) { imageBookmarks.delete(id); },
        setSearch:options=>search.setSearch(editor,options),
        getSearchState:()=>search.getState(editor),
        findMatch:direction=>search.find(editor,direction),
        replaceSearch:(value,all)=>search.replace(editor,value,all),
        getCommandState,
        execute(command, value) {
            if (!getCommandState(command).enabled) return false;
            if (pageBreakCommands.includes(command)) return changePageBreak(command);
            if (['insertImage', 'imageLayout', 'deleteImage'].includes(command)) {
                editor.view.focus();
                const tr = imageTransaction(editor.state, command, value, imageBookmarks.get(value?.bookmark)); if (!tr) return false;
                editor.view.dispatch(tr); editor.view.dispatch(closeHistory(editor.state.tr));
                if (command === 'insertImage') imageBookmarks.delete(value.bookmark); return true;
            }
            if (['mergeNextCell', 'mergeCellsInRow', 'splitCellInRow'].includes(command)) {
                editor.view.focus(); const tr = changeHorizontalCells(editor.state, command); if (!tr) return false;
                editor.view.dispatch(tr); editor.view.dispatch(closeHistory(editor.state.tr)); return true;
            }
            if (command === 'tableCellStyle') {
                editor.view.focus(); const tr = changeCellStyle(editor.state, value); if (!tr) return false;
                editor.view.dispatch(tr.scrollIntoView()); editor.view.dispatch(closeHistory(editor.state.tr)); return true;
            }
            if (command === 'tableColumnWidths') {
                editor.view.focus();
                const tr = setTableColumnWidths(editor.state, value); if (!tr) return false;
                editor.view.dispatch(tr.scrollIntoView()); editor.view.dispatch(closeHistory(editor.state.tr)); return true;
            }
            if (command === 'toggleTableHeaderRow') {
                editor.view.focus();
                const tr = toggleFirstTableRow(editor.state); if (!tr) return false;
                editor.view.dispatch(tr.scrollIntoView());
                editor.view.dispatch(closeHistory(editor.state.tr));
                return true;
            }
            if (command === 'insertTable') {
                const options = tableOptions(value); if (!options) return false;
                const inserted = editor.chain().focus().command(({tr}) => { closeHistory(tr); return true; })
                    .insertTable({ rows: options.rows, cols: options.columns, withHeaderRow: options.withHeaderRow }).run();
                // Keep subsequent typing separate: one Undo removes just the inserted table.
                if (inserted) editor.view.dispatch(closeHistory(editor.state.tr));
                return inserted;
            }
            if(command==='paragraph'||command==='heading')return changeBlock(command);
            if(Object.hasOwn(paragraphCommands,command))return validParagraphValue(paragraphCommands[command],value,5)&&updateParagraph(command,value);
            if(paragraphActions.includes(command))return updateParagraph(command);
            if(Object.hasOwn(typographyCommands,command)){
                const field=typographyCommands[command];if(!validTypographyValue(field,value))return false;
                return editor.chain().focus().command(({tr})=>{closeHistory(tr);return true;}).setMark('typography',{[field]:value}).command(removeEmptyTypography).run();
            }
            if(command==='clearTextStyle')return editor.chain().focus().command(({tr})=>{closeHistory(tr);return true;}).unsetMark('typography').run();
            if(scriptMarks.includes(command)||command==='strike')return operations[command](editor.chain().focus().command(({tr})=>{closeHistory(tr);return true;})).run();
            return operations[command](editor.chain().focus()).run();
        },
        setEditable(value) { editor.setEditable(value, false); notify({ documentChanged: false }); },
        subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); },
        destroy() { clipboard.destroy(); imageBookmarks.clear(); listeners.clear(); editor.off('transaction', transaction); editor.destroy(); }
    };
}
