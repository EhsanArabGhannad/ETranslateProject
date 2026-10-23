import { readFile, mkdir, cp } from 'node:fs/promises';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';
const root = path.dirname(fileURLToPath(import.meta.url));
const web = path.resolve(root, '../../src/Web/ETranslate.Web');
const pkgRoot = path.join(root, 'node_modules/@hufe921/canvas-editor');
const pkg = JSON.parse(await readFile(path.join(pkgRoot, 'package.json'), 'utf8'));
if (pkg.version !== '1.0.5' || pkg.license !== 'MIT') throw new Error('Unexpected Canvas Editor version/license');
const { build } = await import(pathToFileURL(path.join(web, 'node_modules/esbuild/lib/main.js')).href);
await build({ entryPoints:[path.join(root,'lab.js')], outfile:path.join(root,'generated/lab.js'),
    bundle:true, format:'iife', target:['es2022'], legalComments:'linked', logLevel:'info' });
const fonts = path.resolve(root, '../../src/Services/Documents/ETranslate.Documents.Api/Rendering/Fonts');
await mkdir(path.join(root,'generated/fonts'), { recursive:true });
for (const name of ['NotoSans-Regular.ttf','NotoSans-Bold.ttf','NotoSans-Italic.ttf','NotoSans-BoldItalic.ttf',
    'NotoSansArabic-Regular.ttf','NotoSansArabic-Bold.ttf','OFL.txt','README.md'])
    await cp(path.join(fonts,name),path.join(root,'generated/fonts',name));
for (const name of ['LICENSE','README.md']) await cp(path.join(pkgRoot,name),path.join(root,`generated/canvas-editor-${name}`));
console.log('Isolated Canvas Editor 1.0.5 trial built. No production packages or schemas changed.');

