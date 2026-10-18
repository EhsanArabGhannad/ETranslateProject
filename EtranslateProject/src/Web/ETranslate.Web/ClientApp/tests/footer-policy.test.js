import test from 'node:test';
import assert from 'node:assert/strict';
import { footerPolicy, showFooter } from '../footer-policy.js';
test('omitted policy retains legacy all-page behavior', () => {
    assert.equal(footerPolicy(null), 'AllPages'); assert.equal(footerPolicy(''), 'AllPages'); assert.equal(footerPolicy('{"pageSize":"A4"}'), 'AllPages');
    for (const role of ['Earlier', 'Last']) assert.equal(showFooter('AllPages', role), true);
});
test('last-page preview hides custom footer only in earlier-page example', () => {
    assert.equal(footerPolicy('{"footerPlacement":"LastPage"}'), 'LastPage');
    assert.equal(showFooter('LastPage', 'Earlier'), false); assert.equal(showFooter('LastPage', 'Last'), true);
});
test('unknown presentation never silently defaults to all pages', () => {
    for (const json of ['[]', 'null', '{', '{"footerPlacement":null}', '{"footerPlacement":"OddPages"}']) assert.equal(footerPolicy(json), null);
});
