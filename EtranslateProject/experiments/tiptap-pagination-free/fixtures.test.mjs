import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { sample, flatText } from './samples.js';
import { isRichDocument } from '../../src/Web/ETranslate.Web/ClientApp/document-schema.js';
test('bilingual text and long paragraph remain inside the existing JSON contract', () => {
    for (const id of ['text', 'long-paragraph']) assert.equal(isRichDocument(sample(id)), true);
    const text = flatText(sample('text')); assert.match(text, /MARK_048/); assert.match(text, /İstanbul/); assert.match(text, /ساختگی/);
});
test('source simple and long tables are valid before the companion extension adds its attributes', () => {
    for (const id of ['simple-table', 'long-table', 'tall-cell']) assert.equal(isRichDocument(sample(id)), true);
    assert.equal(flatText(sample('long-table')).match(/MARK_\d{3}/g).length, 46);
});
test('merged fixture is deliberately unsupported by the current production contract', () => assert.equal(isRichDocument(sample('merged')), false));
test('fixtures are deterministic and unknown names are refused', () => {
    assert.deepEqual(sample('text'), sample('text')); assert.throws(() => sample('../private'));
    assert.doesNotMatch(sample('word'), /https?:|src=|<script|onerror/i);
});
test('experimental dependencies are exact, isolated, MIT-declared and integrity-pinned', async () => {
    const lock = JSON.parse(await readFile(new URL('./package-lock.json', import.meta.url)));
    const packages = Object.entries(lock.packages).filter(([key]) => key !== '');
    assert.equal(packages.length, 5);
    for (const [, pkg] of packages) { assert.equal(pkg.license, 'MIT'); assert.match(pkg.integrity, /^sha512-/); assert.match(pkg.resolved, /^https:\/\/registry\.npmjs\.org\//); }
    assert.equal(lock.packages['node_modules/tiptap-pagination-plus'].version, '3.1.0');
    assert.equal(lock.packages['node_modules/tiptap-table-plus'].version, '3.1.0');
});
test('bundling reuses a single production ProseMirror model and core', async () => {
    const inputs = JSON.parse(await readFile(new URL('./generated/bundle-inputs.json', import.meta.url)));
    assert.equal(inputs.filter(p => p.endsWith('prosemirror-model/dist/index.js')).length, 1);
    assert.ok(inputs.some(p => p.includes('src/Web/ETranslate.Web/node_modules/@tiptap/core/')));
    assert.ok(!inputs.some(p => /tiptap-pagination-free\/node_modules\/@tiptap\/(core|pm)\//.test(p)));
});

