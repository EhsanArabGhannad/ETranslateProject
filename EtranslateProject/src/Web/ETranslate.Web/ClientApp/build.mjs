import { build } from 'esbuild';
import { cp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

// Checked-in trusted server resource; production needs no Node runtime or external script.
const plannerSource = '../../Shared/DocumentLayout/table-row-plan.js';
const plannerHash = createHash('sha256').update((await readFile(plannerSource,'utf8')).replace(/\r\n/g,'\n')).digest('hex');
await build({ entryPoints: [plannerSource], outfile: '../../Services/Documents/ETranslate.Documents.Api/Rendering/Layout/table-row-plan.bundle.js',
    bundle: true, format: 'iife', globalName: 'NativeTableRows', target: ['es2022'],
    banner: { js: `// Generated from shared table-row-plan.js; source-sha256:${plannerHash}` }, logLevel: 'info' });
await mkdir('wwwroot/fonts/document-layout', { recursive: true });
for (const name of ['NotoSans-Regular.ttf', 'NotoSans-Bold.ttf', 'NotoSans-Italic.ttf', 'NotoSans-BoldItalic.ttf',
    'NotoSansArabic-Regular.ttf', 'NotoSansArabic-Bold.ttf', 'OFL.txt'])
    await cp(`../../Services/Documents/ETranslate.Documents.Api/Rendering/Fonts/${name}`, `wwwroot/fonts/document-layout/${name}`);
await cp('ClientApp/document-pagination.css', 'wwwroot/css/document-pagination.css');

await build({
    entryPoints: { workspace: 'ClientApp/workspace.js', 'template-preview': 'ClientApp/template-preview.js', 'pdf.worker': 'node_modules/pdfjs-dist/build/pdf.worker.mjs' },
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
