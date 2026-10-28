import { test } from 'node:test';
import assert from 'node:assert/strict';
import { getSchema } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TableKit } from '@tiptap/extension-table';
import { EditorState, TextSelection, AllSelection, NodeSelection } from '@tiptap/pm/state';
import { CellSelection } from '@tiptap/pm/tables';
import { history, closeHistory, undo, redo } from '@tiptap/pm/history';
import { firstTableRow, toggleFirstTableRow } from '../editors/tiptap-table-header.js';
import { isRichDocument } from '../document-schema.js';
import { createEditorSession } from '../editors/editor-session.js';
import { richDocumentV5 } from '../editors/document-contract.js';

const schema = getSchema([StarterKit, TableKit]);
const snapshot = node => JSON.parse(JSON.stringify(node.toJSON()));
const p = text => ({ type: 'paragraph', content: [{ type: 'text', text, marks: [{ type: 'italic' }] }] });
const table = (types = ['tableCell', 'tableCell', 'tableCell'], prefix = 'A', rows = 3) => ({ type: 'table', content:
    Array.from({ length: rows }, (_, r) => ({ type: 'tableRow', content: types.map((type, c) => ({
        type: r === 0 ? type : r === 1 && c === 0 ? 'tableHeader' : 'tableCell',
        attrs: { colspan: 1, rowspan: 1, colwidth: null, align: null },
        content: [p(`${prefix}_${r}_${c} İstanbul ترجمه`), p('Second paragraph')]
    })) })) });
function fixture(types, rows = 3) {
    const doc = schema.nodeFromJSON({ type: 'doc', content: [p('BEFORE'), table(types, 'A', rows), p('BETWEEN'), table(undefined, 'B'), p('AFTER')] });
    const position = text => { let result; doc.descendants((node, pos) => { if (node.isText && node.text.startsWith(text)) result = pos; }); return result; };
    const state = EditorState.create({ doc, selection: TextSelection.create(doc, position('A_1_1')), plugins: [history()] });
    return { state, position };
}

test('from a later row, heading toggle preserves all content, cell attributes, body rows, selection and other tables', () => {
    let { state } = fixture(); const before = snapshot(state.doc); const selection = state.selection;
    assert.equal(firstTableRow(state).active, false);
    state = state.apply(toggleFirstTableRow(state));
    const after = snapshot(state.doc);
    const expected = structuredClone(before); for (const cell of expected.content[1].content[0].content) cell.type = 'tableHeader';
    assert.deepEqual(after, expected); assert.ok(state.selection.eq(selection)); assert.equal(firstTableRow(state).active, true);
    for (let version = 1; version <= 5; version++) assert.equal(isRichDocument(after, version), true);
    state = state.apply(toggleFirstTableRow(state)); assert.deepEqual(snapshot(state.doc), before);
});

test('a mixed first row becomes entirely headings; disabling headings does not alter later header cells', () => {
    let { state } = fixture(['tableHeader', 'tableCell', 'tableHeader']);
    const body = state.doc.child(1).child(1).toJSON();
    assert.equal(firstTableRow(state).active, false);
    state = state.apply(toggleFirstTableRow(state)); assert.equal(firstTableRow(state).active, true);
    assert.equal(state.doc.child(1).firstChild.content.content.every(node => node.type.name === 'tableHeader'), true);
    state = state.apply(toggleFirstTableRow(state));
    assert.equal(state.doc.child(1).firstChild.content.content.every(node => node.type.name === 'tableCell'), true);
    assert.deepEqual(state.doc.child(1).child(1).toJSON(), body);
});

test('outside, whole-document, table-node and cross-table selections cannot modify a first row', () => {
    const { state, position } = fixture();
    for (const selection of [TextSelection.create(state.doc, position('BEFORE')),
        new AllSelection(state.doc), NodeSelection.create(state.doc, state.doc.firstChild.nodeSize),
        TextSelection.create(state.doc, position('A_0_0'), position('B_0_0')),
        TextSelection.create(state.doc, position('A_0_0'), position('AFTER'))]) {
        const selected = state.apply(state.tr.setSelection(selection));
        assert.equal(firstTableRow(selected), null); assert.equal(toggleFirstTableRow(selected), null);
    }
});

