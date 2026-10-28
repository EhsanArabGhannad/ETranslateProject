import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { getSchema } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { Node } from '@tiptap/core';
import { EditorState, TextSelection, NodeSelection } from '@tiptap/pm/state';
import { history, closeHistory, undo, redo } from '@tiptap/pm/history';
import { isRichDocument } from '../document-schema.js';
import { imageFields, validImageLayout } from '../body-image.js';
import { canInsertImage, selectedImage, imageTransaction } from '../editors/tiptap-images.js';
import { createEditorSession } from '../editors/editor-session.js';
import { richDocumentV8, richDocumentV9 } from '../editors/document-contract.js';
const id = '11111111-1111-1111-1111-111111111111';
const samples = JSON.parse(await readFile(new URL('./body-image-cases.json', import.meta.url), 'utf8'));
for (const sample of samples) test(`body image v9: ${sample.name}`, () => {
    const doc = { type: 'doc', content: [{ type: 'image', attrs: { assetId: id, ...sample.attrs } }] };
    assert.equal(isRichDocument(doc, 9), sample.valid);
    for (let v = 1; v <= 8; v++) assert.equal(isRichDocument(doc, v), sample.legacyValid);
});
const ImageNode = Node.create({ name: 'image', group: 'block', atom: true, addAttributes: () => Object.fromEntries(['assetId', ...imageFields].map(field => [field, { default: null }])) });
const schema = getSchema([StarterKit, ImageNode]);
const initial = () => {
    const doc = schema.nodeFromJSON({ type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'LEFT_RIGHT', marks: [{ type: 'bold' }] }] }, { type: 'paragraph', content: [{ type: 'text', text: 'AFTER' }] }] });
    return EditorState.create({ doc, selection: TextSelection.create(doc, 6), plugins: [history()] });
};
test('image insertion splits a paragraph without losing marks, uses a captured location and is one history step', () => {
    let state = initial(); const before = state.doc, bookmark = state.selection.getBookmark();
    state = state.apply(state.tr.setSelection(TextSelection.atEnd(state.doc)));
    state = state.apply(imageTransaction(state, 'insertImage', { assetId: id }, bookmark)); state = state.apply(closeHistory(state.tr));
    assert.equal(selectedImage(state).attrs.assetScope, 'document'); assert.equal(selectedImage(state).attrs.widthPercent, 50);
    assert.equal(state.doc.textContent, before.textContent); assert.equal(state.doc.child(0).textContent, 'LEFT_');
    assert.equal(state.doc.child(2).textContent, 'RIGHT'); assert.equal(state.doc.child(0).firstChild.marks[0].type.name, 'bold');
    assert.equal(isRichDocument(state.doc.toJSON(), 9), true);
    const merged = state.doc; const dispatch = tr => { state = state.apply(tr); };
    assert.equal(undo(state, dispatch), true); assert.ok(state.doc.eq(before)); assert.equal(redo(state, dispatch), true); assert.ok(state.doc.eq(merged));
});
test('image resize, alignment and deletion preserve content and remain undoable; invalid dimensions and selections fail closed', () => {
    let state = initial(); state = state.apply(imageTransaction(state, 'insertImage', { assetId: id }, state.selection.getBookmark())); state = state.apply(closeHistory(state.tr));
    const inserted = state.doc;
    state = state.apply(imageTransaction(state, 'imageLayout', { widthPercent: 75, align: 'right' })); state = state.apply(closeHistory(state.tr));
    assert.equal(selectedImage(state).attrs.widthPercent, 75); assert.equal(selectedImage(state).attrs.assetId, id);
    assert.equal(imageTransaction(state, 'imageLayout', { widthPercent: 0, align: 'right' }), null);
    state = state.apply(imageTransaction(state, 'deleteImage')); state = state.apply(closeHistory(state.tr));
    assert.equal(state.doc.textContent, inserted.textContent); assert.equal(selectedImage(state), null);
    assert.equal(undo(state, tr => { state = state.apply(tr); }), true); assert.equal(selectedImage(state).attrs.widthPercent, 75);
    state = state.apply(state.tr.setSelection(TextSelection.atStart(state.doc))); assert.equal(imageTransaction(state, 'deleteImage'), null);
    state = state.apply(state.tr.setSelection(TextSelection.create(state.doc, 1, 3))); assert.equal(canInsertImage(state), false);
    assert.equal(validImageLayout({ widthPercent: 50, align: 'left', src: 'evil' }), false);
});
test('image session boundary validates typed input, old-provider support and readonly upload bookmarks', () => {
    for (const format of [richDocumentV8, richDocumentV9]) {
        let calls = 0, released = false; const source = JSON.stringify({ type: 'doc', content: [{ type: 'paragraph' }] });
        const provider = { id: 'replacement', format, capabilities: {}, create: () => ({ getDocument: () => JSON.parse(source), getCommandState: () => ({ enabled: true }), execute: () => { calls++; return true; }, captureImageInsertion: () => 'opaque-1', releaseImageInsertion: () => { released = true; }, setEditable() {}, subscribe: () => () => {}, destroy() {} }) };
        const session = createEditorSession({ provider, contentJson: source, editable: true });
        assert.equal(session.captureImageInsertion(), format.version === 9 ? 'opaque-1' : null);
        assert.equal(session.execute('insertImage', { assetId: id, bookmark: 'opaque-1' }), format.version === 9);
        assert.equal(session.execute('insertImage', { assetId: id, bookmark: {} }), false);
        assert.equal(session.execute('imageLayout', { widthPercent: 50, align: 'right' }), format.version === 9);
        session.setEditable(false); assert.equal(session.captureImageInsertion(), null); assert.equal(session.execute('deleteImage'), false);
        session.releaseImageInsertion('opaque-1'); assert.equal(released, true); assert.equal(calls, format.version === 9 ? 2 : 0); session.destroy();
    }
});

test('ten body images disable another insertion without changing the existing document', () => {
    const doc = schema.nodeFromJSON({ type: 'doc', content: [...Array.from({ length: 10 }, () => ({ type: 'image', attrs: { assetId: id, assetScope: 'document', widthPercent: 25, align: 'left' } })), { type: 'paragraph' }] });
    const state = EditorState.create({ doc, selection: TextSelection.atEnd(doc) });
    assert.equal(canInsertImage(state), false);
    assert.equal(imageTransaction(state, 'insertImage', { assetId: id }, state.selection.getBookmark()), null);
});
