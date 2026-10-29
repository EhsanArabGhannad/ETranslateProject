import ui from '../Resources/interface.tr.json' with { type: 'json' };

const messages = { fonts: 'EditorPasteFonts', formatting: 'EditorPasteFormatting', layout: 'EditorPasteLayout', images: 'EditorPasteImages', links: 'EditorPasteLinks', table: 'EditorPasteTable', lists: 'EditorPasteLists', paragraphContext: 'EditorPasteParagraphContext' };
export function startEditorClipboard(workspace, session) {
    const mode = document.getElementById('editor-paste-mode'), status = document.getElementById('editor-paste-status');
    if (!mode || !status) return () => {};
    let signature;
    function update() {
        const state = session.getPasteState(); mode.disabled = !state.supported || !state.editable || workspace.dataset.editable !== 'true'; mode.value = state.mode;
        const next = JSON.stringify(state.report); if (next === signature) return; signature = next;
        const report = state.report; status.hidden = !report;
        if (!report) return;
        status.dataset.outcome = report.outcome;
        const error = { limit: ui.EditorPasteLimit, context: ui.EditorPasteContext, empty: ui.EditorPasteEmpty, invalid: ui.EditorPasteInvalid };
        const text = report.outcome === 'rejected' ? error[report.error] ?? ui.EditorPasteInvalid : report.plain ? ui.EditorPastePlainDone : report.internal ? ui.EditorPasteInternalDone : ui.EditorPasteFormattedDone;
        status.textContent = [text, ...report.warnings.map(code => ui[messages[code]])].join(' ');
    }
    const change = () => { session.setPasteMode(mode.value); update(); };
    mode.addEventListener('change', change); const unsubscribe = session.onStateChange(update); update();
    return () => { unsubscribe(); mode.removeEventListener('change', change); };
}
