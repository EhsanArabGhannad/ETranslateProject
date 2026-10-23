import { isRichDocument, normalizeEmptyText } from '../document-schema.js';

// Application vocabulary, not a vendor's unrestricted JSON. Existing API payloads stay unwrapped.
// Persisted format/version dispatch is a separate, server-coordinated change; this descriptor is local.
export const richDocumentV1 = Object.freeze({ id: 'etranslate.rich-document', version: 1 });
export const editorCommands = Object.freeze([
    'bold', 'italic', 'underline', 'paragraph', 'heading', 'bulletList', 'orderedList',
    'rtl', 'ltr', 'auto', 'undo', 'redo', 'insertTable', 'addRowAfter', 'addColumnAfter',
    'deleteRow', 'deleteColumn', 'deleteTable'
]);

export function assertDocumentFormat(format) {
    if (format?.id !== richDocumentV1.id || format?.version !== richDocumentV1.version)
        throw new Error('Unsupported document format');
}

export function readDocumentJson(source, format = richDocumentV1) {
    assertDocumentFormat(format);
    if (typeof source !== 'string' || source.length > 2_000_000) throw new Error('Invalid document size');
    const content = JSON.parse(source);
    if (!isRichDocument(content)) throw new Error('Unsupported document structure');
    return normalizeEmptyText(content);
}

export function writeDocumentJson(content, format = richDocumentV1) {
    assertDocumentFormat(format);
    if (!isRichDocument(content)) throw new Error('Unsupported document structure');
    const source = JSON.stringify(content);
    if (source.length > 2_000_000) throw new Error('Invalid document size');
    return source;
}
