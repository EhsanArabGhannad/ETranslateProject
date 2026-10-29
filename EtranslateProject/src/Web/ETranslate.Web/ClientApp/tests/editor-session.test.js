import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { richDocumentV1, readDocumentJson, writeDocumentJson } from '../editors/document-contract.js';
import { createEditorSession } from '../editors/editor-session.js';

const sample = { type: 'doc', content: [{ type: 'paragraph', attrs: { dir: 'rtl' }, content: [
    { type: 'text', text: 'ترجمه با نیم‌فاصله İstanbul 123', marks: [{ type: 'bold' }] }
] }] };
const source = JSON.stringify(sample);

// A deliberately different in-memory representation proves that the host never uses a native editor.
function fakeProvider(id = 'test-engine') {
    let native, editable, listener, disposed = 0;
    const calls = [];
    const provider = {
        id, format: richDocumentV1, capabilities: { livePagination: false },
        create(options) {
            calls.push(['create', options]);
            native = { body: structuredClone(options.content) }; editable = options.editable;
            return {
                getDocument: () => structuredClone(native.body),
                getCommandState: command => ({ enabled: editable && command !== 'redo', active: command === 'bold' }),
                execute(command) { calls.push(['execute', command]); return true; },
                setEditable(value) { editable = value; calls.push(['editable', value]); listener?.({ documentChanged: false }); },
                subscribe(callback) { listener = callback; return () => { listener = null; }; },
                destroy() { disposed++; }
            };
        }
    };
    return { provider, calls, disposed: () => disposed,
        signal: (documentChanged = false) => listener?.({ documentChanged }),
        replace: value => { native.body = value; }
    };
}

test('application format round-trips supported Unicode, marks and directions without a vendor envelope', () => {
    assert.deepEqual(readDocumentJson(source), sample);
    assert.equal(writeDocumentJson(sample), source);
    assert.equal(JSON.parse(writeDocumentJson(sample)).type, 'doc');
    assert.ok(Object.isFrozen(richDocumentV1));
});

test('unsupported formats, native payloads and malformed data fail before the provider mounts', () => {
    for (const format of [{ id: 'sfdt', version: 1 }, { ...richDocumentV1, version: 11 }, null]) {
        const engine = fakeProvider(); engine.provider.format = format;
        assert.throws(() => createEditorSession({ provider: engine.provider, contentJson: source }));
        assert.equal(engine.calls.length, 0);
    }
    for (const contentJson of ['{', JSON.stringify({ header: [], main: [], footer: [] }),
        JSON.stringify({ ...sample, vendorState: true }), ' '.repeat(2_000_001)]) {
        const engine = fakeProvider();
        assert.throws(() => createEditorSession({ provider: engine.provider, contentJson }));
        assert.equal(engine.calls.length, 0);
    }
});

test('legacy empty text is normalized only in the working copy', () => {
    const legacy = '{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":""}]}]}';
    const engine = fakeProvider();
    const session = createEditorSession({ provider: engine.provider, contentJson: legacy });
    assert.equal(legacy.includes('"text":""'), true);
    assert.deepEqual(JSON.parse(session.getDocumentJson()), { type: 'doc', content: [{ type: 'paragraph', content: [] }] });
    session.destroy();
});

test('two providers use the same host API without exposing chains, nodes or native JSON', () => {
    for (const name of ['first-test-engine', 'replacement-test-engine']) {
        const engine = fakeProvider(name);
        const session = createEditorSession({ provider: engine.provider, contentJson: source, editable: true });
        assert.equal(session.engineId, name);
        assert.equal(session.getDocumentJson(), source);
        assert.equal(session.chain, undefined); assert.equal(session.getJSON, undefined); assert.equal(session.state, undefined);
        assert.equal(session.execute('bold'), true);
        assert.deepEqual(session.getCommandState('bold'), { enabled: true, active: true });
        assert.equal(session.execute('redo'), false);
        assert.equal(session.execute('deleteEverything'), false);
        assert.ok(Object.isFrozen(session)); assert.ok(Object.isFrozen(session.capabilities));
        session.destroy();
    }
});

