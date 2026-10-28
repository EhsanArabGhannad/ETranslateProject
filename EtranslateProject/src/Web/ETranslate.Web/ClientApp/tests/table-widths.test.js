import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { validColumnWidths, equalColumnPercentages } from '../table-widths.js';
import { isRichDocument } from '../document-schema.js';
import { getSchema } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TableKit, Table } from '@tiptap/extension-table';
import { EditorState, TextSelection } from '@tiptap/pm/state';
import { addColumnAfter, deleteColumn, addRowAfter } from '@tiptap/pm/tables';
import { setTableColumnWidths, resetChangedColumnCounts } from '../editors/tiptap-table-widths.js';
import { createEditorSession } from '../editors/editor-session.js';
import { richDocumentV5, richDocumentV6 } from '../editors/document-contract.js';

const body = (columns, widths) => ({type:'doc',content:[{type:'table',attrs:{columnWidths:widths},content:[0,1].map(r=>({type:'tableRow',content:Array.from({length:columns},(_,c)=>({type:r===0?'tableHeader':'tableCell',content:[{type:'paragraph',content:[{type:'text',text:`CELL_${r}_${c}`,marks:[{type:'bold'}]}]}]}))}))}]});
const cases = JSON.parse(await readFile(new URL('./table-width-cases.json',import.meta.url),'utf8'));
for (const sample of cases) test(`v6 column policy: ${sample.name}`, () => {
    assert.equal(validColumnWidths(sample.widths,sample.columns),sample.valid);
    assert.equal(isRichDocument(body(sample.columns,sample.widths),6),sample.valid);
    for(let version=1;version<6;version++) assert.equal(isRichDocument(body(sample.columns,sample.widths),version),false);
});
test('default equal distribution is bounded and exactly sums to 100 for all supported column counts',()=>{
    for(let count=1;count<=12;count++)assert.equal(validColumnWidths(equalColumnPercentages(count),count),true);
    const sample=body(3,null);delete sample.content[0].attrs;
    for(let version=1;version<=6;version++)assert.equal(isRichDocument(sample,version),true);
});
const schema=getSchema([StarterKit,TableKit.configure({table:false}),Table.extend({addAttributes(){return {...this.parent?.(),columnWidths:{default:null}};}})]);
function state(){const doc=schema.nodeFromJSON(body(3,null));return EditorState.create({doc,selection:TextSelection.create(doc,4)});}
test('setting and resetting table percentages preserve cells, marks and selection, and reject no-op or bad writes',()=>{
    let current=state();const original=current.doc,selection=current.selection;
    assert.equal(setTableColumnWidths(current,[30,70]),null);assert.equal(setTableColumnWidths(current,null),null);
    current=current.apply(setTableColumnWidths(current,[25,50,25]));
    assert.ok(current.doc.firstChild.content.eq(original.firstChild.content));assert.ok(current.selection.eq(selection));
    assert.deepEqual(current.doc.firstChild.attrs.columnWidths,[25,50,25]);
    assert.equal(setTableColumnWidths(current,[25,50,25]),null);
    current=current.apply(setTableColumnWidths(current,null));assert.ok(current.doc.eq(original));
});
test('column edits reset unequal widths atomically while row edits keep the saved percentages',()=>{
    for(const command of [addColumnAfter,deleteColumn]){
        let current=state();current=current.apply(setTableColumnWidths(current,[25,50,25]));
        assert.equal(command(current,tr=>{resetChangedColumnCounts({tr});current=current.apply(tr);}),true);
        assert.equal(current.doc.firstChild.attrs.columnWidths,null);assert.equal(isRichDocument(current.doc.toJSON(),6),true);
    }
    let current=state();current=current.apply(setTableColumnWidths(current,[25,50,25]));
    assert.equal(addRowAfter(current,tr=>{current=current.apply(tr);}),true);assert.deepEqual(current.doc.firstChild.attrs.columnWidths,[25,50,25]);
});
test('the session gates the new command by engine version, validates the selected table count and copies values',()=>{
    for(const format of [richDocumentV5,richDocumentV6]){
        const calls=[],source=JSON.stringify({type:'doc',content:[{type:'paragraph'}]});
        const provider={id:'replacement',format,capabilities:{},create:()=>({getDocument:()=>JSON.parse(source),getCommandState:()=>({enabled:true,columns:3,value:null,context:'opaque'}),execute:(command,value)=>{calls.push(value);return true;},setEditable(){},subscribe:()=>()=>{},destroy(){}})};
        const session=createEditorSession({provider,contentJson:source,editable:true});const widths=[25,50,25];
        assert.equal(session.execute('tableColumnWidths',widths),format.version===6);
        if(format.version===6){assert.deepEqual(calls[0],widths);assert.notEqual(calls[0],widths);}
        for(const invalid of [[50,50],[33,33,33],undefined,'25,50,25'])assert.equal(session.execute('tableColumnWidths',invalid),false);
        session.setEditable(false);assert.equal(session.execute('tableColumnWidths',null),false);session.destroy();
    }
});
