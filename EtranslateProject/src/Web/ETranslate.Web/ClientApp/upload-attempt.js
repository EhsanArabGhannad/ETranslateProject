// Keep one logical asset ID while retrying the same selected File in the open page.
// Selecting a new File starts a new operation; nothing is persisted or automatically retried.
export function createUploadAttempt(newId = () => crypto.randomUUID()) {
    let selected, id;
    return file => { if (file !== selected) { selected = file; id = newId(); } return id; };
}
