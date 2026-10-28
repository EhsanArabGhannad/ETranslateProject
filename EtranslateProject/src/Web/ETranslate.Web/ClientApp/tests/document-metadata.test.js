import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { richDocumentV1, legacyDocument, supportsStoredContent } from '../editors/document-contract.js';

test('persisted legacy and rich-v1 metadata are recognized without guessing from the body', () => {
    assert.equal(supportsStoredContent(undefined, undefined), true); // old HTML only
    assert.equal(supportsStoredContent(null, null), true);
    assert.equal(supportsStoredContent(legacyDocument.id, 0), true);
    assert.equal(supportsStoredContent(legacyDocument.id, '0'), true);
    assert.equal(supportsStoredContent(richDocumentV1.id, 1), true);
    assert.equal(supportsStoredContent(richDocumentV1.id, '1'), true);
    assert.equal(supportsStoredContent(richDocumentV1.id,2),true);
    assert.equal(supportsStoredContent(richDocumentV1.id,3),true);
    assert.equal(supportsStoredContent(richDocumentV1.id,4),true);
    assert.equal(supportsStoredContent(richDocumentV1.id,5),true);
    assert.equal(supportsStoredContent(richDocumentV1.id,6),true);
    assert.equal(supportsStoredContent(richDocumentV1.id,7),true);
});

test('unknown, partial, empty, future and malformed metadata never falls back to legacy', () => {
    for (const [format, version] of [[undefined, 1], [richDocumentV1.id, undefined], ['', ''],
        ['sfdt', 1], [richDocumentV1.id, 8], [richDocumentV1.id, 0], [richDocumentV1.id, -1],
        [richDocumentV1.id, '01'], [richDocumentV1.id, '1.0'], [richDocumentV1.id, [1]],
        [richDocumentV1.id, { toString: () => '1' }], [legacyDocument.id, 1], ['etranslate.rich-document ', 1]])
        assert.equal(supportsStoredContent(format, version), false, `${format}/${version}`);
});

test('client identifiers match the shared server content descriptor', async () => {
    const code = await readFile(new URL('../../../../BuildingBlocks/ETranslate.Contracts/Documents/DocumentContentDescriptor.cs', import.meta.url), 'utf8');
    assert.ok(code.includes(`RichFormat = "${richDocumentV1.id}"`));
    assert.ok(code.includes(`RichSchemaVersion = ${richDocumentV1.version}`));
    assert.ok(code.includes(`LegacyDisplayFormat = "${legacyDocument.id}"`));
});
