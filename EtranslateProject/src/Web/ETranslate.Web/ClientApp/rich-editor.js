import ui from '../Resources/interface.tr.json' with { type: 'json' };
import { assetUrl } from './preview.js';
import { createEditorSession } from './editors/editor-session.js';
import { defaultEditorProvider } from './editors/default-provider.js';
import { supportsStoredContent } from './editors/document-contract.js';

// DOM orchestration only. All vendor-specific editing/selection/history code belongs in its provider.
export function startRichEditor(workspace, onChanged, preview) {
    const json = document.getElementById('document-json');
    const status = document.getElementById('editor-format-status');
    if (!supportsStoredContent(workspace.dataset.contentFormat, workspace.dataset.contentSchemaVersion) ||
        !supportsStoredContent(workspace.dataset.templateContentFormat, workspace.dataset.templateContentSchemaVersion)) {
        workspace.dataset.editable = 'false'; json.readOnly = true;
        const text = document.getElementById('translation-text'); if (text) text.readOnly = true;
        const save = document.querySelector('#draft-form button[type=submit]'); if (save) save.disabled = true;
        status.textContent = ui.BelgeIcerikSurumuDesteklenmiyor; return null;
    }
    if (workspace.dataset.richCompatible !== 'true') {
        status.textContent = ui.BuYapiDuzenleyicininKapsamiDisindadirOzgunJSON;
        return null;
    }
    let session;
    const cleanups = [];
    const destroy = () => { for (const cleanup of cleanups.splice(0)) cleanup(); session?.destroy(); };
    try {
        session = createEditorSession({
            provider: defaultEditorProvider, element: document.getElementById('rich-text'),
            contentJson: json.value, editable: workspace.dataset.editable === 'true',
            resolveAsset: id => assetUrl(workspace, id),
            labels: { text: ui.CeviriMetni, image: ui.SablonGorseli, missingImage: ui.SablonGorseliKullanilamiyor },
            onRejectedChange: () => { status.textContent = ui.BuDegisiklikIzinVerilenYapininDisindadirTablo; }
        });
        const buttons = Array.from(document.querySelectorAll('#rich-region .editor-toolbar button'));
        function updateToolbar() {
            for (const button of buttons) {
                const state = session.getCommandState(button.dataset.command);
                button.disabled = workspace.dataset.editable !== 'true' || !state.enabled;
                if (button.hasAttribute('aria-pressed')) button.setAttribute('aria-pressed', String(state.active));
            }
        }
        cleanups.push(session.onDocumentChange(source => { json.value = source; onChanged(); preview(); }));
        cleanups.push(session.onStateChange(updateToolbar));
        for (const button of buttons) {
            const keepSelection = event => event.preventDefault();
            const execute = () => {
                if (workspace.dataset.editable === 'true') session.execute(button.dataset.command);
                updateToolbar();
            };
            button.addEventListener('mousedown', keepSelection); button.addEventListener('click', execute);
            cleanups.push(() => { button.removeEventListener('mousedown', keepSelection); button.removeEventListener('click', execute); });
        }
        updateToolbar();
        for (const id of ['translation-text', 'legacy-format-notice']) {
            const element = document.getElementById(id); if (element) element.hidden = true;
        }
        for (const label of document.querySelectorAll('label[for="translation-text"], label[for="document-json"]')) label.hidden = true;
        json.hidden = true;
        document.querySelector('#draft-form input[name=mode]').value = 'rich';
        document.getElementById('rich-region').hidden = false;
        status.textContent = workspace.dataset.editable === 'true'
            ? ui.MetinBicimlendirmesiHerSurumleBirlikteKaydedilir : ui.SaltOkunurSurumDuzenlemeKapalidir;
        return Object.freeze({ ...session, destroy });
    } catch {
        destroy();
        status.textContent = ui.OzgunYapiKorunduDuzenleyiciAcamadiMetinAlanini;
        return null;
    }
}
