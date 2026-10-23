import { readFile, mkdir, cp, writeFile } from 'node:fs/promises';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const root = path.dirname(fileURLToPath(import.meta.url));
const web = path.resolve(root, '../../src/Web/ETranslate.Web');
const modules = path.join(web, 'node_modules');
const manifest = JSON.parse(await readFile(path.join(web, 'package.json'), 'utf8'));
for (const name of ['@tiptap/core', '@tiptap/pm', '@tiptap/starter-kit', '@tiptap/extension-table']) {
    const installed = JSON.parse(await readFile(path.join(modules, name, 'package.json'), 'utf8'));
    if (installed.version !== '3.31.4' || installed.version !== manifest.dependencies[name]) throw new Error(`Pinned production peer missing: ${name}`);
}
const { build } = await import(pathToFileURL(path.join(modules, 'esbuild/lib/main.js')).href);
const result = await build({ entryPoints: [path.join(root, 'lab.js')], outfile: path.join(root, 'generated/lab.js'),
    nodePaths: [modules], bundle: true, format: 'iife', target: ['es2022'], legalComments: 'linked', metafile: true, logLevel: 'info' });
await writeFile(path.join(root, 'generated/bundle-inputs.json'), JSON.stringify(Object.keys(result.metafile.inputs), null, 2));
const fonts = path.resolve(root, '../../src/Services/Documents/ETranslate.Documents.Api/Rendering/Fonts');
await mkdir(path.join(root, 'generated/fonts'), { recursive: true });
for (const name of ['NotoSans-Regular.ttf', 'NotoSans-Bold.ttf', 'NotoSans-Italic.ttf', 'NotoSans-BoldItalic.ttf',
    'NotoSansArabic-Regular.ttf', 'NotoSansArabic-Bold.ttf', 'OFL.txt', 'README.md'])
    await cp(path.join(fonts, name), path.join(root, 'generated/fonts', name));
await cp(path.join(web, 'wwwroot/vendor/THIRD-PARTY-NOTICES.txt'), path.join(root, 'generated/THIRD-PARTY-NOTICES.txt'));
for (const name of ['tiptap-pagination-plus', 'tiptap-table-plus']) {
    const pkg = JSON.parse(await readFile(path.join(root, 'node_modules', name, 'package.json'), 'utf8'));
    if (pkg.version !== '3.1.0' || pkg.license !== 'MIT') throw new Error('Unexpected experimental dependency');
    await cp(path.join(root, 'node_modules', name, 'Readme.md'), path.join(root, `generated/${name}-Readme.md`));
}
console.log('Free pagination lab built. Production Tiptap peers reused without installing or modifying them.');

