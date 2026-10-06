import { guid } from './document-schema.js';

export function startPdfExport() {
    const form = document.getElementById('draft-pdf-form');
    if (!form) return;
    const button = form.querySelector('button'), status = document.getElementById('draft-pdf-status');
    let busy = false;
    form.addEventListener('submit', async event => {
        event.preventDefault(); if (busy) return;
        busy = true; button.disabled = true;
        status.textContent = 'در حال ساخت PDF از نسخه‌ی ذخیره‌شده… متن ویرایشگر تغییر نمی‌کند.';
        try {
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', headers: { Accept: 'application/json' } });
            if (response.status === 401 || response.redirected && new URL(response.url).pathname === '/Account/Login')
                throw new Error('نشست پایان یافته؛ در تب جدید وارد شوید. متن ویرایشگر حفظ شده است.');
            if (!response.headers.get('content-type')?.includes('json')) throw new Error('ساخت PDF تأیید نشد؛ وضعیت سرویس را بررسی کنید.');
            const result = await response.json();
            if (!response.ok) throw new Error(result.message || 'ساخت PDF انجام نشد.');
            const pdf = result.pdf, url = new URL(form.dataset.downloadBase, location.origin);
            if (!guid.test(pdf?.id ?? '') || pdf.kind !== 'DraftUnsigned' || !/^[a-f0-9]{64}$/.test(pdf.sha256 ?? '') ||
                pdf.revisionNumber !== Number(form.elements.revisionNumber.value) || url.origin !== location.origin || url.pathname !== '/Workspace/DraftPdf')
                throw new Error('پاسخ PDF قابل تأیید نیست.');
            url.searchParams.set('pdfId', pdf.id);
            const list = document.getElementById('draft-pdf-list');
            if (!list.querySelector(`[data-pdf-id="${pdf.id}"]`)) {
                const row = document.createElement('div'); row.dataset.pdfId = pdf.id;
                const link = document.createElement('a'); link.href = url.href; link.textContent = `دانلود PDF پیش‌نویس نسخه‌ی ${pdf.revisionNumber}`;
                const hash = document.createElement('small'); hash.dir = 'ltr'; hash.textContent = `SHA-256: ${pdf.sha256}`;
                row.append(link, hash); list.prepend(row);
            }
            const select = document.getElementById('review-pdf');
            if (select && Number(select.dataset.revision) === pdf.revisionNumber) {
                if (!Array.from(select.options).some(option => option.value === pdf.id)) {
                    const option = document.createElement('option'); option.value = pdf.id;
                    option.textContent = `نسخه ${pdf.revisionNumber} · PDF پیش‌نویس`; select.prepend(option);
                }
                document.getElementById('review-submit').disabled = false;
                const notice = document.getElementById('review-pdf-needed'); if (notice) notice.hidden = true;
            }
            status.textContent = `PDF پیش‌نویس نسخه‌ی ${pdf.revisionNumber} آماده است؛ فایل بدون امضای معتبر است. تغییرات ذخیره‌نشده در ویرایشگر حفظ شده‌اند.`;
        } catch (error) { status.textContent = error.message || 'ارتباط برقرار نشد؛ متن ویرایشگر حفظ شده است.'; }
        finally { busy = false; button.disabled = false; }
    });
}
