import test, { afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { withEditorRequest } from '../editor-request.js';

const originalFetch = globalThis.fetch;
afterEach(() => { globalThis.fetch = originalFetch; });

test('request consumes its response once and clears the deadline after success', async () => {
    let signal, calls = 0;
    globalThis.fetch = async (url, options) => { calls++; assert.equal(url, '/save'); assert.equal(options.method, 'POST'); signal = options.signal; return new Response('{"saved":true}'); };
    assert.deepEqual(await withEditorRequest('/save', { method: 'POST' }, r => r.json(), 10), { saved: true });
    await new Promise(resolve => setTimeout(resolve, 20)); assert.equal(signal.aborted, false); assert.equal(calls, 1);
});
test('deadline aborts a pending request without retrying the mutation', async () => {
    let calls = 0;
    globalThis.fetch = (_, options) => { calls++; return new Promise((_, reject) => options.signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true })); };
    await assert.rejects(withEditorRequest('/save', { method: 'POST' }, () => assert.fail('No response'), 5), { name: 'AbortError' }); assert.equal(calls, 1);
});
test('deadline remains active while response consumption is pending', async () => {
    let signal;
    globalThis.fetch = async (_, options) => { signal = options.signal; return {}; };
    await assert.rejects(withEditorRequest('/review', { method: 'POST' }, () => new Promise((_, reject) => signal.addEventListener('abort', () => reject(new DOMException('Body aborted', 'AbortError')), { once: true })), 5), { name: 'AbortError' });
});
test('transport and body failures are propagated once and their timers are cleared', async () => {
    for (const transport of [true, false]) {
        let signal, calls = 0; const failure = new Error('Synthetic failure');
        globalThis.fetch = async (_, options) => { calls++; signal = options.signal; if (transport) throw failure; return {}; };
        await assert.rejects(withEditorRequest('/save', { method: 'POST' }, () => { throw failure; }, 5), error => error === failure);
        await new Promise(resolve => setTimeout(resolve, 10)); assert.equal(signal.aborted, false); assert.equal(calls, 1);
    }
});
