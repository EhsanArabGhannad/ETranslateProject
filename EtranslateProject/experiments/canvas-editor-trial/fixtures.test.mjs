import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { sample,flat,canonicalText,htmlFixture } from './samples.js';
test('bilingual source and Persian ZWNJ are preserved by comparison',()=>{
    const text=canonicalText(flat(sample('text').main));assert.equal(text.match(/MARK_\d{3}/g).length,48);
    assert.match(text,/İstanbul/);assert.match(text,/جابه‌جایی/);assert.equal(canonicalText('الف‌با'),'الف‌با');
});
test('table fixtures are bounded and retain last-row/cell end markers',()=>{
    const table=sample('long-table').main[0];assert.equal(table.trList.length,47);assert.match(flat([table]),/ROW_046/);
    assert.match(flat(sample('tall-cell').main),/TALL_END/);assert.ok(flat(sample('tall-cell').main).length<10000);
});
test('fixed HTML has formatting but no executable or external content',()=>{
    assert.doesNotMatch(htmlFixture,/<script|onerror|https?:|src=/i);assert.match(htmlFixture,/font-size/);assert.throws(()=>sample('../private'));
});
test('the single trial dependency is MIT-declared, exact and integrity-pinned',async()=>{
    const lock=JSON.parse(await readFile(new URL('./package-lock.json',import.meta.url)));
    const packages=Object.entries(lock.packages).filter(([key])=>key!=='');assert.equal(packages.length,1);
    const pkg=packages[0][1];assert.equal(pkg.version,'1.0.5');assert.equal(pkg.license,'MIT');assert.match(pkg.integrity,/^sha512-/);
});
