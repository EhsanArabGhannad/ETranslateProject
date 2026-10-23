import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = path.dirname(fileURLToPath(import.meta.url));
const index = process.argv.indexOf('--port'), port = index < 0 ? '5080' : process.argv[index+1];
if (!/^\d+$/.test(port ?? '') || Number(port)>65535) throw new Error('Invalid local port');
const files = new Map([['/',['lab.html','text/html; charset=utf-8']],['/lab.css',['lab.css','text/css; charset=utf-8']],['/generated/lab.js',['generated/lab.js','text/javascript; charset=utf-8']]]);
for (const name of ['NotoSans-Regular.ttf','NotoSans-Bold.ttf','NotoSans-Italic.ttf','NotoSans-BoldItalic.ttf','NotoSansArabic-Regular.ttf','NotoSansArabic-Bold.ttf'])
    files.set(`/generated/fonts/${name}`,[`generated/fonts/${name}`,'font/ttf']);
const server = createServer(async(request,response) => {
    // Vendor injects CSS and bundled blob workers. No remote services or arbitrary uploaded content.
    response.setHeader('Content-Security-Policy',"default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src data:; worker-src blob:; connect-src 'none'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'");
    response.setHeader('X-Content-Type-Options','nosniff'); response.setHeader('Referrer-Policy','no-referrer'); response.setHeader('Cache-Control','no-store');
    if (![ `127.0.0.1:${server.address().port}`, `localhost:${server.address().port}` ].includes(request.headers.host)) { response.writeHead(403).end(); return; }
    if (!['GET','HEAD'].includes(request.method)) { response.writeHead(405,{Allow:'GET, HEAD'}).end(); return; }
    const item=files.get(request.url); if(!item){ response.writeHead(404).end();return; }
    try { const bytes=await readFile(path.join(root,item[0])); response.writeHead(200,{'Content-Type':item[1]});response.end(request.method==='HEAD'?undefined:bytes); }
    catch { response.writeHead(503).end('Build the isolated lab first.'); }
});
server.listen(Number(port),'127.0.0.1',()=>console.log(JSON.stringify({url:`http://127.0.0.1:${server.address().port}`})));

