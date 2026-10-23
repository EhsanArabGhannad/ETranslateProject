import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { readPaperLayout, mmToPixels, paperScale } from '../editor-view-layout.js';

const layout = { pageSize: 'A4', orientation: 'Portrait', marginsMm: { top: 25, right: 20, bottom: 20, left: 20 } };
test('paper view uses the same legacy default geometry without changing the source', () => {
    for (const source of [null, undefined, '', JSON.stringify(layout)]) {
        const result = readPaperLayout(source);
        assert.equal(result.width, 210); assert.equal(result.height, 297);
        assert.deepEqual(result.margins, layout.marginsMm); assert.ok(Object.isFrozen(result));
    }
    assert.ok(Math.abs(mmToPixels(25.4) - 96) < 0.00001);
});
test('paper dimensions honor every supported size, orientation and footer policy', () => {
    for (const [pageSize, dimensions] of Object.entries({ A4: [210, 297], A5: [148, 210], Letter: [215.9, 279.4], Legal: [215.9, 355.6] })) {
        for (const orientation of ['Portrait', 'Landscape']) {
            for (const footerPlacement of ['AllPages', 'LastPage']) {
                const value = { ...layout, pageSize, orientation, footerPlacement };
                const source = JSON.stringify(value), result = readPaperLayout(source);
                assert.deepEqual([result.width, result.height], orientation === 'Portrait' ? dimensions : [...dimensions].reverse());
                assert.equal(JSON.stringify(value), source);
            }
        }
    }
});
test('unknown or unsafe geometry falls back to the existing fluid editor, not invented paper dimensions', () => {
    for (const source of ['{', 'null', '[]', ' ', 'x'.repeat(10001),
        ...[{ ...layout, pageSize: 'toString' }, { ...layout, pageSize: ['A4'] }, { ...layout, pageSize: 'a4' }, { ...layout, orientation: 'auto' },
            { ...layout, extra: true }, { ...layout, footerPlacement: null }, { ...layout, footerPlacement: 'OddPages' },
            { ...layout, marginsMm: { ...layout.marginsMm, top: '25' } },
            { ...layout, marginsMm: { ...layout.marginsMm, right: 51 } },
            { ...layout, marginsMm: { ...layout.marginsMm, left: 4.99 } },
            { ...layout, marginsMm: { ...layout.marginsMm, extra: 20 } },
            { ...layout, pageSize: 'A5', marginsMm: { ...layout.marginsMm, left: 50, right: 50 } }].map(value => JSON.stringify(value))])
        assert.equal(readPaperLayout(source), null, source);
});
test('fit and explicit zoom scale the view, never paper width or text metrics', () => {
    assert.equal(paperScale('fit', 400, 800), 0.5);
    assert.equal(paperScale('fit', 1200, 800), 1);
    assert.equal(paperScale('fit', 1, 800), 0.05);
    for (const mode of ['75', '100', '125', '150']) assert.equal(paperScale(mode, 400, 800), Number(mode) / 100);
    for (const mode of ['url(x)', '200', null]) assert.equal(paperScale(mode, 400, 800), 1);
    for (const width of [NaN, Infinity, 0, -5]) assert.equal(paperScale('fit', width, 800), 1);
});
test('view controls stay outside the save form and host view code has no vendor or content dependencies', () => {
    const view = readFileSync(new URL('../../Views/Workspace/Editor.cshtml', import.meta.url), 'utf8');
    assert.ok(view.indexOf('id="editor-view-controls"') < view.indexOf('id="draft-form"'));
    for (const name of ['editor-view.js', 'editor-view-layout.js']) {
        const source = readFileSync(new URL(`../${name}`, import.meta.url), 'utf8');
        assert.doesNotMatch(source, /@tiptap|ProseMirror|\.chain\(|\.getJSON\(|\.setEditable\(|localStorage|fetch\(|onDocumentChange|editorContentJson/);
    }
});
