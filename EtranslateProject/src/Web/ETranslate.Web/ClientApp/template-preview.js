import { createPreview, assetUrl } from './preview.js';
import { alignments, plainRegion, previewHeader } from './template-presentation.js';

const find = id => document.getElementById(id), form = find('template-edit-form'), workspace = find('template-live-preview');
if (form && workspace) {
    const original = { header: find('template-header').value, footer: find('template-footer').value, body: find('document-json').value, watermark: find('template-watermark').value };
    const imageToken = '00000000-0000-0000-0000-000000000001';
    const render = createPreview(workspace, id => id === imageToken ? find('template-logo-preview').getAttribute('src') : assetUrl(workspace, id));
    const editable = part => workspace.dataset[`${part}Editable`] === 'true';
    function lineControls(part) {
        if (!editable(part)) return;
        const source = find(`${part}-text`), input = find(`${part}-alignments`), container = find(`${part}-line-controls`);
        let existing = []; try { const parsed = JSON.parse(input.value || '[]'); if (Array.isArray(parsed)) existing = parsed; } catch { /* Server validation message remains authoritative. */ }
        container.replaceChildren();
        const lines = source.value === '' ? [] : source.value.split('\n');
        const values = lines.map((_, i) => alignments.includes(existing[i]) ? existing[i] : 'auto');
        input.value = JSON.stringify(values);
        for (let index = 0; index < lines.length; index++) {
            const label = document.createElement('label'), text = document.createElement('span'), select = document.createElement('select');
            text.textContent = `خط ${index + 1}: ${lines[index].slice(0, 60) || '(خالی)'}`;
            select.setAttribute('aria-label', `تراز ${part === 'header' ? 'سربرگ' : 'پابرگ'} خط ${index + 1}`);
            for (const [value, caption] of [['auto', 'خودکار'], ['left', 'چپ'], ['center', 'وسط'], ['right', 'راست']]) {
                const option = document.createElement('option'); option.value = value; option.textContent = caption; select.append(option);
            }
            select.value = values[index]; select.disabled = !workspace.dataset.canSave || workspace.dataset.canSave !== 'true';
            select.addEventListener('change', () => { values[index] = select.value; input.value = JSON.stringify(values); input.dispatchEvent(new Event('input', { bubbles: true })); });
            label.append(text, select); container.append(label);
        }
    }
    function update() {
        try {
            const uploaded = find('template-logo-preview').getAttribute('src');
            const pending = find('Form_PendingAssetId').value || null;
            if (editable('header')) find('template-header').value = JSON.stringify(previewHeader(find('header-text').value, JSON.parse(find('header-alignments').value || '[]'), original.header ? JSON.parse(original.header) : null,
                { replace: find('Form_LogoMode').value === 'Replace', imageId: uploaded ? imageToken : pending,
                    imageAlign: find('image-alignment').value, imageLayout: find('image-layout').value }));
            if (editable('footer')) find('template-footer').value = JSON.stringify(plainRegion(find('footer-text').value, 'footer', JSON.parse(find('footer-alignments').value || '[]')));
            if (editable('body')) find('document-json').value = JSON.stringify(plainRegion(find('starter').value));
            if (editable('watermark')) {
                const mark = original.watermark ? JSON.parse(original.watermark) : {};
                mark.text = find('watermark').value; find('template-watermark').value = JSON.stringify(mark);
            }
            render();
            const page = find('template-preview-paper');
            const sizes = { A4: [210, 297], A5: [148, 210], Letter: [215.9, 279.4], Legal: [215.9, 355.6] };
            const size = sizes[find('Form_PageSize').value] || sizes.A4;
            const landscape = find('Form_Orientation').value === 'Landscape';
            const width = size[landscape ? 1 : 0], height = size[landscape ? 0 : 1];
            page.style.setProperty('--preview-mm', String(100 / width));
            page.style.aspectRatio = `${width} / ${height}`;
            for (const side of ['Top', 'Right', 'Bottom', 'Left']) {
                const margin = Number(find(`Form_Margin${side}`).value);
                if (Number.isFinite(margin) && margin >= 5 && margin <= 50) page.style[`padding${side}`] = `${margin / width * 100}%`;
            }
            find('template-preview-size').textContent = `${width} × ${height} mm — پیش‌نمایش تقریبی، بدون صفحه‌بندی`;
            const warning = ['header', 'footer', 'body', 'watermark', 'layout'].some(p => !editable(p));
            if (warning) find('preview-notice').textContent += ' بعضی بخش‌ها پیچیده یا ناشناخته‌اند؛ نمایش آن‌ها ممکن است کامل نباشد و اصل آن‌ها بدون تغییر حفظ می‌شود.';
        } catch { find('preview-notice').textContent = 'پیش‌نمایش کامل نیست؛ ورودی‌ها را بررسی کنید.'; }
    }
    for (const part of ['header', 'footer']) { lineControls(part); find(`${part}-text`).addEventListener('input', () => lineControls(part)); }
    form.addEventListener('input', update); form.addEventListener('change', update);
    // site.js creates/revokes the local preview URL before this observer fires.
    new MutationObserver(update).observe(find('template-logo-preview'), { attributes: true, attributeFilter: ['src', 'hidden'] });
    update();
}
