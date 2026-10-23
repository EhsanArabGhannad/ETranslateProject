import { readFile, mkdir, cp } from 'node:fs/promises';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const root = path.dirname(fileURLToPath(import.meta.url));
const web = path.resolve(root, '../../src/Web/ETranslate.Web');
const installed = JSON.parse(await readFile(path.join(web, 'node_modules/@tiptap/core/package.json'), 'utf8'));
const declared = JSON.parse(await readFile(path.join(web, 'package.json'), 'utf8'));
if (installed.version !== declared.dependencies['@tiptap/core']) throw new Error('Build the installed, pinned production editor version first.');
const { build } = await import(pathToFileURL(path.join(web, 'node_modules/esbuild/lib/main.js')).href);
await build({ entryPoints: [path.join(root, 'lab.js')], outfile: path.join(root, 'generated/lab.js'),
    nodePaths: [path.join(web, 'node_modules')], bundle: true, format: 'iife', target: ['es2022'], legalComments: 'linked', logLevel: 'info' });
await mkdir(path.join(root, 'generated/fonts'), { recursive: true });
const fonts = path.resolve(root, '../../src/Services/Documents/ETranslate.Documents.Api/Rendering/Fonts');
for (const name of ['NotoSans-Regular.ttf', 'NotoSans-Bold.ttf', 'NotoSans-Italic.ttf', 'NotoSans-BoldItalic.ttf',
    'NotoSansArabic-Regular.ttf', 'NotoSansArabic-Bold.ttf'])
    await cp(path.join(fonts, name), path.join(root, 'generated/fonts', name));
// Preserve the notices already shipped alongside the production renderer's fonts.
for (const name of ['OFL.txt', 'README.md']) await cp(path.join(fonts, name), path.join(root, 'generated/fonts', name));
await cp(path.join(web, 'wwwroot/vendor/THIRD-PARTY-NOTICES.txt'), path.join(root, 'generated/THIRD-PARTY-NOTICES.txt'));
console.log(`Isolated baseline lab built with Tiptap ${installed.version}; no dependencies installed.`);
