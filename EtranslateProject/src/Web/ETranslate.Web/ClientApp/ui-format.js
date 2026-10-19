// Replacements become textContent, never HTML. Values may contain any document language.
export function formatUi(pattern, ...values) {
    return pattern.replace(/\{(\d+)\}/g, (_, index) => String(values[Number(index)] ?? ''));
}
