import { assertDocumentFormat, editorCommands, readDocumentJson, writeDocumentJson } from './document-contract.js';
import { typographyCommands, validTypographyValue } from '../typography.js';
import { paragraphCommands, paragraphActions, validParagraphValue } from '../paragraph.js';

/**
 * Provider contract (trusted application code):
 * { id, format, capabilities, create({ element, content, editable, resolveAsset, labels, onRejectedChange }) }.
 * create returns { getDocument, getCommandState, execute, setEditable, subscribe, destroy }.
 * subscribe emits { documentChanged: boolean }; selection/read-only changes must emit false.
 * No vendor instance, native selection, chain, HTML or native document leaks into the host.
 */
export function createEditorSession({ provider, element, contentJson, contentFormat = provider?.format, editable = false,
    resolveAsset = () => null, labels = {}, onRejectedChange = () => {} }) {
    assertDocumentFormat(provider?.format);
    assertDocumentFormat(contentFormat);
    if(contentFormat.version>provider.format.version)throw new Error('Provider cannot preserve the stored content version');
    const content = readDocumentJson(contentJson, contentFormat);
    const canEdit = editable === true;
    const adapter = provider.create({ element, content, editable: canEdit, resolveAsset, labels, onRejectedChange });
    let destroyed = false, enabled = canEdit, failed = false;
    const changes = new Set(), states = new Set();
    const assertAlive = () => { if (destroyed) throw new Error('Editor session is disposed'); };
    const getDocumentJson = () => { assertAlive(); return writeDocumentJson(adapter.getDocument(), provider.format); };
    const notifyState = () => { for (const listener of states) listener(); };
    let unsubscribe;
    try {
        // Fail before hiding the fallback if an adapter cannot export the supported application format.
        getDocumentJson();
        unsubscribe = adapter.subscribe(event => {
            if (destroyed) return;
            if (event.documentChanged) {
                let source;
                try { source = getDocumentJson(); }
                catch {
                    // Keep the host's last good JSON and fail closed; never silently strip native fields.
                    failed = true; enabled = false; adapter.setEditable(false);
                    onRejectedChange(); notifyState(); return;
                }
                for (const listener of changes) listener(source);
            }
            notifyState();
        });
    } catch (error) { adapter.destroy(); throw error; }
    function getCommandState(command) {
        if (destroyed || !editorCommands.includes(command)) return { enabled: false, active: false };
        if(provider.format.version<2 && (Object.hasOwn(typographyCommands,command)||command==='clearTextStyle'))return {enabled:false,active:false};
        if(provider.format.version<3 && (Object.hasOwn(paragraphCommands,command)||paragraphActions.includes(command)))return {enabled:false,active:false};
        const state = adapter.getCommandState(command);
        return { enabled: enabled && state.enabled === true, active: state.active === true,
            ...(Object.hasOwn(typographyCommands,command)||Object.hasOwn(paragraphCommands,command)?{value:state.value??null,mixed:state.mixed===true}:{}) };
    }
    function subscribe(listeners, listener) {
        assertAlive(); listeners.add(listener);
        return () => listeners.delete(listener);
    }
    return Object.freeze({
        engineId: provider.id,
        format: Object.freeze({ ...provider.format }),
        capabilities: Object.freeze({ ...provider.capabilities }),
        getDocumentJson,
        getCommandState,
        execute(command, value) {
            if(Object.hasOwn(typographyCommands,command)&&!validTypographyValue(typographyCommands[command],value))return false;
            if(Object.hasOwn(paragraphCommands,command)&&!validParagraphValue(paragraphCommands[command],value))return false;
            if (!getCommandState(command).enabled) return false;
            return adapter.execute(command, value) === true;
        },
        setEditable(value) {
            if (destroyed) return;
            // A historical/read-only mount cannot be upgraded to editable by a host's retry handler.
            enabled = canEdit && !failed && value === true;
            adapter.setEditable(enabled); notifyState();
        },
        onDocumentChange: listener => subscribe(changes, listener),
        onStateChange: listener => subscribe(states, listener),
        destroy() {
            if (destroyed) return;
            destroyed = true; unsubscribe?.(); changes.clear(); states.clear(); adapter.destroy();
        }
    });
}
