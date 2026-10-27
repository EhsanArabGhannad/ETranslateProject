import test from 'node:test';
import assert from 'node:assert/strict';
import {bodyTextBlocks,documentStatistics,literalMatches,searchLimit,validSearch,validReplacement} from '../document-text-tools.js';
import {createEditorSession} from '../editors/editor-session.js';
import {richDocumentV4} from '../editors/document-contract.js';
const options=(query,extra={})=>({query,caseSensitive:false,wholeWord:false,...extra});
const matches=(text,query,extra)=>literalMatches([{text,from:10}],options(query,extra));
test('literal search uses Turkish case pairs and original UTF16 positions',()=>{
    assert.deepEqual(matches('I ı İ i','ı').matches,[{from:10,to:11},{from:12,to:13}]);
    assert.deepEqual(matches('I ı İ i','i').matches,[{from:14,to:15},{from:16,to:17}]);
    assert.equal(matches('I ı İ i','i',{caseSensitive:true}).matches.length,1);
    assert.deepEqual(matches('😀x😀','😀').matches,[{from:10,to:12},{from:13,to:15}]);
});
test('literal metacharacters and dollar replacement text are not patterns',()=>{
    assert.deepEqual(matches('a.* a.*','a.*').matches,[{from:10,to:13},{from:14,to:17}]);
    assert.equal(validReplacement('$& <script>'),true);
    assert.equal(matches('aaa','aa').matches.length,1);
});
test('whole words use Unicode word boundaries including Persian joiners',()=>{
    assert.equal(matches('test testing test','test',{wholeWord:true}).matches.length,2);
    assert.equal(matches('می‌روم می‌روم','می‌روم',{wholeWord:true}).matches.length,2);
    assert.equal(matches('می‌روم','می',{wholeWord:true}).matches.length,0);
});
test('search never crosses supplied hard-break, block or cell boundaries',()=>{
    assert.equal(literalMatches([{text:'ab',from:1},{text:'cd',from:4}],options('bc')).matches.length,0);
    assert.equal(literalMatches([{text:'ab',from:1},{text:'ab',from:8}],options('ab')).matches.length,2);
});
test('large match sets are explicitly truncated at a fixed decoration limit',()=>{
    const result=matches('x '.repeat(searchLimit+1),'x');assert.equal(result.matches.length,searchLimit);assert.equal(result.truncated,true);
    assert.equal(matches('x '.repeat(searchLimit),'x').truncated,false);
});
test('invalid query and replacement arguments are rejected at the contract',()=>{
    for(const value of [null,{},options('x'.repeat(201)),options('a\nb'),options('\ud800'),{...options('x'),regex:true},options('x',{wholeWord:1})])assert.equal(validSearch(value),false);
    for(const value of [null,'x'.repeat(2001),'a\nb','\udc00'])assert.equal(validReplacement(value),false);
    assert.equal(validReplacement(''),true);assert.equal(validSearch(options('')),true);
});
test('body counts respect marks, grapheme clusters and block separators without counting generated text',()=>{
    const body={type:'doc',content:[{type:'heading',content:[{type:'text',text:'İstanbul ',marks:[{type:'bold'}]},{type:'text',text:'ترجمه'}]},
        {type:'table',content:[{type:'tableRow',content:[{type:'tableCell',content:[{type:'paragraph',content:[{type:'text',text:'e\u0301 👨‍👩‍👧‍👦'},{type:'hardBreak'},{type:'text',text:'42'}]}]}]}]},
        {type:'image',attrs:{assetId:'ignored'}}]};
    assert.deepEqual(bodyTextBlocks(body),['İstanbul ترجمه','e\u0301 👨‍👩‍👧‍👦\n42']);
    assert.deepEqual(documentStatistics(body),{words:4,characters:19,withoutSpaces:17});
    assert.deepEqual(documentStatistics({type:'doc',content:[]}),{words:0,characters:0,withoutSpaces:0});
});
test('search can navigate a readonly session but replacement cannot unlock it',()=>{
    const calls=[];const provider={id:'replacement',format:richDocumentV4,create:()=>({getDocument:()=>({type:'doc',content:[]}),subscribe:()=>()=>{},setEditable:()=>{},destroy:()=>{},getSearchState:()=>({supported:true,total:1,index:1}),setSearch:()=>true,findMatch:()=>true,replaceSearch:()=>{calls.push('replace');return true;}})};
    for(const editable of [false,true]){const session=createEditorSession({provider,editable,contentJson:'{"type":"doc","content":[]}'});
        assert.equal(session.setSearch(options('x')),true);assert.equal(session.findMatch(),true);assert.equal(session.replaceSearch('y'),editable);
        session.setEditable(false);session.setEditable(true);assert.equal(session.getSearchState().canReplace,editable);
        session.setEditable(false);assert.equal(session.replaceSearch('z',true),false);session.destroy();assert.equal(session.findMatch(),false);
    }assert.deepEqual(calls,['replace']);
});
