import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {getSchema} from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import {TableKit,Table,TableCell,TableHeader} from '@tiptap/extension-table';
import {EditorState,TextSelection,AllSelection} from '@tiptap/pm/state';
import {CellSelection,addRowAfter,deleteColumn} from '@tiptap/pm/tables';
import {history,closeHistory,undo,redo} from '@tiptap/pm/history';
import {isRichDocument} from '../document-schema.js';
import {columnCount} from '../table-spans.js';
import {horizontalCells,changeHorizontalCells} from '../editors/tiptap-horizontal-merge.js';
import {setTableColumnWidths,resetChangedColumnCounts} from '../editors/tiptap-table-widths.js';
import {toggleFirstTableRow} from '../editors/tiptap-table-header.js';
import {cellStyleFields} from '../cell-style.js';
import {createEditorSession} from '../editors/editor-session.js';
import {richDocumentV7,richDocumentV8} from '../editors/document-contract.js';
const samples=JSON.parse(await readFile(new URL('./horizontal-span-cases.json',import.meta.url),'utf8'));
for(const sample of samples)test(`v8 horizontal grid: ${sample.name}`,()=>{
 const table={type:'table',...(sample.widths?{attrs:{columnWidths:sample.widths}}:{}),content:sample.rows.map(row=>({type:'tableRow',content:row.map(span=>({type:'tableCell',attrs:{colspan:span,rowspan:sample.rowspan??1,colwidth:sample.colwidth??null},content:[{type:'paragraph'}]}))}))};
 const doc={type:'doc',content:[table]};assert.equal(isRichDocument(doc,8),sample.valid);
 if(sample.rows.flat().some(span=>span!==1))for(let version=1;version<8;version++)assert.equal(isRichDocument(doc,version),false);
});
const styled=Base=>Base.extend({addAttributes(){return {...this.parent?.(),...Object.fromEntries(cellStyleFields.map(field=>[field,{default:null}]))};}});
const schema=getSchema([StarterKit,TableKit.configure({table:false,tableCell:false,tableHeader:false}),Table.extend({addAttributes(){return {columnWidths:{default:null}};}}),styled(TableCell),styled(TableHeader)]);
function fixture(){
 const p=text=>({type:'paragraph',content:[{type:'text',text,marks:[{type:'bold'}]}]});
 const cell=(r,c)=>({type:r===0?'tableHeader':'tableCell',attrs:{backgroundColor:'#bfdbfe',borderWidthPt:1.5},content:[p(`CELL_${r}_${c}`),p(`Extra_${r}_${c}`)]});
 const doc=schema.nodeFromJSON({type:'doc',content:[p('BEFORE'),{type:'table',attrs:{columnWidths:[25,50,25]},content:[0,1,2].map(r=>({type:'tableRow',content:[0,1,2].map(c=>cell(r,c))}))},p('AFTER')]});
 const pos=text=>{let value;doc.descendants((node,offset)=>{if(node.isText&&node.text===text)value=offset;});return value;};
 return {state:EditorState.create({doc,selection:TextSelection.create(doc,pos('CELL_0_0')),plugins:[history()]}),pos};
}
test('next-cell merge preserves all blocks, paint, headings, other rows and logical width; split keeps text first',()=>{
 let {state}=fixture();const before=state.doc,table=before.child(1),first=table.firstChild;
 state=state.apply(changeHorizontalCells(state,'mergeNextCell'));
 const row=state.doc.child(1).firstChild;assert.equal(row.childCount,2);assert.equal(row.firstChild.attrs.colspan,2);assert.equal(columnCount(row.content.content),3);
 assert.deepEqual(state.doc.child(1).attrs.columnWidths,[25,50,25]);assert.ok(state.doc.child(1).child(1).eq(table.child(1)));
 assert.equal(row.firstChild.textContent,first.child(0).textContent+first.child(1).textContent);assert.equal(row.firstChild.attrs.backgroundColor,'#bfdbfe');
 assert.equal(isRichDocument(state.doc.toJSON(),8),true);assert.equal(isRichDocument(state.doc.toJSON(),7),false);
 state=state.apply(changeHorizontalCells(state,'mergeNextCell'));assert.equal(state.doc.child(1).firstChild.firstChild.attrs.colspan,3);
 const merged=state.doc.child(1).firstChild.firstChild;
 state=state.apply(changeHorizontalCells(state,'splitCellInRow'));const split=state.doc.child(1).firstChild;
 assert.equal(split.childCount,3);assert.ok(split.firstChild.content.eq(merged.content));assert.equal(split.child(1).textContent,'');assert.equal(split.child(2).textContent,'');
 assert.equal(split.content.content.every(node=>node.attrs.backgroundColor==='#bfdbfe'&&node.attrs.colspan===1),true);
});
test('a selected same-row range or cell rectangle merges; cross-row, whole-document and incompatible paint/type do not',()=>{
 const f=fixture(),cell=text=>f.state.doc.resolve(f.pos(text)).before(3);
 for(const selection of [TextSelection.create(f.state.doc,f.pos('CELL_1_0'),f.pos('CELL_1_1')+3),CellSelection.create(f.state.doc,cell('CELL_1_0'),cell('CELL_1_1'))]){
  const state=f.state.apply(f.state.tr.setSelection(selection));assert.equal(horizontalCells(state,'mergeCellsInRow').length,2);assert.ok(changeHorizontalCells(state,'mergeCellsInRow'));
 }
 for(const selection of [new AllSelection(f.state.doc),CellSelection.create(f.state.doc,cell('CELL_0_0'),cell('CELL_1_1')),TextSelection.create(f.state.doc,f.pos('BEFORE'))]){
  const state=f.state.apply(f.state.tr.setSelection(selection));assert.equal(changeHorizontalCells(state,'mergeCellsInRow'),null);
 }
 let state=f.state;const nextPos=cell('CELL_0_1'),next=state.doc.nodeAt(nextPos);
 state=state.apply(state.tr.setNodeMarkup(nextPos,undefined,{...next.attrs,backgroundColor:'#fef08a'}));assert.equal(changeHorizontalCells(state,'mergeNextCell'),null);
 state=f.state.apply(f.state.tr.setNodeMarkup(nextPos,schema.nodes.tableCell,next.attrs));assert.equal(changeHorizontalCells(state,'mergeNextCell'),null);
});
test('merged headings retain logical width settings and row/column operations remain rectangular',()=>{
 let {state,pos}=fixture();state=state.apply(changeHorizontalCells(state,'mergeNextCell'));state=state.apply(changeHorizontalCells(state,'mergeNextCell'));
 state=state.apply(setTableColumnWidths(state,[50,30,20]));assert.deepEqual(state.doc.child(1).attrs.columnWidths,[50,30,20]);
 state=state.apply(toggleFirstTableRow(state));assert.equal(state.doc.child(1).firstChild.firstChild.type.name,'tableCell');assert.equal(state.doc.child(1).firstChild.firstChild.attrs.colspan,3);
 assert.equal(addRowAfter(state,tr=>{state=state.apply(tr);}),true);assert.equal(isRichDocument(state.doc.toJSON(),8),true);
 const afterTable=state.doc.child(1);let target;state.doc.descendants((node,offset)=>{if(node.isText&&node.text==='CELL_2_0')target=offset;});
 state=state.apply(state.tr.setSelection(TextSelection.create(state.doc,target)));
 assert.equal(deleteColumn(state,tr=>{resetChangedColumnCounts({tr});state=state.apply(tr);}),true);assert.equal(isRichDocument(state.doc.toJSON(),8),true);assert.equal(state.doc.child(1).attrs.columnWidths,null);
});
test('merge and split are lossless history steps; undo recovers the original cell distribution',()=>{
 let {state}=fixture();const initial=state.doc;const dispatch=tr=>{state=state.apply(tr);};
 state=state.apply(changeHorizontalCells(state,'mergeNextCell'));state=state.apply(closeHistory(state.tr));const merged=state.doc;
 state=state.apply(changeHorizontalCells(state,'splitCellInRow'));state=state.apply(closeHistory(state.tr));
 assert.equal(undo(state,dispatch),true);assert.ok(state.doc.eq(merged));assert.equal(undo(state,dispatch),true);assert.ok(state.doc.eq(initial));
 assert.equal(redo(state,dispatch),true);assert.ok(state.doc.eq(merged));
});
test('replacement providers must opt into schema 8; session and readonly locks gate every merge action',()=>{
 for(const format of [richDocumentV7,richDocumentV8]){
  let calls=0;const source=JSON.stringify({type:'doc',content:[{type:'paragraph'}]});const provider={id:'replacement',format,capabilities:{},create:()=>({getDocument:()=>JSON.parse(source),getCommandState:()=>({enabled:true}),execute:()=>{calls++;return true;},setEditable(){},subscribe:()=>()=>{},destroy(){}})};
  const session=createEditorSession({provider,contentJson:source,editable:true});
  for(const command of ['mergeNextCell','mergeCellsInRow','splitCellInRow'])assert.equal(session.execute(command),format.version===8);
  session.setEditable(false);assert.equal(session.execute('mergeNextCell'),false);session.destroy();assert.equal(calls,format.version===8?3:0);
 }
});
