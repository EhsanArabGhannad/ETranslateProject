import { createPreview } from './preview.js';
import { startRichEditor } from './rich-editor.js';
import { startSources } from './sources.js';
import { isRichDocument } from './document-schema.js';

const find = id => document.getElementById(id), workspace = find('editor-workspace');
if (workspace) {
    const json = find('document-json'), text = find('translation-text'), form = find('draft-form');
    const preview = createPreview(workspace);
    let dirty = false, saving = false, navigating = false;
    const changed = () => { dirty = true; find('save-state').textContent = 'تغییرات ذخیره‌نشده'; };
    const editor = startRichEditor(workspace, changed, preview);
    if (!editor) {
        (text ?? json).addEventListener('input', () => {
            if (text) json.value = JSON.stringify({ type: 'doc', content: text.value.replace(/\r\n/g, '\n').split('\n')
                .map(line => ({ type: 'paragraph', content: line ? [{ type: 'text', text: line }] : [] })) });
            changed(); preview();
        });
    }
    window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (saving || workspace.dataset.editable !== 'true') return;
        const mode = form.elements.mode.value;
        try {
            const document = JSON.parse(json.value);
            if (!document || typeof document !== 'object' || json.value.length > 2000000 || mode === 'rich' && !isRichDocument(document)) throw new Error();
        } catch { find('save-state').textContent = 'ساختار سند معتبر یا قابل ذخیره نیست. تغییرات حفظ شده‌اند.'; return; }
        const button = form.querySelector('button[type=submit]');
        saving = true; button.disabled = true; find('save-state').textContent = 'در حال ذخیره…';
        // Freeze rich edits while awaiting the response: post-submit typing must not disappear on navigation.
        editor?.setEditable(false, false); if (text) text.readOnly = true; json.readOnly = true;
        const formatButtons = Array.from(document.querySelectorAll('.editor-toolbar button'));
        for (const control of formatButtons) control.disabled = true;
        try {
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin' });
            const destination = new URL(response.url);
            if (response.ok && response.redirected && destination.origin === location.origin && destination.pathname === '/Workspace/Editor') {
                dirty = false; navigating = true; location.assign(destination.href); return;
            }
            if (response.status === 409) {
                find('save-state').textContent = 'نسخه‌ی جدیدتری ثبت شده؛ متن و قالب‌بندی شما حفظ شده‌اند. آخرین نسخه را در تب جدید بررسی کنید.';
                dirty = true; workspace.dataset.editable = 'false'; return;
            }
            find('save-state').textContent = destination.pathname === '/Account/Login' || response.status === 401
                ? 'نشست پایان یافته. در تب جدید وارد شوید و دوباره ذخیره کنید؛ متن و قالب‌بندی حفظ شده‌اند.'
                : 'ذخیره تأیید نشد؛ تغییرات حفظ شده‌اند. ورودی‌ها و ارتباط سرویس را بررسی کنید.';
        } catch { find('save-state').textContent = 'ارتباط برقرار نشد؛ تغییرات هنوز در این صفحه‌اند. پیش از خروج یک کپی بگیرید.'; }
        finally {
            saving = false;
            if (!navigating && workspace.dataset.editable === 'true') {
                button.disabled = false; editor?.setEditable(true, false); if (text) text.readOnly = false; json.readOnly = false;
                for (const control of formatButtons) control.disabled = false;
            }
        }
        dirty = true;
    });
    startSources(workspace); preview();
}