test('cell selections and text ranges within one table retain their selection after conversion', () => {
    const { state, position } = fixture();
    const cellPos = text => state.doc.resolve(position(text)).before(3);
    for (const selection of [CellSelection.create(state.doc, cellPos('A_0_0'), cellPos('A_2_2')),
        TextSelection.create(state.doc, position('A_0_0'), position('A_2_2'))]) {
        const selected = state.apply(state.tr.setSelection(selection)), changed = selected.apply(toggleFirstTableRow(selected));
        assert.equal(firstTableRow(changed).active, true); assert.ok(changed.selection.eq(selection));
    }
});

test('minimum and maximum tables convert only the first row without changing dimensions', () => {
    for (const [rows, columns] of [[1, 1], [50, 12]]) {
        const doc = schema.nodeFromJSON({ type: 'doc', content: [table(Array(columns).fill('tableCell'), 'A', rows)] });
        let state = EditorState.create({ doc, selection: TextSelection.create(doc, 4) });
        state = state.apply(toggleFirstTableRow(state));
        assert.equal(state.doc.firstChild.childCount, rows); assert.equal(state.doc.firstChild.firstChild.childCount, columns);
        assert.equal(firstTableRow(state).active, true); assert.equal(isRichDocument(state.doc.toJSON(), 5), true);
    }
});

test('the first row change is one undo step separate from typing, with lossless redo', () => {
    let { state } = fixture(); const initial = state.doc;
    state = state.apply(state.tr.insertText('BEFORE_EDIT ')); const typed = state.doc;
    state = state.apply(toggleFirstTableRow(state)); state = state.apply(closeHistory(state.tr)); const headed = state.doc;
    state = state.apply(state.tr.insertText('AFTER_EDIT '));
    const dispatch = tr => { state = state.apply(tr); };
    assert.equal(undo(state, dispatch), true); assert.ok(state.doc.eq(headed));
    assert.equal(undo(state, dispatch), true); assert.ok(state.doc.eq(typed));
    assert.equal(undo(state, dispatch), true); assert.ok(state.doc.eq(initial));
    assert.equal(redo(state, dispatch), true); assert.ok(state.doc.eq(typed));
    assert.equal(redo(state, dispatch), true); assert.ok(state.doc.eq(headed));
});

test('the engine-neutral header action respects session, provider and immutable readonly locks', () => {
    const calls = []; let providerEnabled = true;
    const contentJson = JSON.stringify({ type: 'doc', content: [p('TEXT')] });
    const provider = { id: 'replacement-engine', format: richDocumentV5, capabilities: {}, create: () => ({
        getDocument: () => JSON.parse(contentJson), getCommandState: () => ({ enabled: providerEnabled, active: true }),
        execute: command => { calls.push(command); return true; }, setEditable() {}, subscribe: () => () => {}, destroy() {}
    }) };
    const session = createEditorSession({ provider, contentJson, editable: true });
    assert.deepEqual(session.getCommandState('toggleTableHeaderRow'), { enabled: true, active: true });
    assert.equal(session.execute('toggleTableHeaderRow'), true);
    session.setEditable(false); assert.equal(session.execute('toggleTableHeaderRow'), false);
    session.setEditable(true); providerEnabled = false; assert.equal(session.execute('toggleTableHeaderRow'), false);
    session.destroy(); providerEnabled = true; assert.equal(session.execute('toggleTableHeaderRow'), false);
    const readonly = createEditorSession({ provider, contentJson }); readonly.setEditable(true);
    assert.equal(readonly.execute('toggleTableHeaderRow'), false); readonly.destroy();
    assert.deepEqual(calls, ['toggleTableHeaderRow']);
});
