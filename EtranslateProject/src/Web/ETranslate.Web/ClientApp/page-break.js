export const pageBreakCommands = ['insertPageBreak', 'removePageBreak'];
export const maximumPageStarts = 49;
export const pageStartCount = doc => (doc.content ?? []).filter(node => node.attrs?.pageBreakBefore === true).length;
export function applyPageStart(element, attrs, label = '') {
    if (attrs.pageBreakBefore === true) { element.dataset.etPageStart = 'true'; if (label) element.title = label; }
}
