// Application-owned table fragmentation plan. No editor/provider or saved document nodes.
export const rowPlanVersion = 'native-table-row-plan-v1';
export function buildRowPlan(tables, pages, fingerprint) {
    if (!Number.isInteger(pages) || pages < 1 || pages > 50 || !Array.isArray(tables) || typeof fingerprint !== 'string')
        throw new Error('Unsupported row-plan input');
    return { version: rowPlanVersion, pages, fingerprint, tables: tables.map((table, index) => {
        if (!Array.isArray(table.rows) || !table.rows.length || typeof table.hasHeader !== 'boolean') throw new Error('Invalid table plan');
        const start = table.hasHeader ? 1 : 0, breaks = [];
        if (table.hasHeader && (table.rows[0].index !== 0 || !Number.isInteger(table.rows[0].page) || table.rows[0].page < 1 ||
            table.rows[0].page > pages || !Number.isInteger(table.rows[0].fragments) || table.rows[0].fragments < 1)) throw new Error('Invalid heading fragment');
        for (let i = start; i < table.rows.length; i++) {
            const row = table.rows[i];
            if (row.index !== i || row.fragments !== 1 || !Number.isInteger(row.page) || row.page < 1 || row.page > pages)
                throw new Error('A table body row is not one bounded native fragment');
            if (i > start) {
                const previous = table.rows[i - 1].page;
                if (row.page < previous || row.page > previous + 1) throw new Error('Non-contiguous table fragments');
                if (row.page > previous) breaks.push(i);
            }
        }
        return { index, hasHeader: table.hasHeader, rowCount: table.rows.length, rows: table.rows, breakBeforeRows: breaks };
    }) };
}
function contentFingerprint(root) {
    // Stale-layout guard only, not a cryptographic hash or an authenticity/security boundary.
    let hash = 2166136261;
    const value = root.textContent + [...root.querySelectorAll('table')].map(table =>
        [...table.rows].map(row => [...row.cells].map(cell => cell.tagName).join(',')).join(';')).join('|');
    for (let i = 0; i < value.length; i++) hash = Math.imul(hash ^ value.charCodeAt(i), 16777619);
    return `${value.length}:${hash >>> 0}`;
}
export function measureRowPlan(root, metrics) {
    const { contentWidth: width, contentHeight: height, columnGap: gap } = metrics ?? {};
    if (![width, height, gap].every(Number.isFinite) || width <= 0 || height <= 0 || gap < 0) throw new Error('Unsupported row-plan geometry');
    if (root.querySelector('table table')) throw new Error('Nested tables are not supported');
    if ([...root.querySelectorAll('th,td')].some(cell => cell.colSpan !== 1 || cell.rowSpan !== 1)) throw new Error('Merged cells are not supported');
    const saved = new Map();
    const remember = node => {
        if (!saved.has(node)) saved.set(node, { present: node.hasAttribute('style'),
            properties: [...node.style].map(name => [name, node.style.getPropertyValue(name), node.style.getPropertyPriority(name)]) });
    };
    try {
        remember(root);
        Object.assign(root.style, { width: `${width}px`, height: `${height}px`, columnCount: '1', columnWidth: `${width}px`, columnGap: `${gap}px`, columnFill: 'auto' });
        const tables = [...root.querySelectorAll('table')];
        for (const table of tables) {
            const first = table.rows[0];
            if (first?.cells.length && [...first.cells].every(cell => cell.tagName === 'TH')) {
                if (first.parentElement.tagName === 'THEAD') { remember(first.parentElement); first.parentElement.style.breakInside = 'avoid'; }
                else {
                    remember(first.parentElement); first.parentElement.style.display = 'contents';
                    remember(first); Object.assign(first.style, { display: 'table-header-group', breakInside: 'avoid' });
                }
            }
        }
        const origin = root.getBoundingClientRect().left, pitch = width + gap;
        const pages = Math.max(1, Math.ceil((root.scrollWidth + gap - 1) / pitch));
        const values = tables.map(table => ({
            hasHeader: !!table.rows[0]?.cells.length && [...table.rows[0].cells].every(cell => cell.tagName === 'TH'),
            rows: [...table.rows].map((row, index) => {
                const rects = [...row.getClientRects()];
                return { index, page: rects.length ? Math.max(1, Math.floor((rects[0].left - origin + 1) / pitch) + 1) : null, fragments: rects.length };
            })
        }));
        return buildRowPlan(values, pages, contentFingerprint(root));
    } finally {
        // CSSOM properties, not an inline-style attribute string (the lab has a strict CSP).
        for (const [node, style] of saved) {
            while (node.style.length) node.style.removeProperty(node.style.item(0));
            for (const [name, value, priority] of style.properties) node.style.setProperty(name, value, priority);
            if (!style.present) node.removeAttribute('style');
        }
    }
}
export function applyRowPlan(root, plan) {
    const tables = [...root.querySelectorAll('table')];
    if (plan?.version !== rowPlanVersion || plan.fingerprint !== contentFingerprint(root) || tables.length !== plan.tables.length)
        throw new Error('Row plan is stale or unsupported');
    // Validate everything before applying even one break. Plans are trusted, short-lived layout data.
    for (const table of plan.tables) {
        const rows = tables[table.index]?.rows;
        if (!rows || rows.length !== table.rowCount || table.breakBeforeRows.some(index => !Number.isInteger(index) || index < 1 || index >= rows.length))
            throw new Error('Row-plan shape changed');
    }
    for (const table of plan.tables) for (const index of table.breakBeforeRows) tables[table.index].rows[index].style.breakBefore = 'page';
    return plan;
}
export function prepareRowPlan(root, metrics) { return applyRowPlan(root, measureRowPlan(root, metrics)); }
