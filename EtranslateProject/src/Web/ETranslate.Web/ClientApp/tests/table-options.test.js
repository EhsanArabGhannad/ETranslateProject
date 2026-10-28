import { test } from 'node:test';
import assert from 'node:assert/strict';
import { tableOptions, defaultTableOptions } from '../table-options.js';
import { createEditorSession } from '../editors/editor-session.js';
import { richDocumentV5 } from '../editors/document-contract.js';

test('table insertion accepts exact integer bounds, optional header and an unchanged default', () => {
    assert.deepEqual(tableOptions(), { rows: 3, columns: 3, withHeaderRow: true });
    assert.notEqual(tableOptions(), defaultTableOptions);
    for (const rows of [1, 2, 50]) for (const columns of [1, 4, 12]) for (const withHeaderRow of [false, true]) {
        const value = { rows, columns, withHeaderRow };
        assert.deepEqual(tableOptions(value), value); assert.notEqual(tableOptions(value), value);
    }
});

const invalid = [null, [], '3x3', 3, {}, { rows: 3, columns: 3 },
    ...[0, -1, 51, 1.5, '3', NaN, Infinity].map(rows => ({ ...defaultTableOptions, rows })),
    ...[0, -1, 13, 2.5, '3', NaN, Infinity].map(columns => ({ ...defaultTableOptions, columns })),
    ...[null, 1, 'true'].map(withHeaderRow => ({ ...defaultTableOptions, withHeaderRow })),
    { ...defaultTableOptions, html: '<table>' }, { ...defaultTableOptions, cols: 3 }];

test('malformed, oversized or vendor-specific table inputs are rejected without clamping', () => {
    for (const value of invalid) assert.equal(tableOptions(value), null, JSON.stringify(value));
});

test('session passes only validated application table options and blocks disabled/read-only/disposed writes', () => {
    const calls = []; let editable = true;
    const document = { type: 'doc', content: [{ type: 'paragraph' }] };
    const provider = { id: 'replacement-engine', format: richDocumentV5, capabilities: {}, create: () => ({
        getDocument: () => document, getCommandState: () => ({ enabled: editable }),
        execute: (command, value) => { calls.push({ command, value }); return true; },
        setEditable: value => { editable = value; }, subscribe: () => () => {}, destroy() {}
    }) };
    const session = createEditorSession({ provider, contentJson: JSON.stringify(document), editable: true });
    const value = { rows: 50, columns: 12, withHeaderRow: false };
    assert.equal(session.execute('insertTable', value), true);
    assert.deepEqual(calls[0], { command: 'insertTable', value }); assert.notEqual(calls[0].value, value);
    assert.equal(session.execute('insertTable'), true); assert.deepEqual(calls[1].value, defaultTableOptions);
    for (const bad of invalid) assert.equal(session.execute('insertTable', bad), false);
    session.setEditable(false); assert.equal(session.execute('insertTable', value), false);
    session.setEditable(true); editable = false; assert.equal(session.execute('insertTable', value), false);
    session.destroy(); assert.equal(session.execute('insertTable', value), false); assert.equal(calls.length, 2);
    const readonly = createEditorSession({ provider, contentJson: JSON.stringify(document), editable: false });
    readonly.setEditable(true); assert.equal(readonly.execute('insertTable', value), false); readonly.destroy();
});
