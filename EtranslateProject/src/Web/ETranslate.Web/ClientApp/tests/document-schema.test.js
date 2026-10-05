import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { isRichDocument, normalizeEmptyText } from '../document-schema.js';

const cases = JSON.parse(await readFile(new URL('./document-cases.json', import.meta.url), 'utf8'));
for (const fixture of cases) test(fixture.name, () => assert.equal(isRichDocument(fixture.document), fixture.valid));
test('legacy normalization preserves marks and directions without mutating the source', () => {
    const source = { type: 'doc', content: [{ type: 'paragraph', attrs: { dir: 'rtl' }, content: [
        { type: 'text', text: '' }, { type: 'text', text: 'ترجمه', marks: [{ type: 'bold' }] }
    ] }] };
    const normalized = normalizeEmptyText(source);
    assert.equal(source.content[0].content.length, 2);
    assert.equal(normalized.content[0].content.length, 1);
    assert.deepEqual(normalized.content[0].content[0].marks, [{ type: 'bold' }]);
    assert.equal(normalized.content[0].attrs.dir, 'rtl');
});
test('deep and oversized node trees are rejected', () => {
    let tree = { type: 'paragraph' };
    for (let index = 0; index < 34; index++) tree = { type: 'blockquote', content: [tree] };
    assert.equal(isRichDocument({ type: 'doc', content: [tree] }), false);
    assert.equal(isRichDocument({ type: 'doc', content: Array.from({ length: 20001 }, () => ({ type: 'paragraph' })) }), false);
});
