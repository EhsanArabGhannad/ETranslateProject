import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

const script = readFileSync(new URL('../../wwwroot/js/site.js', import.meta.url), 'utf8');
function fixture(accept = false) {
    const element = props => ({ listeners: {}, addEventListener(name, fn) { this.listeners[name] = fn; }, ...props });
    const picker = element({ value: 'old' }), form = element({ dataset: { discardConfirm: 'Discard?', invalidLogo: 'Invalid logo' } }), preview = { hidden: true, removeAttribute() { delete this.src; } };
    const file = element({ files: [], setCustomValidity(value) { this.validity = value; }, reportValidity() {} });
    const nodes = { 'template-picker': picker, 'template-edit-form': form, 'template-save': {}, 'template-logo': file,
        'template-logo-preview': preview, 'template-picker-form': { requestSubmit() { this.submitted = true; } } };
    const window = element({ confirm: () => accept }), revoked = [];
    runInNewContext(script, { document: { getElementById: id => nodes[id] }, window,
        URL: { createObjectURL: () => 'blob:preview', revokeObjectURL: url => revoked.push(url) } });
    return { nodes, picker, form, file, preview, window, revoked };
}
test('cancelling dirty template selection restores picker and does not submit', () => {
    const f = fixture(); f.form.listeners.input(); f.picker.value = 'new'; f.picker.listeners.change();
    assert.equal(f.picker.value, 'old'); assert.equal(f.nodes['template-picker-form'].submitted, undefined);
    let prevented = false; f.window.listeners.beforeunload({ preventDefault() { prevented = true; } }); assert.equal(prevented, true);
});
test('accepted selection submits; saving disables repeated submission and clears unload warning', () => {
    const f = fixture(true); f.form.listeners.input(); f.picker.value = 'new'; f.picker.listeners.change();
    assert.equal(f.nodes['template-picker-form'].submitted, true);
    f.form.listeners.input(); f.form.listeners.submit(); assert.equal(f.nodes['template-save'].disabled, true);
    f.window.listeners.beforeunload({ preventDefault() { assert.fail('Saved form should not warn'); } });
});
test('preview uses object URLs, rejects invalid files and revokes replaced URLs', () => {
    const f = fixture(); f.file.files = [{ type: 'image/png', size: 20 }]; f.file.listeners.change();
    assert.equal(f.preview.src, 'blob:preview'); assert.equal(f.preview.hidden, false);
    f.file.files = [{ type: 'text/html', size: 20 }]; f.file.listeners.change();
    assert.deepEqual(f.revoked, ['blob:preview']); assert.equal(f.preview.hidden, true); assert.ok(f.file.validity);
    f.file.files = [{ type: 'image/jpeg', size: 6 * 1024 * 1024 }]; f.file.listeners.change(); assert.equal(f.preview.hidden, true);
});
test('leaving page releases preview URL', () => {
    const f = fixture(); f.file.files = [{ type: 'image/jpeg', size: 20 }]; f.file.listeners.change(); f.window.listeners.pagehide();
    assert.deepEqual(f.revoked, ['blob:preview']);
});
