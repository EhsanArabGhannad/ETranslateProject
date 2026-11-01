import test from 'node:test';
import assert from 'node:assert/strict';
import { createUploadAttempt } from '../upload-attempt.js';

test('manual retries reuse identity for the selected File; a new selection is a new operation even with the same name', () => {
    let generated = 0; const attempt = createUploadAttempt(() => `upload-${++generated}`);
    const first = new File(['bytes'], 'same.png'), other = new File(['bytes'], 'same.png');
    assert.equal(attempt(first), 'upload-1'); assert.equal(attempt(first), 'upload-1');
    assert.equal(attempt(other), 'upload-2'); assert.equal(attempt(other), 'upload-2'); assert.equal(generated, 2);
    assert.equal(createUploadAttempt(() => 'different-page')(first), 'different-page');
});
