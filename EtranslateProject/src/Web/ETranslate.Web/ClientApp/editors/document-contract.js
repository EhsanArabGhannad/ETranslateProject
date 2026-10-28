import { isRichDocument, normalizeEmptyText } from '../document-schema.js';

// Application vocabulary, not a vendor's unrestricted JSON. Existing API payloads stay unwrapped.
// Matches the server descriptor; NULL/NULL legacy rows remain unlabelled in storage.
export const richDocumentV1 = Object.freeze({ id: 'etranslate.rich-document', version: 1 });
export const richDocumentV2 = Object.freeze({ id: 'etranslate.rich-document', version: 2 });
export const richDocumentV3 = Object.freeze({ id: 'etranslate.rich-document', version: 3 });
export const richDocumentV4 = Object.freeze({ id: 'etranslate.rich-document', version: 4 });
export const richDocumentV5 = Object.freeze({ id: 'etranslate.rich-document', version: 5 });
export const legacyDocument = Object.freeze({ id: 'etranslate.legacy-json', version: 0 });

export function supportsStoredContent(format, version) {
    // Old pages had no metadata attributes. Do not treat partial/empty/unknown metadata as legacy.
    if (format == null && version == null) return true;
    return format === richDocumentV1.id && [1,'1',2,'2',3,'3',4,'4',5,'5'].includes(version) ||
        format === legacyDocument.id && (version === 0 || version === '0');
}
export const editorCommands = Object.freeze([
    'bold', 'italic', 'underline', 'strike', 'superscript', 'subscript', 'paragraph', 'heading', 'bulletList', 'orderedList',
    'rtl', 'ltr', 'auto', 'undo', 'redo', 'insertTable', 'addRowAfter', 'addColumnAfter',
    'deleteRow', 'deleteColumn', 'deleteTable', 'fontFamily', 'fontSize', 'textColor', 'highlight', 'clearTextStyle',
    'textAlign','lineHeight','indentLevel','increaseIndent','decreaseIndent','clearParagraphStyle','spaceBeforePt','spaceAfterPt','firstLineIndentMm'
]);

export function assertDocumentFormat(format) {
    if (format?.id !== richDocumentV1.id || ![1,2,3,4,5].includes(format?.version))
        throw new Error('Unsupported document format');
}

export function storedDocumentFormat(format,version){
    if(!supportsStoredContent(format,version))throw new Error('Unsupported stored format');
    return Number(version)===5?richDocumentV5:Number(version)===4?richDocumentV4:Number(version)===3?richDocumentV3:Number(version)===2?richDocumentV2:richDocumentV1;
}
export function readDocumentJson(source, format = richDocumentV5) {
    assertDocumentFormat(format);
    if (typeof source !== 'string' || source.length > 2_000_000) throw new Error('Invalid document size');
    const content = JSON.parse(source);
    if (!isRichDocument(content,format.version)) throw new Error('Unsupported document structure');
    return normalizeEmptyText(content);
}

export function writeDocumentJson(content, format = richDocumentV5) {
    assertDocumentFormat(format);
    if (!isRichDocument(content,format.version)) throw new Error('Unsupported document structure');
    const source = JSON.stringify(content);
    if (source.length > 2_000_000) throw new Error('Invalid document size');
    return source;
}
