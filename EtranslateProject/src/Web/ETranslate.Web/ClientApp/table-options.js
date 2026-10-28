// Application command input, independent of the editor engine and saved document schema.
export const tableLimits = Object.freeze({ rows: 50, columns: 12 });
export const defaultTableOptions = Object.freeze({ rows: 3, columns: 3, withHeaderRow: true });

export function tableOptions(value = defaultTableOptions) {
    if (!value || typeof value !== 'object' || Array.isArray(value) ||
        Object.keys(value).length !== 3 || !['rows', 'columns', 'withHeaderRow'].every(key => Object.hasOwn(value, key)) ||
        !Number.isInteger(value.rows) || value.rows < 1 || value.rows > tableLimits.rows ||
        !Number.isInteger(value.columns) || value.columns < 1 || value.columns > tableLimits.columns ||
        typeof value.withHeaderRow !== 'boolean') return null;
    return { rows: value.rows, columns: value.columns, withHeaderRow: value.withHeaderRow };
}
