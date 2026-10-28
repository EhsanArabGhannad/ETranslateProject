import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {isRichDocument} from '../document-schema.js';
import {validParagraphValue,applyParagraph} from '../paragraph.js';
import {richDocumentV4,richDocumentV5,readDocumentJson,writeDocumentJson} from '../editors/document-contract.js';
import {createEditorSession} from '../editors/editor-session.js';
for(const sample of JSON.parse(readFileSync(new URL('paragraph-spacing-cases.json',import.meta.url))))test('v5 paragraph spacing: '+sample.name,()=>{
    const body={type:'doc',content:[{type:'paragraph',attrs:{dir:'rtl',...sample.attrs},content:[{type:'text',text:'ترجمه İstanbul'}]}]};
    assert.equal(isRichDocument(body,5),sample.valid);assert.equal(Object.entries(sample.attrs).every(([field,value])=>validParagraphValue(field,value,5)),sample.valid);
    for(const version of [1,2,3,4])assert.equal(isRichDocument(body,version),false);
    if(sample.valid)assert.deepEqual(readDocumentJson(writeDocumentJson(body,richDocumentV5),richDocumentV5),body);
});
test('paragraph spacing is bounded CSS with independent direction and first-line indentation',()=>{
    const element={dir:'rtl',style:{}};applyParagraph(element,{spaceBeforePt:12,spaceAfterPt:0,firstLineIndentMm:12.5,indentLevel:2});
    assert.deepEqual(element.style,{marginInlineStart:'10mm',marginTop:'12pt',marginBottom:'0pt',textIndent:'12.5mm'});assert.equal(element.dir,'rtl');
});
test('v4 replacement provider cannot issue new commands; v5 retains typed arguments and locks',()=>{
    const calls=[];const create=()=>({getDocument:()=>({type:'doc',content:[]}),getCommandState:()=>({enabled:true}),execute:(...args)=>{calls.push(args);return true;},subscribe:()=>()=>{},setEditable:()=>{},destroy:()=>{}});
    for(const format of [richDocumentV4,richDocumentV5]){const session=createEditorSession({provider:{id:'replacement',format,create},editable:true,contentJson:'{"type":"doc","content":[]}'});
        assert.equal(session.execute('firstLineIndentMm',12.5),format.version===5);assert.equal(session.execute('spaceAfterPt','12'),false);
        session.setEditable(false);assert.equal(session.execute('spaceBeforePt',6),false);session.destroy();}
    assert.deepEqual(calls,[['firstLineIndentMm',12.5]]);
});
