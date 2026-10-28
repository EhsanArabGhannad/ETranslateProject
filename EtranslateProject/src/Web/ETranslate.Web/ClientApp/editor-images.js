import ui from '../Resources/interface.tr.json' with { type: 'json' };
import { guid } from './document-schema.js';
import { validImageLayout } from './body-image.js';

export function startEditorImages(workspace, session, onBusy = () => {}) {
    const find = id => document.getElementById(id), panel = find('editor-images'); if (!panel) return () => {};
    const file = find('editor-image-file'), upload = find('editor-image-upload'), width = find('editor-image-width'), align = find('editor-image-align'), apply = find('editor-image-apply'), status = find('editor-image-status');
    let busy = false, disposed = false, signature, controller;
    const cleanups = [], on = (node, type, callback) => { node.addEventListener(type, callback); cleanups.push(() => node.removeEventListener(type, callback)); };
    const layout = () => ({ widthPercent: /^[1-9]\d{0,2}$/.test(width.value) ? Number(width.value) : NaN, align: align.value });
    function update() {
        const state = session.getCommandState('imageLayout'), insert = session.getCommandState('insertImage');
        const editable = workspace.dataset.editable === 'true' && !busy;
        const next = JSON.stringify([state.context, state.widthPercent, state.align]);
        if (next !== signature) { signature = next; width.value = state.context && state.widthPercent == null ? '' : String(state.widthPercent ?? 50); align.value = state.context ? state.align ?? '' : 'center'; }
        file.disabled = !editable || !insert.enabled; upload.disabled = file.disabled || !file.files.length;
        width.disabled = align.disabled = !editable || !state.enabled;
        apply.disabled = width.disabled || !validImageLayout(layout());
        width.setAttribute('aria-invalid', String(!validImageLayout(layout())));
    }
    on(file, 'change', update); on(width, 'input', update); on(align, 'change', update);
    on(width, 'keydown', event => { if (event.key === 'Enter') event.preventDefault(); });
    for (const button of [upload, apply]) on(button, 'mousedown', event => event.preventDefault());
    on(apply, 'click', () => {
        if (busy || workspace.dataset.editable !== 'true') return;
        if (!validImageLayout(layout())) { status.textContent = ui.EditorImageInvalidWidth; return; }
        if (session.execute('imageLayout', layout())) status.textContent = ui.EditorImageLayoutSaved;
        update();
    });
    on(upload, 'click', async () => {
        if (busy || workspace.dataset.editable !== 'true' || !session.getCommandState('insertImage').enabled) return;
        const selected = file.files[0];
        if (!selected || !['image/png', 'image/jpeg'].includes(selected.type) || selected.size <= 0 || selected.size > 5 * 1024 * 1024) { status.textContent = ui.EditorImageInvalidFile; return; }
        const bookmark = session.captureImageInsertion(); if (!bookmark) return;
        busy = true; session.setEditable(false); onBusy(true); update(); status.textContent = ui.EditorImageUploading;
        controller = new AbortController(); const timer = setTimeout(() => controller.abort(), 45000);
        try {
            const url = new URL(workspace.dataset.imageUploadUrl, location.href);
            if (url.origin !== location.origin) throw new Error();
            const form = find('draft-form'), body = new FormData();
            for (const key of ['tenantId', 'jobId', 'expectedRevision', '__RequestVerificationToken']) if (form.elements[key]) body.set(key, form.elements[key].value);
            body.set('file', selected, selected.name);
            const response = await fetch(url, { method: 'POST', body, credentials: 'same-origin', headers: { Accept: 'application/json' }, signal: controller.signal });
            if (response.status === 409) { workspace.dataset.editable = 'false'; status.textContent = ui.SurumVeyaIncelemeDurumuDegistiMetninizVe; return; }
            if (response.redirected || !response.headers.get('content-type')?.includes('json')) throw new Error();
            const result = await response.json();
            if (!response.ok) { status.textContent = typeof result.message === 'string' ? result.message : ui.EditorImageFailed; return; }
            if (!guid.test(result.id ?? '')) throw new Error();
            // Verify the private response can actually render before adding a persistent node.
            const image = new Image(); image.src = `/Workspace/BodyImage?tenantId=${workspace.dataset.tenantId}&jobId=${workspace.dataset.jobId}&assetId=${result.id}`;
            if (controller.signal.aborted) throw new Error();
            controller.signal.addEventListener('abort', () => { image.src = ''; }, { once: true });
            await image.decode(); if (disposed || workspace.dataset.editable !== 'true') return;
            session.setEditable(true);
            if (!session.execute('insertImage', { assetId: result.id, bookmark })) throw new Error();
            file.value = ''; status.textContent = ui.EditorImageAdded;
        } catch { if (!disposed) status.textContent = ui.EditorImageFailed; }
        finally {
            clearTimeout(timer); session.releaseImageInsertion(bookmark); busy = false;
            if (!disposed) { session.setEditable(workspace.dataset.editable === 'true'); onBusy(false); update(); }
        }
    });
    cleanups.push(session.onStateChange(update)); update(); status.textContent = ui.EditorImageSelect;
    return () => { disposed = true; controller?.abort(); for (const cleanup of cleanups) cleanup(); };
}
