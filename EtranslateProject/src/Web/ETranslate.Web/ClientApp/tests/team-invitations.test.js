import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

const script = readFileSync(new URL('../../wwwroot/js/team-invitations.js', import.meta.url), 'utf8');
function run({ hash = '', link = null, input = null } = {}) {
    const historyCalls = [];
    runInNewContext(script, {
        document: { getElementById: id => id === 'invitation-link' ? link : id === 'accept-token' ? input : null },
        location: { origin: 'https://localhost:7049', pathname: '/Team/Accept', search: '?invitationId=test', hash },
        history: { replaceState: (...args) => historyCalls.push(args) },
        URL
        // No storage, network or cookie APIs are exposed to this script.
    });
    return historyCalls;
}

test('manual invitation link uses the current origin and retains the fragment', () => {
    const link = { value: '/Team/Accept?invitationId=test#secret', dataset: { relativeLink: 'true' } };
    run({ link });
    assert.equal(link.value, 'https://localhost:7049/Team/Accept?invitationId=test#secret');
});
test('valid token prefills the form and is removed from the URL', () => {
    const token = 'a'.repeat(41) + '_-';
    const input = { value: '' };
    assert.deepEqual(run({ hash: '#' + token, input }), [[null, '', '/Team/Accept?invitationId=test']]);
    assert.equal(input.value, token);
});
test('invalid fragments are removed without populating the token field', () => {
    for (const token of ['short', '?'.repeat(43), '%61'.repeat(43), 'a'.repeat(44)]) {
        const input = { value: '' };
        assert.equal(run({ hash: '#' + token, input }).length, 1);
        assert.equal(input.value, '');
    }
});
test('manual input without a fragment is preserved', () => {
    const input = { value: 'manually-entered' };
    assert.equal(run({ input }).length, 0);
    assert.equal(input.value, 'manually-entered');
});
test('pages without invitation fields are unaffected', () => {
    assert.equal(run({ hash: '#unrelated' }).length, 0);
});
