(() => {
    'use strict';
    const find = id => document.getElementById(id);
    const conditional = (selectId, containerId, value) => {
        const select = find(selectId), container = find(containerId);
        if (!select || !container) return;
        const update = () => { container.hidden = select.value !== value; };
        select.addEventListener('change', update); update();
    };
    conditional('notary', 'notary-options', 'Required');
    conditional('acceptance', 'other-options', 'Other');
    const template = find('template-select');
    if (template) {
        const update = () => { find('template-revision').value = template.selectedOptions[0]?.dataset.revision ?? ''; };
        template.addEventListener('change', update); update();
    }
    const picker = find('template-picker'), editForm = find('template-edit-form');
    if (picker && editForm) {
        let dirty = false, previous = picker.value, previewUrl = null;
        editForm.addEventListener('input', () => { dirty = true; });
        editForm.addEventListener('submit', () => { dirty = false; find('template-save').disabled = true; });
        window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
        picker.addEventListener('change', () => {
            if (dirty && !window.confirm('تغییرات فرم ذخیره نشده‌اند. قالب دیگری باز شود؟')) { picker.value = previous; return; }
            dirty = false; previous = picker.value; find('template-picker-form').requestSubmit();
        });
        const file = find('template-logo'), preview = find('template-logo-preview');
        file.addEventListener('change', () => {
            if (previewUrl) URL.revokeObjectURL(previewUrl);
            previewUrl = null;
            preview.removeAttribute('src'); preview.hidden = true; file.setCustomValidity('');
            const image = file.files[0]; if (!image) return;
            if (!['image/png', 'image/jpeg'].includes(image.type) || image.size <= 0 || image.size > 5 * 1024 * 1024) {
                file.setCustomValidity('تصویر باید PNG یا JPEG و حداکثر ۵ مگابایت باشد.'); file.reportValidity(); return;
            }
            previewUrl = URL.createObjectURL(image); preview.src = previewUrl; preview.hidden = false;
        });
        window.addEventListener('pagehide', () => { if (previewUrl) URL.revokeObjectURL(previewUrl); });
    }
})();
