import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {validTypography} from '../typography.js';
import {isRichDocument} from '../document-schema.js';
import {richDocumentV1,richDocumentV2,readDocumentJson,writeDocumentJson} from '../editors/document-contract.js';
import {createEditorSession} from '../editors/editor-session.js';

const cases=JSON.parse(readFileSync(new URL('typography-cases.json',import.meta.url)));
const body=attrs=>({type:'doc',content:[{type:'paragraph',content:[{type:'text',text:'İstanbul ترجمه',marks:[{type:'bold'},{type:'typography',attrs}]}]}]});
for(const fixture of cases)test('v2 typography: '+fixture.name,()=>{
    assert.equal(validTypography(fixture.attrs),fixture.valid);
    assert.equal(isRichDocument(body(fixture.attrs),2),fixture.valid);
    assert.equal(isRichDocument(body(fixture.attrs),1),false);
});
test('v2 round trips while v1 rejects new marks; unknown CSS cannot be flattened',()=>{
    const document=body({fontFamily:'serif',fontSize:18,color:'#b91c1c',highlight:'#fef08a'}),source=JSON.stringify(document);
    assert.equal(writeDocumentJson(readDocumentJson(source,richDocumentV2),richDocumentV2),source);
    assert.throws(()=>readDocumentJson(source,richDocumentV1));
    document.content[0].content[0].marks.push({type:'typography',attrs:{fontSize:12}});
    assert.equal(isRichDocument(document,2),false);
});
test('parameter commands preserve typed values and fail closed before dispatch on invalid arguments and read-only sessions',()=>{
    const calls=[];let edit=true;
    const provider={id:'replacement-test',format:richDocumentV2,create:()=>({
        getDocument:()=>({type:'doc',content:[]}),getCommandState:()=>({enabled:edit,active:false,value:18,mixed:true}),
        execute:(...args)=>{calls.push(args);return true;},setEditable:value=>{edit=value;},subscribe:()=>()=>{},destroy:()=>{}
    })};
    const session=createEditorSession({provider,contentJson:'{"type":"doc","content":[]}',editable:true});
    assert.equal(session.execute('fontSize',24),true);assert.deepEqual(calls,[['fontSize',24]]);
    assert.deepEqual(session.getCommandState('fontSize'),{enabled:true,active:false,value:18,mixed:true});
    for(const [command,value] of [['fontSize','24'],['fontFamily','remote'],['textColor','red'],['highlight','#ff0']])assert.equal(session.execute(command,value),false);
    session.setEditable(false);assert.equal(session.execute('fontFamily','serif'),false);
    assert.equal(calls.length,1);session.destroy();
});
