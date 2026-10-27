import ui from '../Resources/interface.tr.json' with { type: 'json' };
import { assetUrl } from './preview.js';
import { createEditorSession } from './editors/editor-session.js';
import { defaultEditorProvider } from './editors/default-provider.js';
import { supportsStoredContent, storedDocumentFormat } from './editors/document-contract.js';
import { startEditorView } from './editor-view.js';

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
            contentFormat: storedDocumentFormat(workspace.dataset.contentFormat,workspace.dataset.contentSchemaVersion),
            resolveAsset: id => assetUrl(workspace, id),
            labels: { text: ui.CeviriMetni, image: ui.SablonGorseli, missingImage: ui.SablonGorseliKullanilamiyor },
            onRejectedChange: () => { status.textContent = ui.BuDegisiklikIzinVerilenYapininDisindadirTablo; }
        });
        const buttons = Array.from(document.querySelectorAll('#rich-region button[data-command]'));
        const styles=Array.from(document.querySelectorAll('#rich-region select[data-style-command]'));
        function updateToolbar() {
            for (const button of buttons) {
                const state = session.getCommandState(button.dataset.command);
                button.disabled = workspace.dataset.editable !== 'true' || !state.enabled;
                if (button.hasAttribute('aria-pressed')) button.setAttribute('aria-pressed', String(state.active));
            }
            for(const control of styles){const state=session.getCommandState(control.dataset.styleCommand);
                control.disabled=workspace.dataset.editable!=='true'||!state.enabled;
                const value=state.mixed?'__mixed__':String(state.value??'');
                let custom=control.querySelector('[data-custom-value]');custom?.remove();
                if(value!==''&&![...control.options].some(option=>option.value===value)){
                    custom=document.createElement('option');custom.dataset.customValue='true';custom.value=value;custom.textContent=value;control.append(custom);
                }
                control.value=value;
            }
        }
        cleanups.push(session.onDocumentChange(source => {
            json.value=source;
            // The working copy may be newer than the immutable revision shown on this page.
            workspace.dataset.workingContentFormat=session.format.id;
            workspace.dataset.workingContentSchemaVersion=String(session.format.version);
            onChanged();preview();
        }));
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
        for(const control of styles){const apply=()=>{
            if(workspace.dataset.editable!=='true'||control.value==='__mixed__')return;
            const value=control.value===''?null:['fontSize','lineHeight','indentLevel'].includes(control.dataset.styleCommand)?Number(control.value):control.value;
            session.execute(control.dataset.styleCommand,value);updateToolbar();
        };control.addEventListener('change',apply);cleanups.push(()=>control.removeEventListener('change',apply));}
        updateToolbar();
        for (const id of ['translation-text', 'legacy-format-notice']) {
            const element = document.getElementById(id); if (element) element.hidden = true;
        }
        for (const label of document.querySelectorAll('label[for="translation-text"], label[for="document-json"]')) label.hidden = true;
        json.hidden = true;
        document.querySelector('#draft-form input[name=mode]').value = 'rich';
        document.getElementById('rich-region').hidden = false;
        cleanups.push(startEditorView(workspace, session));
        status.textContent = workspace.dataset.editable === 'true'
            ? ui.MetinBicimlendirmesiHerSurumleBirlikteKaydedilir : ui.SaltOkunurSurumDuzenlemeKapalidir;
        return Object.freeze({ ...session, destroy });
    } catch {
        destroy();
        status.textContent = ui.OzgunYapiKorunduDuzenleyiciAcamadiMetinAlanini;
        return null;
    }
}
