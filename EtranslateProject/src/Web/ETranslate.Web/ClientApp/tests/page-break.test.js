import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { getSchema, Node } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Paragraph from '@tiptap/extension-paragraph';
import Heading from '@tiptap/extension-heading';
import { TableKit } from '@tiptap/extension-table';
import { EditorState, TextSelection, AllSelection } from '@tiptap/pm/state';
import { history, closeHistory, undo, redo } from '@tiptap/pm/history';
import { isRichDocument } from '../document-schema.js';
import { pageStartCount } from '../page-break.js';
import { pageBreakState, pageBreakTransaction } from '../editors/tiptap-page-break.js';
import { createEditorSession } from '../editors/editor-session.js';
import { richDocumentV9, richDocumentV10 } from '../editors/document-contract.js';
import { imageTransaction } from '../editors/tiptap-images.js';

const cases = JSON.parse(await readFile(new URL('./page-break-cases.json', import.meta.url), 'utf8'));
for (const sample of cases) test(`v10 manual page start: ${sample.name}`, () => {
    assert.equal(isRichDocument(sample.doc, 10), sample.valid);
    for (let v = 1; v < 10; v++) assert.equal(isRichDocument(sample.doc, v), sample.legacyValid);
});
const withStart = Base => Base.extend({ addAttributes() { return { ...this.parent?.(), dir: { default: 'rtl' }, pageBreakBefore: { default: null, keepOnSplit: false }, textAlign: { default: 'right' }, spaceBeforePt: { default: 6 } }; } });
const image = Node.create({ name: 'image', group: 'block', atom: true, addAttributes: () => Object.fromEntries(['assetId', 'assetScope', 'widthPercent', 'align'].map(field => [field, { default: null }])) });
const schema = getSchema([StarterKit.configure({ paragraph: false, heading: false }), withStart(Paragraph), withStart(Heading), TableKit, image]);
const P = text => ({ type: 'paragraph', content: [{ type: 'text', text, marks: [{ type: 'bold' }] }] });
function state(doc, offset = 6) { const node = schema.nodeFromJSON(doc); return EditorState.create({ doc: node, selection: TextSelection.create(node, offset), plugins: [history()] }); }

