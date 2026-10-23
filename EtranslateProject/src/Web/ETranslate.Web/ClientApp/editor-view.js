import ui from '../Resources/interface.tr.json' with { type: 'json' };
import { formatUi } from './ui-format.js';
import { readPaperLayout, mmToPixels, paperScale } from './editor-view-layout.js';

// Operate on the existing DOM only. No editor SDK, content transactions, remounts or saved preferences.
export function startEditorView(workspace) {
    const find = id => document.getElementById(id);
    const controls = find('editor-view-controls'), viewport = find('editor-viewport');
    const stage = find('editor-paper-stage'), paper = find('editor-paper');
    const zoom = find('editor-zoom'), toggle = find('editor-focus-toggle'), status = find('editor-zoom-status');
    const panel = workspace.querySelector('.editor-controls');
    if (!controls || !viewport || !stage || !paper || !zoom || !toggle || !status || !panel) return () => {};
    const layout = readPaperLayout(find('template-layout')?.value);
    if (!layout) { find('editor-paper-description').textContent = ui.EditorPaperUnavailable; return () => {}; }
    const width = mmToPixels(layout.width), height = mmToPixels(layout.height);
    paper.style.width = `${width}px`; paper.style.minHeight = `${height}px`;
    for (const edge of ['top', 'right', 'bottom', 'left']) paper.style.setProperty(`padding-${edge}`, `${mmToPixels(layout.margins[edge])}px`);
    paper.style.setProperty('--editor-body-min-height', `${height - mmToPixels(layout.margins.top + layout.margins.bottom) - 2}px`);
    viewport.dataset.paperView = 'true'; controls.hidden = false;
    find('editor-paper-description').textContent = formatUi(ui.EditorPaperDescription, layout.pageSize,
        layout.orientation === 'Landscape' ? ui.EditorLandscape : ui.EditorPortrait, layout.width, layout.height);
    let frame = 0, disposed = false, focused = false, returnFocus, windowScroll, panelScroll, bodyWasLocked;
    let inertElements = [], attributes = [];
    function resize() {
        frame = 0;
        if (disposed) return;
        const scale = paperScale(zoom.value, viewport.clientWidth - 32, width);
        paper.style.transform = `scale(${scale})`;
        stage.style.width = `${width * scale}px`; stage.style.height = `${paper.offsetHeight * scale}px`;
        status.textContent = formatUi(ui.EditorZoomPercent, Math.round(scale * 100));
    }
    function scheduleResize() { if (!frame && !disposed) frame = requestAnimationFrame(resize); }
    const observer = new ResizeObserver(scheduleResize);
    observer.observe(viewport); observer.observe(paper);
    function setFocused(value, restore = true) {
        if (focused === value) return;
        focused = value;
        if (value) {
            returnFocus = document.activeElement; windowScroll = [window.scrollX, window.scrollY]; panelScroll = panel.scrollTop;
            bodyWasLocked = document.body.classList.contains('editor-focus-open');
            attributes = ['role', 'aria-modal', 'aria-labelledby'].map(name => [name, panel.getAttribute(name)]);
            // Inert siblings along the ancestry path, never the editor or its ancestors.
            for (let node = panel; node && node !== document.body; node = node.parentElement) {
                for (const sibling of node.parentElement.children) {
                    if (sibling === node || !(sibling instanceof HTMLElement)) continue;
                    inertElements.push([sibling, sibling.inert]); sibling.inert = true;
                }
            }
            panel.classList.add('editor-focus'); document.body.classList.add('editor-focus-open');
            panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-modal', 'true'); panel.setAttribute('aria-labelledby', 'editor-heading');
            if (!panel.contains(document.activeElement)) toggle.focus({ preventScroll: true });
        } else {
            panel.classList.remove('editor-focus');
            if (!bodyWasLocked) document.body.classList.remove('editor-focus-open');
            for (const [element, inert] of inertElements) element.inert = inert;
            inertElements = [];
            for (const [name, value] of attributes) {
                if (value === null) panel.removeAttribute(name); else panel.setAttribute(name, value);
            }
            panel.scrollTop = panelScroll;
            if (restore) {
                if (returnFocus?.isConnected) returnFocus.focus({ preventScroll: true });
                window.scrollTo(...windowScroll);
            }
        }
        toggle.textContent = value ? ui.EditorExitFocus : ui.EditorEnterFocus;
        toggle.setAttribute('aria-pressed', String(value)); scheduleResize();
    }
    function keyboard(event) {
        if (!focused || event.isComposing) return;
        // The focus surface owns Esc. Editors may prevent its native default without
        // consuming it; an inner popup can still stop propagation when closing itself.
        if (event.key === 'Escape') { event.preventDefault(); setFocused(false); }
        if (event.defaultPrevented || event.key !== 'Tab') return;
        const targets = Array.from(panel.querySelectorAll('a[href], button, input:not([type=hidden]), select, textarea, [tabindex], [contenteditable=true]'))
            .filter(element => !element.matches(':disabled') && element.tabIndex >= 0 && !element.closest('[inert]') && element.getClientRects().length);
        const first = targets[0], last = targets.at(-1);
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    }
    const toggleFocus = () => setFocused(!focused);
    const keepSelection = event => event.preventDefault();
    const leavePage = () => setFocused(false, false);
    toggle.addEventListener('mousedown', keepSelection); toggle.addEventListener('click', toggleFocus);
    zoom.addEventListener('change', scheduleResize); document.addEventListener('keydown', keyboard);
    window.addEventListener('pagehide', leavePage);
    resize();
    return () => {
        setFocused(false); disposed = true; cancelAnimationFrame(frame); observer.disconnect();
        toggle.removeEventListener('mousedown', keepSelection); toggle.removeEventListener('click', toggleFocus);
        zoom.removeEventListener('change', scheduleResize); document.removeEventListener('keydown', keyboard);
        window.removeEventListener('pagehide', leavePage);
        controls.hidden = true; delete viewport.dataset.paperView;
        for (const element of [stage, paper]) element.removeAttribute('style');
    };
}
