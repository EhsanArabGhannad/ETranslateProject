import ui from '../Resources/interface.tr.json' with { type: 'json' };
import { formatUi } from './ui-format.js';
import { tableOptions, defaultTableOptions, tableLimits } from './table-options.js';

export function startEditorTableTools(workspace, session, refreshToolbar) {
    const panel = document.getElementById('editor-table-settings');
    // Old hosts can still use the default 3x3 command without a size picker.
    if (!panel) return { getOptions: () => ({ ...defaultTableOptions }), update() {}, destroy() {} };
    const find = id => document.getElementById(id), rows = find('editor-table-rows'), columns = find('editor-table-columns');
    const header = find('editor-table-header'), grid = find('editor-table-grid'), status = find('editor-table-size'), error = find('editor-table-error');
    const cells = [], cleanups = [], height = 6, width = 8;
    const on = (element, event, handler) => { element.addEventListener(event, handler); cleanups.push(() => element.removeEventListener(event, handler)); };
    const dimension = control => /^[1-9]\d?$/.test(control.value) ? Number(control.value) : NaN;
    const getOptions = () => tableOptions({ rows: dimension(rows), columns: dimension(columns), withHeaderRow: header.checked });
    function paint(r, c) {
        for (const cell of cells) {
            cell.dataset.preview = String(Number(cell.dataset.rows) <= r && Number(cell.dataset.columns) <= c);
            cell.dataset.header = String(header.checked && cell.dataset.rows === '1');
        }
    }
    function update() {
        const allowed = workspace.dataset.editable === 'true' && session.getCommandState('insertTable').enabled;
        const options = getOptions();
        rows.disabled = columns.disabled = header.disabled = !allowed;
        for (const [control, max] of [[rows, tableLimits.rows], [columns, tableLimits.columns]])
            control.setAttribute('aria-invalid', String(!Number.isInteger(dimension(control)) || dimension(control) < 1 || dimension(control) > max));
        error.hidden = options !== null; error.textContent = options ? '' : ui.EditorTableInvalid;
        status.textContent = options ? formatUi(ui.EditorTableSize, options.rows, options.columns) + ' · ' + (options.withHeaderRow ? ui.EditorTableWithHeader : ui.EditorTableWithoutHeader) : '';
        const insert = document.querySelector('#rich-region button[data-command=insertTable]');
        if (insert) insert.textContent = options ? formatUi(ui.EditorInsertTableSize, options.rows, options.columns) : ui.EditorInsertTable;
        const active = cells.find(cell => Number(cell.dataset.rows) === options?.rows && Number(cell.dataset.columns) === options?.columns) ?? cells[0];
        for (const cell of cells) {
            cell.disabled = !allowed; cell.tabIndex = cell === active ? 0 : -1;
            cell.setAttribute('aria-pressed', String(Number(cell.dataset.rows) === options?.rows && Number(cell.dataset.columns) === options?.columns));
        }
        paint(options?.rows ?? 0, options?.columns ?? 0);
    }
    for (let r = 1; r <= height; r++) for (let c = 1; c <= width; c++) {
        const cell = document.createElement('button'); cell.type = 'button'; cell.dataset.rows = String(r); cell.dataset.columns = String(c);
        cell.setAttribute('aria-label', formatUi(ui.EditorTableSize, r, c)); cell.title = cell.getAttribute('aria-label'); cells.push(cell); grid.append(cell);
        on(cell, 'mousedown', event => event.preventDefault());
        on(cell, 'pointerenter', () => { if (!cell.disabled) paint(r, c); });
        on(cell, 'focus', () => paint(r, c));
        on(cell, 'click', () => {
            if (workspace.dataset.editable !== 'true' || !session.getCommandState('insertTable').enabled) return;
            rows.value = String(r); columns.value = String(c); refreshToolbar();
        });
        on(cell, 'keydown', event => {
            const target = { ArrowRight: [r, c + 1], ArrowLeft: [r, c - 1], ArrowDown: [r + 1, c], ArrowUp: [r - 1, c], Home: [r, 1], End: [r, width] }[event.key];
            if (!target) return;
            event.preventDefault();
            const index = (Math.max(1, Math.min(height, target[0])) - 1) * width + Math.max(1, Math.min(width, target[1])) - 1;
            for (const item of cells) item.tabIndex = item === cells[index] ? 0 : -1;
            cells[index].focus();
        });
    }
    on(grid, 'pointerleave', update);
    for (const control of [rows, columns]) {
        on(control, 'input', refreshToolbar);
        // Editing the size must never submit/save the translation with Enter.
        on(control, 'keydown', event => { if (event.key === 'Enter') { event.preventDefault(); refreshToolbar(); } });
    }
    on(header, 'change', refreshToolbar);
    return { getOptions, update, destroy() { for (const cleanup of cleanups) cleanup(); grid.replaceChildren(); } };
}
