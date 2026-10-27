import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {isRichDocument} from '../document-schema.js';
import {validParagraphValue,applyParagraph} from '../paragraph.js';
import {readDocumentJson,writeDocumentJson,richDocumentV1,richDocumentV2,richDocumentV3} from '../editors/document-contract.js';
import {createEditorSession} from '../editors/editor-session.js';
const samples=JSON.parse(readFileSync(new URL('paragraph-cases.json',import.meta.url)));
for(const fixture of samples)test('v3 paragraph: '+fixture.name,()=>{
    const body={type:'doc',content:[{type:'paragraph',attrs:{dir:'rtl',...fixture.attrs},content:[{type:'text',text:'İstanbul ترجمه'}]}]};
    assert.equal(isRichDocument(body,3),fixture.valid);
    assert.equal(Object.entries(fixture.attrs).every(([field,value])=>validParagraphValue(field,value)),fixture.valid);
    assert.equal(isRichDocument(body,2),false);assert.equal(isRichDocument(body,1),false);
    if(fixture.valid)assert.deepEqual(readDocumentJson(writeDocumentJson(body,richDocumentV3),richDocumentV3),body);
    for(const format of [richDocumentV1,richDocumentV2])assert.throws(()=>readDocumentJson(JSON.stringify(body),format));
});
test('paragraph CSS uses logical start and unitless spacing without changing text direction',()=>{
    const element={style:{},dir:'rtl'};applyParagraph(element,{textAlign:'left',lineHeight:2,indentLevel:3});
    assert.deepEqual(element.style,{textAlign:'left',lineHeight:'2',minHeight:'2em',marginInlineStart:'15mm'});assert.equal(element.dir,'rtl');
});
test('a v2 replacement provider cannot dispatch paragraph commands; v3 validates arguments and retains locks',()=>{
    const calls=[];const create=()=>({getDocument:()=>({type:'doc',content:[]}),getCommandState:()=>({enabled:true,value:2,mixed:true}),execute:(...args)=>{calls.push(args);return true;},setEditable:()=>{},subscribe:()=>()=>{},destroy:()=>{}});
    const older=createEditorSession({provider:{id:'previous',format:richDocumentV2,create},contentJson:'{"type":"doc","content":[]}',editable:true});
    assert.equal(older.execute('textAlign','right'),false);older.destroy();
    const session=createEditorSession({provider:{id:'replacement',format:richDocumentV3,create},contentJson:'{"type":"doc","content":[]}',editable:true});
    assert.equal(session.execute('lineHeight',1.5),true);assert.deepEqual(calls,[['lineHeight',1.5]]);
    assert.equal(session.execute('lineHeight','1.5'),false);assert.equal(session.execute('indentLevel',9),false);
    session.setEditable(false);assert.equal(session.execute('increaseIndent'),false);session.destroy();
});
