import ui from '../Resources/interface.tr.json' with { type: 'json' };
import { formatUi } from './ui-format.js';

export function startEditorCellStyle(workspace, session) {
    const panel = document.getElementById('editor-cell-style'); if (!panel) return () => {};
    const scope = document.getElementById('editor-cell-scope'), reset = document.getElementById('editor-cell-reset'), count = document.getElementById('editor-cell-count');
    const controls = [...panel.querySelectorAll('select[data-cell-field]')], colors = [...panel.querySelectorAll('input[data-cell-color]')], cleanups = [];
    const on = (element, event, callback) => { element.addEventListener(event, callback); cleanups.push(() => element.removeEventListener(event, callback)); };
    function update() {
        const state = session.getCommandState('tableCellStyle'), group = state[scope.value] ?? { count: 0, values: {}, mixed: {} };
        const allowed = workspace.dataset.editable === 'true' && state.enabled;
        scope.disabled = !(state.table?.count > 0); reset.disabled = !allowed || !group.custom;
        count.textContent = group.count ? formatUi(ui.EditorCellCount, group.count) : ui.EditorColumnSelectTable;
        for (const control of controls) {
            control.disabled = !allowed; const field = control.dataset.cellField, value = group.mixed[field] ? '__mixed__' : String(group.values[field] ?? '');
            control.querySelector('[data-custom-value]')?.remove();
            if (![...control.options].some(option => option.value === value)) { const option = document.createElement('option'); option.value = value; option.textContent = value; option.dataset.customValue = 'true'; control.append(option); }
            control.value = value;
        }
        for (const control of colors) { control.disabled = !allowed; control.value = group.values[control.dataset.cellColor] ?? (control.dataset.cellColor === 'backgroundColor' ? '#ffffff' : '#87969c'); }
    }
    function apply(field, value) { if (workspace.dataset.editable === 'true') session.execute('tableCellStyle', { scope: scope.value, field, value }); update(); }
    on(scope, 'change', update);
    for (const control of controls) on(control, 'change', () => { if (control.value !== '__mixed__') apply(control.dataset.cellField, control.value === '' ? null : control.dataset.cellField === 'borderWidthPt' ? Number(control.value) : control.value); });
    for (const control of colors) on(control, 'change', () => apply(control.dataset.cellColor, control.value));
    on(reset, 'mousedown', event => event.preventDefault()); on(reset, 'click', () => apply('reset', null));
    cleanups.push(session.onStateChange(update)); update();
    return () => { for (const cleanup of cleanups) cleanup(); };
}
