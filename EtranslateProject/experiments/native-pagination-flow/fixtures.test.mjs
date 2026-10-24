import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { geometry, pageCount, pageAt } from './geometry.js';
import { fixture, ids } from './samples.js';
import { isRichDocument } from '../../src/Web/ETranslate.Web/ClientApp/document-schema.js';
test('every synthetic native-flow input uses the unchanged strict production vocabulary', () => {
    for (const id of ids) assert.equal(isRichDocument(fixture(id).body), true, id);
    assert.throws(() => fixture('../../appsettings.json'));
});
test('geometry includes the renderer footer reserve and header/footer measurements', () => {
    const basic = geometry(null); assert.ok(Math.abs(basic.contentHeight - 244 * 96 / 25.4) < 0.001);
    const headed = geometry(null, 20.2, 10.1); assert.equal(headed.headerReserve, 25); assert.equal(headed.footerReserve, 15);
    assert.equal(geometry(null, 46, 0), null); assert.equal(geometry('{}'), null);
    const last = fixture('letterhead-last'); assert.equal(geometry(JSON.stringify(last.layout), 20.2, 10.1).contentHeight, headed.contentHeight);
});
test('native column counting is bounded without adding page nodes to the document', () => {
    const g = geometry(null); assert.equal(pageCount(g.contentWidth, g), 1);
    assert.equal(pageCount(g.contentWidth + g.pitch, g), 2);
    assert.equal(pageCount(g.contentWidth + 50 * g.pitch, g), null);
    assert.equal(pageCount(Infinity, g), null); assert.equal(pageAt(g.pitch + 20, g), 2);
});
test('the lab reuses the adapter, has no paginator, network client or storage API and is absent from production bundle inputs', async () => {
    const code = await readFile(new URL('lab.js', import.meta.url), 'utf8');
    assert.doesNotMatch(code, /@tiptap|\.chain\(|\.getJSON\(|innerHTML|fetch\(|localStorage|sessionStorage/);
    const inputs = await readFile(new URL('generated/bundle-inputs.json', import.meta.url), 'utf8');
    assert.doesNotMatch(inputs, /tiptap-pagination-plus|tiptap-table-plus|canvas-editor|syncfusion/);
    const host = await readFile(new URL('../../src/Web/ETranslate.Web/ClientApp/workspace.js', import.meta.url), 'utf8');
    assert.doesNotMatch(host, /native-pagination-flow/);
});
