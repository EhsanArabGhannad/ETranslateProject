import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { getSchema } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TableKit, TableCell, TableHeader, Table } from '@tiptap/extension-table';
import { EditorState, TextSelection, AllSelection } from '@tiptap/pm/state';
import { CellSelection } from '@tiptap/pm/tables';
import { cellStyleFields, validCellStyleValue, validCellStyleChange } from '../cell-style.js';
import { selectedCells, cellStyleState, changeCellStyle } from '../editors/tiptap-cell-style.js';
import { isRichDocument } from '../document-schema.js';
import { createEditorSession } from '../editors/editor-session.js';
import { richDocumentV6, richDocumentV7 } from '../editors/document-contract.js';

const body = attrs => ({type:'doc',content:[{type:'table',content:[{type:'tableRow',content:[{type:'tableCell',attrs,content:[{type:'paragraph'}]}]}]}]});
const samples=JSON.parse(await readFile(new URL('./cell-style-cases.json',import.meta.url),'utf8'));
for(const sample of samples)test(`v7 cell style: ${sample.name}`,()=>{
    assert.equal(Object.entries(sample.attrs).every(([key,value])=>validCellStyleValue(key,value)),sample.valid);
    assert.equal(isRichDocument(body(sample.attrs),7),sample.valid);
    for(let version=1;version<7;version++)assert.equal(isRichDocument(body(sample.attrs),version),false);
});
const styleNode = Base => Base.extend({addAttributes(){return {...this.parent?.(),...Object.fromEntries(cellStyleFields.map(field=>[field,{default:null}]))};}});
const schema=getSchema([StarterKit,TableKit.configure({table:false,tableCell:false,tableHeader:false}),Table.extend({addAttributes(){return {columnWidths:{default:null}};}}),styleNode(TableCell),styleNode(TableHeader)]);
function fixture(){
 const p=text=>({type:'paragraph',content:[{type:'text',text,marks:[{type:'bold'}]}]});
 const table=prefix=>({type:'table',attrs:{columnWidths:[25,75]},content:[0,1,2].map(r=>({type:'tableRow',content:[0,1].map(c=>({type:r===0?'tableHeader':'tableCell',content:[p(`${prefix}_${r}_${c}`)]}))}))});
 const doc=schema.nodeFromJSON({type:'doc',content:[p('BEFORE'),table('A'),p('BETWEEN'),table('B')]});
 const pos=text=>{let result;doc.descendants((node,offset)=>{if(node.isText&&node.text===text)result=offset;});return result;};
 const state=EditorState.create({doc,selection:TextSelection.create(doc,pos('A_1_0'))});return {state,pos};
}
test('single cell changes preserve marks, selected text, widths, other cells and tables, and support field-only reset',()=>{
 let {state}=fixture();const before=state.doc,selection=state.selection;
 assert.equal(selectedCells(state).length,1);assert.equal(selectedCells(state,'table').length,6);
 state=state.apply(changeCellStyle(state,{scope:'selection',field:'backgroundColor',value:'#bfdbfe'}));
 assert.ok(state.selection.eq(selection));assert.ok(state.doc.child(1).content.child(1).firstChild.content.eq(before.child(1).content.child(1).firstChild.content));
 assert.deepEqual(state.doc.child(1).attrs.columnWidths,[25,75]);assert.ok(state.doc.child(3).eq(before.child(3)));
 assert.equal(changeCellStyle(state,{scope:'selection',field:'backgroundColor',value:'#bfdbfe'}),null);
 assert.equal(cellStyleState(state,'table').mixed.backgroundColor,true);
 state=state.apply(changeCellStyle(state,{scope:'selection',field:'reset',value:null}));assert.ok(state.doc.eq(before));
});
test('cell rectangles and text ranges apply to each intersected cell once; whole-table and cross-table rules are explicit',()=>{
 const f=fixture();let state=f.state;
 const cell=text=>state.doc.resolve(f.pos(text)).before(3);
 state=state.apply(state.tr.setSelection(CellSelection.create(state.doc,cell('A_0_0'),cell('A_1_1'))));
 assert.equal(selectedCells(state).length,4);const selection=state.selection;
 state=state.apply(changeCellStyle(state,{scope:'selection',field:'borderWidthPt',value:2}));assert.ok(state.selection.eq(selection));
 assert.equal(selectedCells(state).every(({node})=>node.attrs.borderWidthPt===2),true);
 state=state.apply(changeCellStyle(state,{scope:'table',field:'backgroundColor',value:'#fef08a'}));assert.equal(cellStyleState(state,'table').mixed.backgroundColor,false);
 assert.equal(cellStyleState(state,'table').values.backgroundColor,'#fef08a');
 state=state.apply(state.tr.setSelection(TextSelection.create(state.doc,f.pos('A_0_0'),f.pos('A_1_1'))));assert.equal(selectedCells(state).length,4);
 for(const selection of [new AllSelection(state.doc),TextSelection.create(state.doc,f.pos('A_0_0'),f.pos('B_0_0')),TextSelection.create(state.doc,f.pos('BEFORE'))]){
   const outside=state.apply(state.tr.setSelection(selection));assert.equal(selectedCells(outside,'table').length,0);assert.equal(changeCellStyle(outside,{scope:'table',field:'borderColor',value:'#000000'}),null);
 }
});
test('cell action validates exact typed input and respects provider versions and write locks',()=>{
 for(const format of [richDocumentV6,richDocumentV7]){
  let calls=0;const source=JSON.stringify({type:'doc',content:[{type:'paragraph'}]});
  const provider={id:'replacement',format,capabilities:{},create:()=>({getDocument:()=>JSON.parse(source),getCommandState:()=>({enabled:true}),execute:()=>{calls++;return true;},setEditable(){},subscribe:()=>()=>{},destroy(){}})};
  const session=createEditorSession({provider,contentJson:source,editable:true});
  const good={scope:'selection',field:'borderWidthPt',value:1.5};assert.equal(validCellStyleChange(good),true);assert.equal(session.execute('tableCellStyle',good),format.version===7);
  for(const bad of [{...good,scope:'other'},{...good,value:'1.5'},{...good,html:'x'},null,{...good,field:'reset',value:1}]){assert.equal(validCellStyleChange(bad),false);assert.equal(session.execute('tableCellStyle',bad),false);}
  session.setEditable(false);assert.equal(session.execute('tableCellStyle',good),false);session.destroy();assert.equal(calls,format.version===7?1:0);
 }
});
