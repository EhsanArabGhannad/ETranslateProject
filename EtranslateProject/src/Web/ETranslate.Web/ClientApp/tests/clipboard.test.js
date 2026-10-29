import { test } from 'node:test';
import assert from 'node:assert/strict';
import { clipboardColor, clipboardFont, lengthPt, columnPercentages, neutralizeClipboardStyles } from '../clipboard-style.js';
import { importClipboard, plainClipboard } from '../clipboard-import.js';
import { isRichDocument } from '../document-schema.js';
import { createEditorSession } from '../editors/editor-session.js';
import { richDocumentV10 } from '../editors/document-contract.js';
import { getSchema } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Paragraph from '@tiptap/extension-paragraph';
import { TableKit } from '@tiptap/extension-table';
import { EditorState, TextSelection } from '@tiptap/pm/state';
import { undo, redo, history } from '@tiptap/pm/history';
import { clipboardTransaction } from '../editors/tiptap-clipboard.js';

test('clipboard colors accept opaque bounded CSS colors and reject URLs, alpha and invalid channels', () => {
    for (const [input, output] of [['#abc', '#aabbcc'], ['RED', '#ff0000'], ['rgb(185, 28, 28)', '#b91c1c'], ['rgba(1,2,3,1)', '#010203'], ['transparent', null], ['rgba(1,2,3,.5)', null], ['rgb(256,0,0)', null], ['url(https://example.test)', null]]) assert.equal(clipboardColor(input), output);
});
test('Office lengths convert to points and font substitutions remain explicit', () => {
    assert.equal(lengthPt('24px'), 18); assert.equal(lengthPt('2.54cm'), 72); assert.equal(lengthPt('25.4mm'), 72); assert.equal(lengthPt('1in'), 72);
    for (const value of ['calc(2px)', 'auto', '12em', 'url(x)']) assert.equal(lengthPt(value), null);
    const warnings = new Set(); assert.equal(clipboardFont('"Times New Roman",serif', warnings), 'serif'); assert.ok(warnings.has('fonts'));
    warnings.clear(); assert.equal(clipboardFont('Noto Sans Arabic', warnings), 'arabic'); assert.equal(warnings.size, 0);
});
test('column conversion always returns exact positive integer percentages or declines the source', () => {
    assert.deepEqual(columnPercentages([100, 100, 100]), [34, 33, 33]); assert.deepEqual(columnPercentages([3, 2]), [60, 40]);
    for (const input of [[], [0, 1], [null, 1], [1, 100000], Array(13).fill(1)]) assert.equal(columnPercentages(input), null);
});
test('plain paste keeps blank lines, Unicode, markup as literal text and all existing size bounds', () => {
    const text = 'İstanbul $5 <b>x</b>\r\n\r\nترجمه\t۱۲۳';
    const result = importClipboard({ html: '<script>bad()</script>', text, mode: 'plain' });
    assert.equal(result.content.content.length, 3); assert.equal(result.content.content[0].content[0].text, 'İstanbul $5 <b>x</b>');
    assert.equal(isRichDocument(result.content, 10), true); assert.equal(result.plain, true);
    assert.equal(importClipboard({ text: 'x'.repeat(1_000_001), mode: 'plain' }).error, 'limit'); assert.equal(importClipboard({ text: '' }).error, 'empty');
});
test('clipboard session reporting is optional, allowlisted, readonly gated and never changes source metadata', () => {
    let calls = 0;
    const provider = { id: 'replacement', format: richDocumentV10, create: ({ content }) => ({ getDocument: () => content, getCommandState: () => ({}), setEditable() {}, subscribe: () => () => {}, destroy() {},
        setPasteMode() { calls++; return true; }, getPasteState: () => ({ supported: true, mode: 'plain', report: { outcome: 'inserted', warnings: ['images', '<script>', 'images'], html: 'unsafe' } }) }) };
    const session = createEditorSession({ provider, contentJson: JSON.stringify(plainClipboard('OLD')), editable: true });
    assert.equal(session.setPasteMode('html'), false); assert.equal(session.setPasteMode('plain'), true); assert.deepEqual(session.getPasteState().report.warnings, ['images']); assert.equal('html' in session.getPasteState().report, false);
    session.setEditable(false); assert.equal(session.setPasteMode('formatted'), false); assert.equal(calls, 1); session.destroy(); assert.equal(session.getPasteState().supported, false);
});

const schema = getSchema([StarterKit.configure({ paragraph: false }), Paragraph.extend({ addAttributes: () => ({ pageBreakBefore: { default: null }, dir: { default: 'auto' } }) }), TableKit]);
test('style neutralization respects quoted attributes, comments and raw stylesheet text', () => {
    const html = '<p title="style=literal > test" STYLE = "color:red">style=x</p><!-- <p style=x> --><style>p{font-family:"style=x"}</style><b style=color:blue>x</b>';
    const result = neutralizeClipboardStyles(html);
    assert.ok(result.includes('title="style=literal > test" data-et-clipboard-style = "color:red"')); assert.ok(result.includes('>style=x</p>'));
    assert.ok(result.includes('<!-- <p style=x> -->')); assert.ok(result.includes('p{font-family:"style=x"}')); assert.ok(result.includes('<b data-et-clipboard-style=color:blue>'));
});
test('multi-paragraph paste preserves surrounding text and one existing page start with one Undo step', () => {
    const doc = schema.nodeFromJSON({ type: 'doc', content: [{ type: 'paragraph', attrs: { pageBreakBefore: true }, content: [{ type: 'text', text: 'BEFORE_AFTER' }] }] });
    let value = EditorState.create({ doc, selection: TextSelection.create(doc, 8), plugins: [history()] });
    const tr = clipboardTransaction(value, plainClipboard('ONE\nTWO')); assert.ok(tr); value = value.apply(tr);
    assert.equal(value.doc.textContent, 'BEFORE_ONETWOAFTER'); assert.equal(value.doc.content.content.filter(n => n.attrs.pageBreakBefore).length, 1);
    const pasted = value.doc; const dispatch = tr => { value = value.apply(tr); }; assert.equal(undo(value, dispatch), true); assert.ok(value.doc.eq(doc)); assert.equal(redo(value, dispatch), true); assert.ok(value.doc.eq(pasted));
});
test('paste replaces a selected range without erasing surrounding text or inheriting foreign HTML', () => {
    const doc = schema.nodeFromJSON(plainClipboard('LEFT_MIDDLE_RIGHT')); const state = EditorState.create({ doc, selection: TextSelection.create(doc, 6, 12) });
    const tr = clipboardTransaction(state, plainClipboard('<x>')); assert.ok(tr); assert.equal(tr.doc.textContent, 'LEFT_<x>_RIGHT');
});
test('combined document limits are preflighted before a clipboard transaction can change the source', () => {
    const doc = schema.nodeFromJSON(plainClipboard('x'.repeat(999_998))), state = EditorState.create({ doc, selection: TextSelection.create(doc, 2) });
    assert.equal(clipboardTransaction(state, plainClipboard('TOO_LONG')), null); assert.equal(state.doc, doc);
});
