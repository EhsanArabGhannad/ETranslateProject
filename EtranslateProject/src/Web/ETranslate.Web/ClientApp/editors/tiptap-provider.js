import { Editor, Extension, Node, Mark } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TableKit } from '@tiptap/extension-table';
import Paragraph from '@tiptap/extension-paragraph';
import Heading from '@tiptap/extension-heading';
import { Plugin } from '@tiptap/pm/state';
import { closeHistory } from '@tiptap/pm/history';
import { guid, isRichDocument } from '../document-schema.js';
import { editorCommands, richDocumentV5 } from './document-contract.js';
import { typographyFields, typographyCommands, validTypography, validTypographyValue, applyTypography } from '../typography.js';
import { paragraphFields, paragraphCommands, paragraphActions, validParagraphValue, applyParagraph } from '../paragraph.js';
import { scriptMarks, applyInlineScript } from '../inline-script.js';
import {createTiptapSearch} from './tiptap-search.js';
import { tableOptions, tableLimits } from '../table-options.js';
import { firstTableRow, toggleFirstTableRow } from './tiptap-table-header.js';

export const tiptapProvider = Object.freeze({
    id: 'tiptap', format: richDocumentV5,
    capabilities: Object.freeze({ livePagination: false, docxImport: false, faithfulWordPaste: false, mergedCells: false, literalSearch:true }),
    create: createTiptapAdapter
});

function createTiptapAdapter({ element, content, editable, resolveAsset, labels, onRejectedChange }) {
    let editor;
    const search=createTiptapSearch();
    const listeners = new Set();
    const notify = event => { for (const listener of listeners) listener(event); };
    function textBlock(Base,tag){return Base.extend({
        addAttributes(){return {...this.parent?.(),...Object.fromEntries(paragraphFields.map(field=>[field,{default:null,rendered:false,parseHTML:element=>{
            try{const value=JSON.parse(element.getAttribute('data-et-paragraph'));return value&&typeof value==='object'&&!Array.isArray(value)&&Object.entries(value).every(([key,item])=>validParagraphValue(key,item,5))?value[field]??null:null;}catch{return null;}
        }}]))};},
        renderHTML({node}){
            const element=document.createElement(tag??`h${node.attrs.level}`);element.dir=['auto','rtl','ltr'].includes(node.attrs.dir)?node.attrs.dir:'auto';
            element.dataset.etParagraph=JSON.stringify(Object.fromEntries(paragraphFields.map(field=>[field,node.attrs[field]??null])));
            applyParagraph(element,node.attrs);return {dom:element,contentDOM:element};
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
            return [search.plugin,new Plugin({ filterTransaction: transaction => {
                if (!transaction.docChanged || isRichDocument(transaction.doc.toJSON(),5)) return true;
                onRejectedChange(); return false;
            } })];
        }
    });
    try {
        editor = new Editor({
            element, injectCSS: false, editable, textDirection: 'auto',
            extensions: [StarterKit.configure({ code: false, codeBlock: false, link: false, horizontalRule: false,
                dropcursor: false, trailingNode: false, paragraph:false,heading:false }), StyledParagraph,StyledHeading,ManagedImage, Typography,...scripts,
                TableKit.configure({ table: { resizable: false, View: null } }), PreservationPolicy],
            content: { type: 'doc', content: [{ type: 'paragraph' }] },
            editorProps: { attributes: { role: 'textbox', tabindex:'0', 'aria-label': labels.text, 'aria-multiline': 'true', spellcheck: 'false' } }
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
    const transaction = ({ transaction }) => notify({ documentChanged: transaction.docChanged });
    editor.on('transaction', transaction);
    return {
        getDocument: () => editor.getJSON(),
        setSearch:options=>search.setSearch(editor,options),
        getSearchState:()=>search.getState(editor),
        findMatch:direction=>search.find(editor,direction),
        replaceSearch:(value,all)=>search.replace(editor,value,all),
        getCommandState,
        execute(command, value) {
            if (!getCommandState(command).enabled) return false;
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
        destroy() { listeners.clear(); editor.off('transaction', transaction); editor.destroy(); }
    };
}
