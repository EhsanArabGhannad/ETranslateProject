import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { geometry, pageCount, pageAt } from './geometry.js';
import { fixture, ids } from './samples.js';
import { isRichDocument } from '../../src/Web/ETranslate.Web/ClientApp/document-schema.js';
import { buildRowPlan, rowPlanVersion } from './table-row-plan.js';

test('row plans use measured native boundaries, not row-count estimates or document page nodes', () => {
    const rows = pages => pages.map((page,index) => ({index,page,fragments:1}));
    const plan = buildRowPlan([{hasHeader:false,rows:rows([1,1,2,2,3])},{hasHeader:true,rows:rows([3,3,4])}],4,'test');
    assert.equal(plan.version,rowPlanVersion);
    assert.deepEqual(plan.tables.map(table=>table.breakBeforeRows),[[2,4],[2]]);
    assert.equal(plan.tables[1].rowCount,3);
    assert.throws(()=>buildRowPlan([{hasHeader:false,rows:[{index:0,page:1,fragments:2}]}],2,'test'));
    assert.throws(()=>buildRowPlan([{hasHeader:false,rows:rows([1,3])}],3,'test'));
    assert.throws(()=>buildRowPlan([{hasHeader:false,rows:rows([2,1])}],2,'test'));
    assert.throws(()=>buildRowPlan([],51,'test'));
    assert.deepEqual(buildRowPlan([],1,'test').tables,[]);
});
test('every synthetic native-flow input uses the unchanged strict production vocabulary', () => {
    for (const id of ids) assert.equal(isRichDocument(fixture(id).body), true, id);
    assert.throws(() => fixture('../../appsettings.json'));
});
test('every fixture has a selectable Turkish lab option and unique trace markers', async () => {
    const html = await readFile(new URL('lab.html',import.meta.url),'utf8');
    const options = [...html.matchAll(/<option value="([^"]+)">/g)].map(match=>match[1]).filter(id=>!['fit','75','100','125'].includes(id));
    assert.deepEqual(options,ids);
    for (const id of ids) {
        const markers = JSON.stringify(fixture(id).body).match(/MARK_\d{3}/g) ?? [];
        assert.equal(new Set(markers).size,markers.length,id);
    }
});
test('geometry includes the renderer footer reserve and header/footer measurements', () => {
    const basic = geometry(null); assert.ok(Math.abs(basic.contentHeight - 244 * 96 / 25.4) < 0.001);
    const headed = geometry(null, 20.2, 10.1); assert.equal(headed.headerReserve, 25); assert.equal(headed.footerReserve, 15);
    assert.equal(geometry(null, 46, 0), null); assert.equal(geometry('{}'), null);
    const last = fixture('letterhead-last'); assert.equal(geometry(JSON.stringify(last.layout), 20.2, 10.1).contentHeight, headed.contentHeight);
});
test('heading variants preserve the original document and identify only an all-header first row', () => {
    const row = id => fixture(id).body.content.find(node => node.type === 'table').content[0];
    const header = id => row(id).content.every(cell => cell.type === 'tableHeader');
    assert.equal(header('long-table'), true); assert.equal(header('long-table-multiline'), true);
    assert.equal(header('table-no-header'), false); assert.equal(header('table-mixed-header'), false);
    assert.equal(row('table-mixed-header').content[0].type, 'tableHeader');
    assert.equal(row('table-no-header').content[0].type, 'tableCell');
    assert.equal(row('long-table').content[0].content[0].content[0].text, 'Sıra');
    assert.equal(row('long-table-multiline').content[1].content[0].attrs.dir, 'rtl');
});
test('repeating headings are CSS-only and scoped to experimental column mode, not measuring or saved documents', async () => {
    const css = await readFile(new URL('lab.css', import.meta.url), 'utf8');
    assert.match(css, /#world\[data-mode=columns\] #editor .*:not\(:has\(>tbody>tr:first-child>td\)\)>tbody\{display:contents\}/);
    assert.match(css, /tr:first-child:has\(>th\):not\(:has\(>td\)\)\{display:table-header-group;break-inside:avoid\}/);
    const production = await readFile(new URL('../../src/Web/ETranslate.Web/wwwroot/css/site.css', import.meta.url), 'utf8');
    assert.doesNotMatch(production, /display:table-header-group|display:contents/);
});
test('native column counting is bounded without adding page nodes to the document', () => {
    const g = geometry(null); assert.equal(pageCount(g.contentWidth, g), 1);
    assert.equal(pageCount(g.contentWidth + g.pitch, g), 2);
    assert.equal(pageCount(g.contentWidth + 50 * g.pitch, g), null);
    assert.equal(pageCount(Infinity, g), null); assert.equal(pageAt(g.pitch + 20, g), 2);
});
test('the lab reuses the adapter, has no third-party paginator, network client or storage API and is absent from production bundle inputs', async () => {
    const code = await readFile(new URL('lab.js', import.meta.url), 'utf8');
    assert.doesNotMatch(code, /@tiptap|\.chain\(|\.getJSON\(|innerHTML|fetch\(|localStorage|sessionStorage/);
    const inputs = await readFile(new URL('generated/bundle-inputs.json', import.meta.url), 'utf8');
    assert.doesNotMatch(inputs, /tiptap-pagination-plus|tiptap-table-plus|canvas-editor|syncfusion/);
    const host = await readFile(new URL('../../src/Web/ETranslate.Web/ClientApp/workspace.js', import.meta.url), 'utf8');
    assert.doesNotMatch(host, /native-pagination-flow/);
});
