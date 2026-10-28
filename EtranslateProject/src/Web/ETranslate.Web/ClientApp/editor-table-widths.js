import ui from '../Resources/interface.tr.json' with { type: 'json' };
import { formatUi } from './ui-format.js';
import { equalColumnPercentages, validColumnWidths } from './table-widths.js';

export function startEditorTableWidths(workspace, session) {
    const panel = document.getElementById('editor-table-widths'); if (!panel) return () => {};
    const fields = document.getElementById('editor-column-inputs'), status = document.getElementById('editor-column-status');
    const apply = document.getElementById('editor-column-apply'), reset = document.getElementById('editor-column-reset');
    let context, inputs = [], state;
    function values() { return inputs.map(input => /^[1-9]\d{0,2}$/.test(input.value) ? Number(input.value) : NaN); }
    function eligibility() {
        const widths = values(), allowed = workspace.dataset.editable === 'true' && state.enabled;
        const valid = state.columns > 0 && validColumnWidths(widths, state.columns);
        for (const input of inputs) input.disabled = !allowed;
        apply.disabled = !allowed || !valid; reset.disabled = !allowed || state.value === null;
        status.textContent = !state.columns ? ui.EditorColumnSelectTable : valid ? formatUi(ui.EditorColumnSum, 100) : ui.EditorColumnInvalid;
        for (const [index, input] of inputs.entries()) input.setAttribute('aria-invalid', String(!Number.isInteger(widths[index]) || widths[index] < 1 || widths[index] > 100));
    }
    function update() {
        state = session.getCommandState('tableColumnWidths');
        const signature = JSON.stringify([state.context, state.value, state.columns]);
        if (context !== signature) {
            context = signature; inputs = []; fields.replaceChildren();
            const widths = state.columns ? state.value ?? equalColumnPercentages(state.columns) : [];
            for (const [index, width] of widths.entries()) {
                const label = document.createElement('label'), input = document.createElement('input');
                label.textContent = formatUi(ui.EditorColumnNumber, index + 1); input.type = 'text'; input.inputMode = 'numeric'; input.maxLength = 3;
                input.value = String(width); input.autocomplete = 'off'; input.setAttribute('aria-describedby', 'editor-column-status');
                label.append(input); fields.append(label); inputs.push(input);
            }
        }
        eligibility();
    }
    const onInput = () => { state = session.getCommandState('tableColumnWidths'); eligibility(); };
    const onKey = event => { if (event.key === 'Enter') { event.preventDefault(); onInput(); } };
    const set = widths => { if (workspace.dataset.editable === 'true') session.execute('tableColumnWidths', widths); update(); };
    const onApply = () => set(values()), onReset = () => set(null), keepSelection = event => event.preventDefault();
    fields.addEventListener('input', onInput); fields.addEventListener('keydown', onKey);
    apply.addEventListener('mousedown', keepSelection); reset.addEventListener('mousedown', keepSelection);
    apply.addEventListener('click', onApply); reset.addEventListener('click', onReset);
    const unsubscribe = session.onStateChange(update); update();
    return () => { unsubscribe(); fields.removeEventListener('input', onInput); fields.removeEventListener('keydown', onKey);
        apply.removeEventListener('mousedown', keepSelection); reset.removeEventListener('mousedown', keepSelection);
        apply.removeEventListener('click', onApply); reset.removeEventListener('click', onReset); fields.replaceChildren(); };
}
