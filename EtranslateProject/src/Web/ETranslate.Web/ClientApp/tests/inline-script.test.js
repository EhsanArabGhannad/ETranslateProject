import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {isRichDocument} from '../document-schema.js';
import {applyInlineScript,orderedInlineMarks} from '../inline-script.js';
import {createEditorSession} from '../editors/editor-session.js';
import {richDocumentV3,richDocumentV4,readDocumentJson,writeDocumentJson} from '../editors/document-contract.js';

for(const fixture of JSON.parse(readFileSync(new URL('inline-script-cases.json',import.meta.url))))test('v4 inline script: '+fixture.name,()=>{
    const body={type:'doc',content:[{type:'paragraph',attrs:{dir:'rtl'},content:[{type:'text',text:'۲ İstanbul',marks:fixture.marks}]}]};
    assert.equal(isRichDocument(body,4),fixture.valid);
    for(const version of [1,2,3])assert.equal(isRichDocument(body,version),false);
    if(fixture.valid)assert.deepEqual(readDocumentJson(writeDocumentJson(body,richDocumentV4),richDocumentV4),body);
});
test('script styles are bounded and nested inside typography independently of input order',()=>{
    for(const type of ['superscript','subscript']){const element={style:{}};applyInlineScript(element,type);
        assert.equal(element.style.fontSize,'.75em');assert.equal(element.style.lineHeight,'0');assert.equal(element.style.top,type==='superscript'?'-.5em':'.25em');
        for(const marks of [[{type:'typography'},{type}],[{type},{type:'typography'}]])assert.equal(orderedInlineMarks(marks)[0].type,type);
        assert.deepEqual(orderedInlineMarks([{type:'typography'},{type},{type:'strike'}]).map(mark=>mark.type),['strike',type,'typography']);
    }
    assert.throws(()=>applyInlineScript({style:{}},'script'));
});
test('older replacement provider cannot issue script commands; current session retains locks',()=>{
    const calls=[];const create=()=>({getDocument:()=>({type:'doc',content:[]}),getCommandState:()=>({enabled:true,active:true}),execute:command=>{calls.push(command);return true;},setEditable:()=>{},subscribe:()=>()=>{},destroy:()=>{}});
    for(const format of [richDocumentV3,richDocumentV4]){const session=createEditorSession({provider:{id:'replacement',format,create},contentJson:'{"type":"doc","content":[]}',editable:true});
        assert.equal(session.execute('superscript'),format.version===4);session.setEditable(false);assert.equal(session.execute('subscript'),false);session.destroy();
    }
    assert.deepEqual(calls,['superscript']);
});
