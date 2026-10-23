import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.dirname(fileURLToPath(import.meta.url));
const portIndex = process.argv.indexOf('--port');
const requested = portIndex < 0 ? '5078' : process.argv[portIndex + 1];
if (!/^\d+$/.test(requested ?? '') || Number(requested) > 65535) throw new Error('Invalid local port');
const files = new Map([
    ['/', ['lab.html', 'text/html; charset=utf-8']], ['/lab.css', ['lab.css', 'text/css; charset=utf-8']],
    ['/generated/lab.js', ['generated/lab.js', 'text/javascript; charset=utf-8']]
]);
for (const name of ['NotoSans-Regular.ttf', 'NotoSans-Bold.ttf', 'NotoSans-Italic.ttf', 'NotoSans-BoldItalic.ttf',
    'NotoSansArabic-Regular.ttf', 'NotoSansArabic-Bold.ttf'])
    files.set(`/generated/fonts/${name}`, [`generated/fonts/${name}`, 'font/ttf']);

const server = createServer(async (request, response) => {
    response.setHeader('Content-Security-Policy', "default-src 'none'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'none'; connect-src 'none'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'");
    response.setHeader('X-Content-Type-Options', 'nosniff'); response.setHeader('Referrer-Policy', 'no-referrer');
    response.setHeader('Cache-Control', 'no-store');
    const allowedHosts = [`127.0.0.1:${server.address().port}`, `localhost:${server.address().port}`];
    if (!allowedHosts.includes(request.headers.host)) { response.writeHead(403).end(); return; }
    if (!['GET', 'HEAD'].includes(request.method)) { response.writeHead(405, { Allow: 'GET, HEAD' }).end(); return; }
    const resource = files.get(request.url);
    if (!resource) { response.writeHead(404).end(); return; }
    try {
        const bytes = await readFile(path.join(root, resource[0]));
        response.writeHead(200, { 'Content-Type': resource[1] }); response.end(request.method === 'HEAD' ? undefined : bytes);
    } catch { response.writeHead(503).end('Build the isolated lab first.'); }
});
server.listen(Number(requested), '127.0.0.1', () => console.log(JSON.stringify({ url: `http://127.0.0.1:${server.address().port}` })));
