import test from 'node:test';
import assert from 'node:assert/strict';
import { plainRegion, previewHeader } from '../template-presentation.js';
import { isRichDocument } from '../document-schema.js';
const id = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
test('each header line has its own allowlisted alignment; strings stay text', () => {
    const result = plainRegion('<script>\nAddress', 'header', ['center', 'right']);
    assert.equal(result.content[0].attrs.textAlign, 'center'); assert.equal(result.content[1].attrs.textAlign, 'right');
    assert.equal(result.content[0].content[0].text, '<script>');
    assert.equal(plainRegion('Text', 'header', ['url(unsafe)']).content[0].attrs, undefined);
});
test('banner preview preserves original asset nodes without mutating them', () => {
    const original = { type: 'header', content: [{ type: 'image', attrs: { assetId: id } }] };
    const rendered = previewHeader('', [], original, { imageId: id, imageAlign: 'right', imageLayout: 'banner' });
    assert.equal(rendered.content.length, 1); assert.equal(rendered.content[0].attrs.layout, 'banner'); assert.equal(rendered.content[0].attrs.align, 'right');
    assert.equal(original.content[0].attrs.layout, undefined);
});
test('replacement needs an actual new/recovery image, not just a selected mode', () => {
    const original = { content: [{ type: 'image', attrs: { assetId: id } }] };
    assert.equal(previewHeader('', [], original, { replace: true }).content.length, 1);
    assert.equal(previewHeader('', [], original, { replace: true, imageId: 'pending' }).content[0].attrs.assetId, 'pending');
});
test('empty regions are empty; presentation attrs never become editable body attrs', () => {
    assert.deepEqual(plainRegion('', 'footer').content, []);
    assert.equal(isRichDocument(plainRegion('Body', 'doc', ['center'])), false);
});
