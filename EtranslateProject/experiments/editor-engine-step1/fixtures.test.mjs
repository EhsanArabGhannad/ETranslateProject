import test from 'node:test';
import assert from 'node:assert/strict';
import { baselineDocument, oversizedDocument, wordLikeHtml, baselineStyleGaps } from './samples.js';
import { isRichDocument } from '../../src/Web/ETranslate.Web/ClientApp/document-schema.js';

test('bilingual baseline uses the unchanged production schema and includes a simple table', () => {
    const document = baselineDocument(); assert.equal(isRichDocument(document), true);
    assert.ok(document.content.some(node => node.type === 'table'));
    assert.match(JSON.stringify(document), /[\u0600-\u06ff]/); assert.match(JSON.stringify(document), /İstanbul/);
});
test('large block fixture is valid content, not a malformed-schema shortcut', () => {
    assert.equal(isRichDocument(oversizedDocument()), true);
    assert.ok(oversizedDocument().content[0].content[0].text.length > 10000);
});
test('synthetic Word-style input contains no URLs or executable markup', () => {
    assert.doesNotMatch(wordLikeHtml, /https?:|<script|<img|<iframe|onload=/i);
    assert.match(wordLikeHtml, /font-size:18pt/);
});
test('gap report distinguishes unsupported styles from the document text', () => {
    const doc = baselineDocument(), snapshot = JSON.stringify(doc);
    assert.equal(baselineStyleGaps(doc).length, 6); assert.equal(JSON.stringify(doc), snapshot);
});
