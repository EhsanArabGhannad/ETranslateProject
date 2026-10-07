// Keep invitation secrets out of server URLs, logs, referrers and browser storage.
const invitationLink = document.getElementById('invitation-link');
if (invitationLink?.dataset.relativeLink === 'true') invitationLink.value = new URL(invitationLink.value, location.origin).href;
const tokenInput = document.getElementById('accept-token');
if (tokenInput && location.hash) {
    const token = location.hash.slice(1);
    if (/^[A-Za-z0-9_-]{43}$/.test(token)) tokenInput.value = token;
    history.replaceState(null, '', location.pathname + location.search);
}
