import { test } from 'node:test';
import assert from 'node:assert/strict';
import { isRichDocument } from '../document-schema.js';
import { getSchema } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { TableKit } from '@tiptap/extension-table';

const cell = () => ({ type: 'tableCell', content: [{ type: 'paragraph' }] });
const document = (rows = 2, columns = 3) => ({ type: 'doc', content: [{ type: 'table', content:
    Array.from({ length: rows }, () => ({ type: 'tableRow', content: Array.from({ length: columns }, cell) })) }] });
test('pinned Tiptap cell defaults round-trip through the preservation policy', () => {
    const schema = getSchema([StarterKit, TableKit]);
    const node = schema.nodeFromJSON(document()); node.check();
    assert.equal(isRichDocument(node.toJSON()), true);
    assert.deepEqual({ ...node.firstChild.firstChild.firstChild.attrs }, { colspan: 1, rowspan: 1, colwidth: null, align: null });
});
test('table limits are exact and per table', () => {
    assert.equal(isRichDocument(document(50, 12)), true);
    assert.equal(isRichDocument(document(51, 12)), false);
    assert.equal(isRichDocument(document(50, 13)), false);
    assert.equal(isRichDocument(document(0, 3)), false);
    assert.equal(isRichDocument(document(2, 0)), false);
});
test('uneven and nested tables are not silently repaired', () => {
    const uneven = document(); uneven.content[0].content[1].content.pop();
    assert.equal(isRichDocument(uneven), false);
    const nested = document(); nested.content[0].content[0].content[0].content.push(document().content[0]);
    assert.equal(isRichDocument(nested), false);
});
test('merged, resized, aligned or styled cells require unsupported fallback', () => {
    for (const attrs of [{ colspan: 2 }, { rowspan: 2 }, { colwidth: [100] }, { align: 'center' }, { style: 'color:red' }, { colspan: '1' }]) {
        const value = document(); value.content[0].content[0].content[0].attrs = attrs;
        assert.equal(isRichDocument(value), false);
    }
});
