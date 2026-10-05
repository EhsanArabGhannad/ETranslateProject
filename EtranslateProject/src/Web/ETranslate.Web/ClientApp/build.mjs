import { build } from 'esbuild';
import { cp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';

await build({
    entryPoints: { workspace: 'ClientApp/workspace.js', 'pdf.worker': 'node_modules/pdfjs-dist/build/pdf.worker.mjs' },
    outdir: 'wwwroot/js/generated', bundle: true, minify: true, format: 'esm',
    target: ['es2022'], legalComments: 'linked', logLevel: 'info'
});
await mkdir('wwwroot/vendor/pdfjs', { recursive: true });
for (const folder of ['cmaps', 'standard_fonts', 'wasm', 'iccs'])
    await cp(`node_modules/pdfjs-dist/${folder}`, `wwwroot/vendor/pdfjs/${folder}`, { recursive: true });
await cp('node_modules/pdfjs-dist/LICENSE', 'wwwroot/vendor/pdfjs/LICENSE');
await mkdir('wwwroot/vendor/tiptap', { recursive: true });
await cp('node_modules/@tiptap/core/LICENSE.md', 'wwwroot/vendor/tiptap/LICENSE.md');

// Keep copyright/license texts for transitive packages as well as the two direct libraries.
const lock = JSON.parse(await readFile('package-lock.json', 'utf8'));
const notices = [];
for (const [path, metadata] of Object.entries(lock.packages).sort(([a], [b]) => a.localeCompare(b))) {
    if (!path.startsWith('node_modules/')) continue;
    let files;
    try { files = await readdir(path, { withFileTypes: true }); }
    catch (error) { if (error.code === 'ENOENT') continue; throw error; } // Optional platform packages may be absent.
    for (const file of files.filter(file => file.isFile() && /^(licen[cs]e|copying)(\.|$)/i.test(file.name)))
        notices.push(`${path} @ ${metadata.version}\n${file.name}\n${await readFile(`${path}/${file.name}`, 'utf8')}`);
}
await writeFile('wwwroot/vendor/THIRD-PARTY-NOTICES.txt', notices.join('\n\n--------------------\n\n'));