test('split preserves formatted text, direction and layout, moves the caret and forms an independent undo step', () => {
    let value = state({ type: 'doc', content: [P('LEFT_ترجمه_RIGHT'), P('AFTER')] }); const before = value.doc;
    value = value.apply(pageBreakTransaction(value, 'insertPageBreak')); value = value.apply(closeHistory(value.tr));
    assert.equal(value.doc.textContent, before.textContent); assert.equal(value.doc.childCount, 3);
    assert.equal(value.doc.child(0).textContent, 'LEFT_'); assert.equal(value.doc.child(1).textContent, 'ترجمه_RIGHT');
    assert.equal(value.doc.child(1).attrs.pageBreakBefore, true); assert.equal(value.selection.$from.parent, value.doc.child(1));
    for (const index of [0, 1]) { assert.equal(value.doc.child(index).attrs.dir, 'rtl'); assert.equal(value.doc.child(index).attrs.spaceBeforePt, 6); assert.equal(value.doc.child(index).firstChild.marks[0].type.name, 'bold'); }
    const split = value.doc, dispatch = tr => { value = value.apply(tr); };
    assert.equal(undo(value, dispatch), true); assert.ok(value.doc.eq(before)); assert.equal(redo(value, dispatch), true); assert.ok(value.doc.eq(split));
    value = value.apply(pageBreakTransaction(value, 'removePageBreak')); assert.equal(value.doc.textContent, before.textContent); assert.equal(value.doc.childCount, 3); assert.equal(pageStartCount(value.doc.toJSON()), 0);
});
test('start of a later paragraph toggles without splitting; start of document and repeated starts are no-ops', () => {
    let value = state({ type: 'doc', content: [P('FIRST'), P('SECOND')] }, 1);
    assert.equal(pageBreakState(value, 'insertPageBreak'), false);
    value = value.apply(value.tr.setSelection(TextSelection.create(value.doc, value.doc.child(0).nodeSize + 1)));
    value = value.apply(pageBreakTransaction(value, 'insertPageBreak')); assert.equal(value.doc.childCount, 2); assert.equal(pageBreakState(value, 'insertPageBreak'), false);
    assert.equal(pageBreakState(value, 'removePageBreak'), true); assert.equal(pageBreakTransaction(value, 'insertPageBreak'), null);
});
test('end of heading makes an editable paragraph on the next page; middle of heading keeps its level', () => {
    const doc = { type: 'doc', content: [{ type: 'heading', attrs: { level: 2 }, content: [{ type: 'text', text: 'TITLE' }] }] };
    let value = state(doc, 6); value = value.apply(pageBreakTransaction(value, 'insertPageBreak'));
    assert.equal(value.doc.child(1).type.name, 'paragraph'); assert.equal(value.doc.child(1).attrs.pageBreakBefore, true); assert.equal(value.selection.$from.parent.content.size, 0);
    value = state(doc, 3); value = value.apply(pageBreakTransaction(value, 'insertPageBreak')); assert.equal(value.doc.child(1).type.name, 'heading'); assert.equal(value.doc.child(1).attrs.level, 2);
});
test('range, whole document and table/list/quote selections cannot introduce nested page starts', () => {
    for (const wrap of [p => ({ type: 'blockquote', content: [p] }), p => ({ type: 'bulletList', content: [{ type: 'listItem', content: [p] }] }), p => ({ type: 'table', content: [{ type: 'tableRow', content: [{ type: 'tableCell', content: [p] }] }] })]) {
        const node = schema.nodeFromJSON({ type: 'doc', content: [wrap(P('NESTED'))] }); let position;
        node.descendants((child, pos) => { if (child.isText) position = pos; });
        const value = EditorState.create({ doc: node, selection: TextSelection.create(node, position) }); assert.equal(pageBreakTransaction(value, 'insertPageBreak'), null);
    }
    let value = state({ type: 'doc', content: [P('RANGE')] });
    for (const selection of [TextSelection.create(value.doc, 1, 3), new AllSelection(value.doc)]) { const next = value.apply(value.tr.setSelection(selection)); assert.equal(pageBreakTransaction(next, 'insertPageBreak'), null); }
});
test('49 starts is enforced before a transaction can exceed the bounded source contract', () => {
    const doc = cases.find(item => item.name === '49 starts').doc; const value = state(doc, 3);
    assert.equal(pageBreakTransaction(value, 'insertPageBreak'), null);
});
test('replacement providers must opt into v10; session readonly cannot be bypassed for either command', () => {
    for (const format of [richDocumentV9, richDocumentV10]) {
        let calls = 0; const source = JSON.stringify({ type: 'doc', content: [P('TEST')] });
        const provider = { id: 'replacement', format, capabilities: {}, create: () => ({ getDocument: () => JSON.parse(source), getCommandState: () => ({ enabled: true }), execute: () => { calls++; return true; }, setEditable() {}, subscribe: () => () => {}, destroy() {} }) };
        const session = createEditorSession({ provider, contentJson: source, editable: true });
        for (const command of ['insertPageBreak', 'removePageBreak']) assert.equal(session.execute(command), format.version === 10);
        session.setEditable(false); assert.equal(session.execute('insertPageBreak'), false); session.destroy(); assert.equal(calls, format.version === 10 ? 2 : 0);
    }
});

test('image insertion within a flagged paragraph retains one page start and every text mark', () => {
    for (const offset of [0, 3, 5]) {
        let value = state({ type: 'doc', content: [P('BEFORE'), { ...P('AFTER'), attrs: { pageBreakBefore: true } }] }, 1);
        value = value.apply(value.tr.setSelection(TextSelection.create(value.doc, value.doc.child(0).nodeSize + 1 + offset)));
        const before = value.doc;
        value = value.apply(imageTransaction(value, 'insertImage', { assetId: '11111111-1111-1111-1111-111111111111' }, value.selection.getBookmark()));
        assert.equal(value.doc.textContent, before.textContent); assert.equal(pageStartCount(value.doc.toJSON()), 1); assert.equal(value.doc.child(1).attrs.pageBreakBefore, true);
        assert.equal(isRichDocument(value.doc.toJSON(), 10), true);
    }
});
