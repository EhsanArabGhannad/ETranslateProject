import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import ui from '../../Resources/interface.tr.json' with { type: 'json' };
import { formatUi } from '../ui-format.js';

test('client resource references resolve and product copy is Turkish, not document data', () => {
    for (const file of readdirSync(new URL('..', import.meta.url)).filter(f => f.endsWith('.js'))) {
        const source = readFileSync(new URL(`../${file}`, import.meta.url), 'utf8');
        for (const [, key] of source.matchAll(/\bui\.([A-Za-z0-9_]+)/g)) assert.equal(typeof ui[key], 'string', `${file}: ${key}`);
    }
    for (const value of Object.values(ui)) assert.doesNotMatch(value, /[\u0600-\u06ff]|UiText\.Get|<\/?(?:html|script)/);
    assert.equal(ui.Ayarlar, 'Ayarlar'); assert.equal(ui.Islerim, 'İşlerim');
});
test('formatting preserves RTL, dollar signs and literal markup without rewriting values', () => {
    const value = 'فارسی $& {1} <script>literal</script>';
    assert.equal(formatUi(ui.LinePreview, 1, value), `Satır 1: ${value}`);
    assert.equal(formatUi(ui.PageOf, 2, 12), 'Sayfa 2 / 12');
});

test('all static Razor and BFF resource keys resolve in the shared catalogue', () => {
    function inspect(directory) {
        for (const entry of readdirSync(directory, { withFileTypes: true })) {
            const file = new URL(entry.name + (entry.isDirectory() ? '/' : ''), directory);
            if (entry.isDirectory()) { inspect(file); continue; }
            if (!/\.(cs|cshtml)$/.test(entry.name)) continue;
            const source = readFileSync(file, 'utf8');
            for (const [, key] of source.matchAll(/\bUiText\.(?:Get|Format)\("([^"]+)"/g))
                assert.equal(typeof ui[key], 'string', `${file.pathname}: ${key}`);
        }
    }
    for (const folder of ['Views', 'Controllers', 'Models', 'Services', 'Localization'])
        inspect(new URL(`../../${folder}/`, import.meta.url));
});