test('selection and temporary locks notify state but do not mark content dirty', () => {
    const engine = fakeProvider();
    const session = createEditorSession({ provider: engine.provider, contentJson: source, editable: true });
    let stateEvents = 0; const changes = [];
    session.onStateChange(() => stateEvents++); const unsubscribe = session.onDocumentChange(value => changes.push(value));
    engine.signal(); session.setEditable(false);
    assert.equal(session.execute('bold'), false);
    session.setEditable(true); assert.equal(session.execute('bold'), true);
    assert.equal(changes.length, 0); assert.ok(stateEvents >= 3);
    engine.replace({ type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Yeni sürüm' }] }] });
    engine.signal(true); assert.equal(changes.length, 1); assert.match(changes[0], /Yeni sürüm/);
    unsubscribe(); engine.signal(true); assert.equal(changes.length, 1);
    session.destroy();
});

test('a read-only mount cannot be unlocked by retry and disposed sessions do not dispatch', () => {
    const engine = fakeProvider();
    const session = createEditorSession({ provider: engine.provider, contentJson: source, editable: false });
    session.setEditable(true);
    assert.equal(session.getCommandState('bold').enabled, false); assert.equal(session.execute('bold'), false);
    let called = 0; session.onStateChange(() => called++); session.onDocumentChange(() => called++);
    session.destroy(); session.destroy(); engine.signal(true); session.setEditable(true);
    assert.equal(called, 0); assert.equal(engine.disposed(), 1); assert.equal(session.execute('bold'), false);
    assert.throws(() => session.getDocumentJson(), /disposed/);
    assert.throws(() => session.onDocumentChange(() => {}), /disposed/);
});

test('invalid adapter export is rejected, not silently downgraded or accepted as native content', () => {
    const engine = fakeProvider();
    let rejected = 0;
    const session = createEditorSession({ provider: engine.provider, contentJson: source, editable: true, onRejectedChange: () => rejected++ });
    let changes = 0; session.onDocumentChange(() => changes++);
    engine.replace({ main: [{ value: 'native' }] });
    assert.throws(() => session.getDocumentJson(), /Unsupported/);
    assert.doesNotThrow(() => engine.signal(true)); assert.equal(changes, 0); assert.equal(rejected, 1);
    session.setEditable(true); assert.equal(session.execute('bold'), false);
    session.destroy();
});

test('adapter initialization failure disposes it and leaves caller-owned source untouched', () => {
    let disposed = 0;
    const engine = fakeProvider(); const create = engine.provider.create;
    engine.provider.create = options => ({ ...create(options), getDocument: () => ({ type: 'native' }), destroy: () => disposed++ });
    assert.throws(() => createEditorSession({ provider: engine.provider, contentJson: source }), /Unsupported/);
    assert.equal(disposed, 1); assert.equal(JSON.stringify(sample), source);
});

test('workspace host stays vendor-independent; selection is build-time and experiments stay out of the bundle graph', async () => {
    for (const path of ['workspace.js', 'rich-editor.js', 'editors/editor-session.js', 'editors/document-contract.js']) {
        const code = await readFile(new URL(`../${path}`, import.meta.url), 'utf8');
        assert.doesNotMatch(code, /from ['"]@tiptap\/|\.chain\(|\.getJSON\(|\.setEditable\([^)]*,/);
    }
    for (const file of await readdir(new URL('../editors/', import.meta.url))) {
        const code = await readFile(new URL(`../editors/${file}`, import.meta.url), 'utf8');
        assert.doesNotMatch(code, /tiptap-pagination-plus|tiptap-table-plus|canvas-editor|syncfusion|https?:\/\//);
    }
});
