import ui from '../Resources/interface.tr.json' with { type: 'json' };
import { createPreview } from './preview.js';
import { startRichEditor } from './rich-editor.js';
import { startSources } from './sources.js';
import { isRichDocument } from './document-schema.js';
import { startPdfExport } from './pdf-export.js';
import { withEditorRequest } from './editor-request.js';

const find = id => document.getElementById(id), workspace = find('editor-workspace');
if (workspace) {
    const json = find('document-json'), text = find('translation-text'), form = find('draft-form');
    const preview = createPreview(workspace);
    let dirty = false, saving = false, navigating = false, uploading = false;
    const changed = () => { dirty = true; find('save-state').textContent = ui.KaydedilmemisDegisiklikler; };
    const editor = startRichEditor(workspace, changed, preview, busy => {
        uploading = busy; form.querySelector('button[type=submit]').disabled = busy || workspace.dataset.editable !== 'true';
    });
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
        if (saving || uploading || workspace.dataset.editable !== 'true') return;
        const mode = form.elements.mode.value;
        try {
            if (editor) json.value = editor.getDocumentJson();
            const document = JSON.parse(json.value);
            if (!document || typeof document !== 'object' || json.value.length > 2000000 || mode === 'rich' && !isRichDocument(document,10)) throw new Error();
        } catch { find('save-state').textContent = ui.BelgeYapisiGecersizVeyaKaydedilemiyorDegisikliklerKorunmustur; return; }
        const button = form.querySelector('button[type=submit]');
        saving = true; button.disabled = true; find('save-state').textContent = ui.Kaydediliyor;
        // Freeze rich edits while awaiting the response: post-submit typing must not disappear on navigation.
        editor?.setEditable(false); if (text) text.readOnly = true; json.readOnly = true;
        try {
            await withEditorRequest(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', headers: { Accept: 'application/json' } }, async response => {
                const destination = new URL(response.url);
                if (response.ok && response.redirected && destination.origin === location.origin && destination.pathname === '/Workspace/Editor') {
                    dirty = false; navigating = true; location.assign(destination.href); return;
                }
                if (response.status === 409) {
                    find('save-state').textContent = ui.SurumVeyaIncelemeDurumuDegistiMetninizVe;
                    dirty = true; workspace.dataset.editable = 'false'; return;
                }
                if (response.status === 402 || response.status === 403) {
                    find('save-state').textContent = ui.EditorWriteAccessLost;
                    dirty = true; workspace.dataset.editable = 'false'; return;
                }
                find('save-state').textContent = destination.pathname === '/Account/Login' || response.status === 401
                    ? ui.OturumSonaErdiYeniSekmedeGirisYapip2
                    : ui.KayitDogrulanamadiDegisikliklerKorunduGirdileriVeHizmet;
                if (response.status === 503 && response.headers.get('content-type')?.includes('json')) {
                    const result = await response.json();
                    if (typeof result?.message === 'string') find('save-state').textContent += ' ' + result.message;
                }
            });
        } catch { find('save-state').textContent = ui.EditorRequestUnconfirmed; }
        finally {
            saving = false;
            if (!navigating && workspace.dataset.editable === 'true') {
                button.disabled = false; editor?.setEditable(true); if (text) text.readOnly = false; json.readOnly = false;
            }
        }
        if (!navigating) dirty = true;
    });
    startSources(workspace); preview();
    startPdfExport();
    for (const reviewForm of document.querySelectorAll('.review-form')) {
        reviewForm.addEventListener('submit', async event => {
            event.preventDefault();
            const status = find('review-status');
            if (dirty || saving || uploading) {
                status.textContent = ui.OnceMetinDegisiklikleriniKaydedinYalnizcaKayitliSurum; return;
            }
            const controls = Array.from(document.querySelectorAll('.review-form button'));
            const previousDisabled = controls.map(button => button.disabled);
            const saveButton = form.querySelector('button[type=submit]');
            const editable = workspace.dataset.editable === 'true';
            const data = new FormData(reviewForm);
            saving = true;
            controls.forEach(button => { button.disabled = true; }); saveButton.disabled = true;
            editor?.setEditable(false); if (text) text.readOnly = true; json.readOnly = true;
            status.textContent = ui.IncelemeDurumuKaydediliyor;
            let rejected = false;
            try {
                await withEditorRequest(reviewForm.action, { method: 'POST', body: data, credentials: 'same-origin', headers: { Accept: 'application/json' } }, async response => {
                    if (response.status === 401 || response.redirected && new URL(response.url).pathname === '/Account/Login')
                        throw new Error(ui.OturumSonaErdiYeniSekmedeGirisYapin2);
                    if (!response.headers.get('content-type')?.includes('json')) throw new Error(ui.IncelemeKaydiDogrulanamadiGuncelDurumuKontrolEdin);
                    const result = await response.json();
                    if ([409, 402, 403].includes(response.status)) {
                        rejected = true;
                        workspace.dataset.editable = 'false';
                        status.textContent = result.message || ui.IncelemeKaydedilemedi;
                        return;
                    }
                    if (!response.ok) throw new Error(result.message || ui.IncelemeKaydedilemedi);
                    navigating = true; location.reload();
                });
            } catch (error) { status.textContent = error instanceof TypeError || error instanceof SyntaxError || error.name === 'AbortError' ? ui.EditorRequestUnconfirmed : error.message || ui.BaglantiKurulamadiDurumuYeniSekmedeKontrolEdin; }
            finally {
                if (!navigating) {
                    saving = false;
                    controls.forEach((button, index) => { button.disabled = rejected || previousDisabled[index]; });
                    const canEdit = editable && !rejected;
                    saveButton.disabled = !canEdit; editor?.setEditable(canEdit);
                    if (text) text.readOnly = !canEdit; json.readOnly = !canEdit;
                }
            }
        });
    }
}
