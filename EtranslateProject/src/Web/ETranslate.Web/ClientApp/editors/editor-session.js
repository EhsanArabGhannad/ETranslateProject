import { assertDocumentFormat, editorCommands, readDocumentJson, writeDocumentJson } from './document-contract.js';

/**
 * Provider contract (trusted application code):
 * { id, format, capabilities, create({ element, content, editable, resolveAsset, labels, onRejectedChange }) }.
 * create returns { getDocument, getCommandState, execute, setEditable, subscribe, destroy }.
 * subscribe emits { documentChanged: boolean }; selection/read-only changes must emit false.
 * No vendor instance, native selection, chain, HTML or native document leaks into the host.
 */
export function createEditorSession({ provider, element, contentJson, editable = false,
    resolveAsset = () => null, labels = {}, onRejectedChange = () => {} }) {
    assertDocumentFormat(provider?.format);
    const content = readDocumentJson(contentJson, provider.format);
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
        const state = adapter.getCommandState(command);
        return { enabled: enabled && state.enabled === true, active: state.active === true };
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
        execute(command) {
            if (!getCommandState(command).enabled) return false;
            return adapter.execute(command) === true;
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
