import { assertDocumentFormat, editorCommands, readDocumentJson, writeDocumentJson } from './document-contract.js';
import { typographyCommands, validTypographyValue } from '../typography.js';
import { paragraphCommands, paragraphActions, advancedParagraphFields, validParagraphValue } from '../paragraph.js';
import { scriptMarks } from '../inline-script.js';
import {validSearch,validReplacement} from '../document-text-tools.js';
import { tableOptions } from '../table-options.js';
import { validColumnWidths } from '../table-widths.js';
import { cellStyleFields, validCellStyleValue, validCellStyleChange } from '../cell-style.js';

/**
 * Provider contract (trusted application code):
 * { id, format, capabilities, create({ element, content, editable, resolveAsset, labels, onRejectedChange }) }.
 * create returns { getDocument, getCommandState, execute, setEditable, subscribe, destroy }.
 * subscribe emits { documentChanged: boolean }; selection/read-only changes must emit false.
 * Optional local search methods: setSearch, getSearchState, findMatch, replaceSearch.
 * Search/navigation may operate on readonly content; replacement always requires the write lock.
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
        if(provider.format.version<4 && scriptMarks.includes(command))return {enabled:false,active:false};
        if(provider.format.version<2 && (Object.hasOwn(typographyCommands,command)||command==='clearTextStyle'))return {enabled:false,active:false};
        if(provider.format.version<3 && (Object.hasOwn(paragraphCommands,command)||paragraphActions.includes(command)))return {enabled:false,active:false};
        if(provider.format.version<5 && advancedParagraphFields.includes(command))return {enabled:false,active:false};
        if(provider.format.version<6 && command==='tableColumnWidths')return {enabled:false,active:false,columns:0,value:null};
        if(provider.format.version<7 && command==='tableCellStyle')return {enabled:false,active:false};
        if(provider.format.version<8 && ['mergeNextCell','mergeCellsInRow','splitCellInRow'].includes(command))return {enabled:false,active:false};
        const state = adapter.getCommandState(command);
        if (command === 'tableCellStyle') {
            const group = source => ({ count: Number.isInteger(source?.count) && source.count >= 0 && source.count <= 600 ? source.count : 0,
                values: Object.fromEntries(cellStyleFields.map(field => [field, validCellStyleValue(field, source?.values?.[field]) ? source.values[field] : null])),
                mixed: Object.fromEntries(cellStyleFields.map(field => [field, source?.mixed?.[field] === true])), custom: source?.custom === true });
            return { enabled: enabled && state.enabled === true, active: state.active === true, selection: group(state.selection), table: group(state.table) };
        }
        if (command === 'tableColumnWidths') {
            const columns = Number.isInteger(state.columns) && state.columns >= 1 && state.columns <= 12 ? state.columns : 0;
            return { enabled: enabled && state.enabled === true && columns > 0, active: state.active === true, columns,
                value: validColumnWidths(state.value, columns) && Array.isArray(state.value) ? [...state.value] : null,
                context: typeof state.context === 'string' ? state.context : null };
        }
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
        setSearch(options){return !destroyed&&validSearch(options)&&adapter.setSearch?.(options)===true;},
        getSearchState(){const state=!destroyed?adapter.getSearchState?.():null;return state?.supported===true?{supported:true,total:state.total,index:state.index,truncated:state.truncated===true,editable:enabled,canReplace:enabled&&state.total>0}:{supported:false,total:0,index:0,truncated:false,editable:false,canReplace:false};},
        findMatch(direction=1){return !destroyed&&[1,-1].includes(direction)&&adapter.findMatch?.(direction)===true;},
        replaceSearch(value,all=false){return !destroyed&&enabled&&validReplacement(value)&&typeof all==='boolean'&&adapter.replaceSearch?.(value,all)===true;},
        getCommandState,
        execute(command, value) {
            if (command === 'tableCellStyle') { if (!validCellStyleChange(value)) return false; value = { ...value }; }
            if (command === 'insertTable') { value = tableOptions(value); if (!value) return false; }
            if(Object.hasOwn(typographyCommands,command)&&!validTypographyValue(typographyCommands[command],value))return false;
            if(Object.hasOwn(paragraphCommands,command)&&!validParagraphValue(paragraphCommands[command],value,provider.format.version))return false;
            const state = getCommandState(command); if (!state.enabled) return false;
            if (command === 'tableColumnWidths') { if (!validColumnWidths(value, state.columns)) return false; value = value?.slice() ?? null; }
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
