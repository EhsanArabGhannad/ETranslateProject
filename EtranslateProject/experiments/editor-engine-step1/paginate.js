// A bounded measurement experiment, NOT an editing/pagination engine.
// It never writes back to the editor, splits JSON or silently clips a tall block.
export function paginateBlocks(source, container, maximumPages = 50) {
    container.replaceChildren();
    const bodies = [];
    function addPage() {
        const sheet = document.createElement('article'); sheet.className = 'sheet'; sheet.id = `page-${bodies.length + 1}`;
        const body = document.createElement('div'); body.className = 'page-content'; body.dir = 'auto';
        const label = document.createElement('div'); label.className = 'page-label';
        sheet.append(body, label); container.append(sheet); bodies.push(body); return body;
    }
    let target = addPage();
    for (const block of source.children) {
        const clone = block.cloneNode(true); target.append(clone);
        if (target.scrollHeight <= target.clientHeight + 1) continue;
        clone.remove();
        if (target.children.length) {
            if (bodies.length >= maximumPages) return fail('page-limit');
            target = addPage();
        }
        target.append(clone);
        if (target.scrollHeight > target.clientHeight + 1) return fail('oversized-block');
    }
    bodies.forEach((body, i) => body.nextElementSibling.textContent = `Ölçüm örneği - Sayfa ${i + 1} / ${bodies.length} - PDF değildir`);
    return { pages: bodies.length, issue: null, previewText: bodies.map(b => b.textContent).join('') };
    function fail(issue) {
        container.replaceChildren();
        const warning = document.createElement('p'); warning.className = 'error-card';
        warning.textContent = issue === 'page-limit' ? '50 sayfalık deney sınırı aşıldı; özgün metin solda korunuyor.'
            : 'Tek bir blok sayfadan uzun. Bu basit deney onu bölemez; hiçbir içerik kırpılmadı veya silinmedi. Özgün metin solda korunuyor.';
        container.append(warning); return { pages: null, issue, previewText: null };
    }
}
