import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { geometry } from '../pagination-geometry.js';
import { supportedPaginationRegion } from '../editor-pagination.js';
test('production page geometry reserves the same measured template and number footer space',()=>{
    assert.ok(Math.abs(geometry(null).contentHeight-244*96/25.4)<.001);
    assert.equal(geometry(null,20.2,10.1).headerReserve,25);assert.equal(geometry(null,20.2,10.1).footerReserve,15);
    assert.equal(geometry('{}'),null);assert.equal(geometry(null,46,0),null);assert.equal(geometry(null,0,36),null);
});
test('template validation preserves supported alignment/banner while rejecting unknown data',()=>{
    assert.equal(supportedPaginationRegion('','header'),true);
    const legacy={type:'doc',content:[{type:'paragraph',content:[{type:'text',text:'Eski üstbilgi'}]}]};
    assert.equal(supportedPaginationRegion(JSON.stringify(legacy),'header'),true);
    assert.equal(supportedPaginationRegion(JSON.stringify(legacy),'footer'),true);
    legacy.content[0].attrs={textAlign:'right'};
    assert.equal(supportedPaginationRegion(JSON.stringify(legacy),'header'),false);
    assert.equal(supportedPaginationRegion(JSON.stringify({type:'header',content:[{type:'paragraph',attrs:{textAlign:'right'},content:[{type:'text',text:'متن İstanbul'}]},
        {type:'image',attrs:{assetId:'11111111-1111-1111-1111-111111111111',align:'center',layout:'banner'}}]}),'header'),true);
    for(const value of ['null','[]','{}','{"type":"footer","content":[]}',
        '{"type":"header","content":[],"unknown":"keep"}',
        '{"type":"header","content":[{"type":"paragraph","attrs":{"textAlign":"justify"}}]}',
        '{"type":"header","content":[{"type":"image","attrs":{"src":"https://example.org"}}]}'])
        assert.equal(supportedPaginationRegion(value,'header'),false,value);
});
test('packaged server planner matches the shared source and needs no runtime network or imports',()=>{
    const source=readFileSync(new URL('../../../../Shared/DocumentLayout/table-row-plan.js',import.meta.url),'utf8').replace(/\r\n/g,'\n');
    const bundle=readFileSync(new URL('../../../../Services/Documents/ETranslate.Documents.Api/Rendering/Layout/table-row-plan.bundle.js',import.meta.url),'utf8');
    assert.ok(bundle.includes('source-sha256:'+createHash('sha256').update(source).digest('hex')));
    assert.doesNotMatch(bundle,/fetch\(|XMLHttpRequest|import\s|@tiptap/);
});
test('page view is opt-in and does not submit preferences, HTML or page plans',()=>{
    const view=readFileSync(new URL('../../Views/Workspace/Editor.cshtml',import.meta.url),'utf8');
    assert.ok(view.indexOf('id="editor-pages-toggle"')<view.indexOf('id="draft-form"'));
    const code=readFileSync(new URL('../editor-pagination.js',import.meta.url),'utf8');
    assert.doesNotMatch(code,/\.setEditable\(|\.execute\(|@tiptap|\.chain\(|localStorage|fetch\(|innerHTML/);
});
