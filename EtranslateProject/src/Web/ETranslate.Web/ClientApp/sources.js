import * as pdfjs from 'pdfjs-dist/build/pdf.mjs';
import { guid } from './document-schema.js';

pdfjs.GlobalWorkerOptions.workerSrc = '/js/generated/pdf.worker.js';

export function startSources(workspace) {
    const find = id => document.getElementById(id), select = find('source-select');
    if (!select) return;
    const status = find('source-status'), display = find('source-display');
    const previous = find('pdf-prev'), next = find('pdf-next'), zoom = find('source-zoom');
    let generation = 0, loadingTask = null, pdf = null, renderTask = null, pageNumber = 1, rendering = false;
    const base = new URL(workspace.dataset.sourceBase, location.origin);
    function sourceUrl(id, download = false) {
        if (!guid.test(id) || base.origin !== location.origin || base.pathname !== '/Workspace/Source') throw new Error('Invalid source URL');
        const url = new URL(base); url.searchParams.set('sourceFileId', id);
        if (download) url.searchParams.set('download', 'true');
        return url.href;
    }
    function controls() {
        previous.disabled = !pdf || rendering || pageNumber <= 1;
        next.disabled = !pdf || rendering || pageNumber >= pdf.numPages;
        zoom.disabled = !pdf || rendering;
        find('pdf-page-label').textContent = pdf ? `صفحه ${pageNumber} از ${pdf.numPages}` : '';
    }
    async function renderPage(version) {
        if (!pdf) return;
        const currentPdf = pdf;
        rendering = true; controls(); status.textContent = 'در حال نمایش صفحه…';
        try {
            const page = await currentPdf.getPage(pageNumber);
            if (version !== generation) return;
            const natural = page.getViewport({ scale: 1 });
            const desired = Math.max(200, display.clientWidth - 24) / natural.width * Number(zoom.value);
            const scale = Math.min(desired, 2200 / natural.width, 3000 / natural.height,
                Math.sqrt(6000000 / (natural.width * natural.height)));
            const viewport = page.getViewport({ scale });
            const canvas = document.createElement('canvas'); canvas.width = Math.ceil(viewport.width); canvas.height = Math.ceil(viewport.height);
            canvas.setAttribute('aria-label', `صفحه ${pageNumber} مدرک مبدأ`); canvas.setAttribute('role', 'img');
            renderTask = page.render({ canvas, viewport });
            await renderTask.promise;
            if (version !== generation) return;
            display.replaceChildren(canvas); status.textContent = 'مدرک مبدأ · پیش‌نمایش تصویری PDF';
        } catch {
            if (version === generation) status.textContent = 'نمایش این صفحه ممکن نشد؛ فایل اصلی را می‌توانید دانلود کنید.';
        } finally { if (version === generation) { rendering = false; controls(); } }
    }
    async function openSelected() {
        const version = ++generation;
        renderTask?.cancel(); renderTask = null;
        if (loadingTask) { void loadingTask.destroy().catch(() => {}); loadingTask = null; }
        pdf = null; rendering = false; pageNumber = 1; zoom.value = '1'; controls();
        display.replaceChildren(); find('pdf-controls').hidden = true;
        find('source-download').hidden = !select.value;
        if (!select.value) { status.textContent = 'فایل مبدأ را انتخاب یا بارگذاری کنید.'; return; }
        const option = select.selectedOptions[0], url = sourceUrl(select.value);
        find('source-download').href = sourceUrl(select.value, true);
        if (option.dataset.type === 'image/tiff') {
            status.textContent = 'پیش‌نمایش TIFF پشتیبانی نمی‌شود؛ فایل اصلی قابل دانلود است.'; return;
        }
        if (['image/png', 'image/jpeg'].includes(option.dataset.type)) {
            const image = document.createElement('img'); image.src = url; image.alt = option.textContent;
            image.addEventListener('load', () => { if (version === generation) status.textContent = 'تصویر مدرک مبدأ'; });
            image.addEventListener('error', () => { if (version === generation) status.textContent = 'تصویر باز نشد؛ ورود و دسترسی پرونده را بررسی کنید.'; });
            display.append(image); status.textContent = 'در حال دریافت تصویر…'; return;
        }
        if (option.dataset.type !== 'application/pdf') { status.textContent = 'این نوع فایل قابل پیش‌نمایش نیست.'; return; }
        status.textContent = 'در حال دریافت PDF…';
        try {
            const task = pdfjs.getDocument({ url, withCredentials: true, isEvalSupported: false, enableXfa: false,
                disableFontFace: true, cMapUrl: '/vendor/pdfjs/cmaps/', cMapPacked: true,
                standardFontDataUrl: '/vendor/pdfjs/standard_fonts/', wasmUrl: '/vendor/pdfjs/wasm/',
                iccUrl: '/vendor/pdfjs/iccs/', maxImageSize: 16000000, canvasMaxAreaInBytes: 32000000 });
            loadingTask = task;
            let passwordRequired = false;
            task.onPassword = () => { passwordRequired = true; void task.destroy().catch(() => {}); };
            let loaded;
            try { loaded = await task.promise; }
            catch (error) {
                if (version === generation && passwordRequired) { status.textContent = 'PDF رمزدار در این مرحله پیش‌نمایش ندارد؛ فایل اصلی را دانلود کنید.'; return; }
                throw error;
            }
            if (version !== generation) { await loaded.destroy(); return; }
            pdf = loaded;
            find('pdf-controls').hidden = false; controls(); await renderPage(version);
        } catch { if (version === generation) status.textContent = 'PDF باز نشد؛ فایل ممکن است خراب باشد یا نشست شما پایان یافته باشد. نسخه‌ی اصلی قابل دانلود است.'; }
    }
    select.addEventListener('change', () => void openSelected());
    previous.addEventListener('click', () => { if (pdf && !rendering && pageNumber > 1) { pageNumber--; void renderPage(generation); } });
    next.addEventListener('click', () => { if (pdf && !rendering && pageNumber < pdf.numPages) { pageNumber++; void renderPage(generation); } });
    zoom.addEventListener('change', () => { if (pdf && !rendering) void renderPage(generation); });
    const upload = find('source-upload-form');
    upload?.addEventListener('submit', async event => {
        event.preventDefault();
        const input = find('source-file'), file = input.files[0], button = upload.querySelector('button');
        if (!file || !file.size || file.size > 25 * 1024 * 1024) { status.textContent = 'فایل خالی یا بزرگ‌تر از ۲۵ مگابایت پذیرفته نمی‌شود.'; return; }
        button.disabled = true; status.textContent = 'در حال بارگذاری فایل… متن ترجمه حفظ می‌شود.';
        try {
            const response = await fetch(upload.action, { method: 'POST', body: new FormData(upload), credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if (response.redirected && new URL(response.url).pathname === '/Account/Login') throw new Error('نشست پایان یافته؛ در تب جدید وارد شوید و دوباره بارگذاری کنید.');
            if (!response.headers.get('content-type')?.includes('application/json')) throw new Error('بارگذاری تأیید نشد؛ ورود، اندازه‌ی فایل و ارتباط را بررسی کنید.');
            const result = await response.json();
            if (!response.ok) throw new Error(typeof result.message === 'string' ? result.message : 'فایل پذیرفته نشد.');
            const source = result.sourceFile;
            if (!guid.test(source?.id ?? '')) throw new Error('پاسخ بارگذاری قابل تأیید نیست.');
            const option = document.createElement('option'); option.value = source.id; option.textContent = source.originalFileName; option.dataset.type = source.contentType;
            select.append(option); select.value = source.id;
            const link = document.createElement('a'); link.href = sourceUrl(source.id, true); link.textContent = source.originalFileName;
            find('source-file-list').append(link); find('source-count').textContent = `${select.options.length - 1} فایل`;
            input.value = ''; await openSelected();
        } catch (error) { status.textContent = error.message || 'ارتباط برقرار نشد؛ متن ترجمه در همین صفحه حفظ شده است.'; }
        finally { button.disabled = false; }
    });
    if (select.options.length > 1) { select.selectedIndex = 1; void openSelected(); }
}
