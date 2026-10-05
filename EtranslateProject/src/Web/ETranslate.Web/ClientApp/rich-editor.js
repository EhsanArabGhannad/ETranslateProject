import { Editor, Node } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { guid, isRichDocument, normalizeEmptyText } from './document-schema.js';
import { assetUrl } from './preview.js';

export function startRichEditor(workspace, onChanged, preview) {
    const json = document.getElementById('document-json');
    const status = document.getElementById('editor-format-status');
    if (workspace.dataset.richCompatible !== 'true') {
        status.textContent = 'این ساختار خارج از امکانات ویرایشگر است؛ JSON اصلی بدون تبدیل حفظ شده است.';
        return null;
    }
    let editor = null, initializing = true;
    try {
        const content = JSON.parse(json.value);
        if (!isRichDocument(content)) throw new Error('Unsupported structure');
        const ManagedImage = Node.create({
            name: 'image', group: 'block', atom: true, draggable: false,
            addAttributes() {
                return { assetId: { default: null, parseHTML: element => element.getAttribute('data-asset-id'), renderHTML: attrs =>
                    guid.test(attrs.assetId ?? '') ? { 'data-asset-id': attrs.assetId } : {} } };
            },
            parseHTML() { return [{ tag: 'img[data-asset-id]', getAttrs: element => guid.test(element.getAttribute('data-asset-id') ?? '') ? {} : false }]; },
            renderHTML({ node }) {
                const url = assetUrl(workspace, node.attrs.assetId);
                return url ? ['img', { src: url, alt: 'تصویر قالب', 'data-asset-id': node.attrs.assetId }]
                    : ['span', { class: 'missing-asset' }, '[تصویر قالب در دسترس نیست]'];
            }
        });
        const editable = workspace.dataset.editable === 'true';
        editor = new Editor({
            element: document.getElementById('rich-text'), injectCSS: false, editable, textDirection: 'auto',
            extensions: [StarterKit.configure({ code: false, codeBlock: false, link: false, horizontalRule: false,
                dropcursor: false, trailingNode: false }), ManagedImage],
            content: { type: 'doc', content: [{ type: 'paragraph' }] },
            editorProps: { attributes: { role: 'textbox', 'aria-label': 'متن ترجمه', 'aria-multiline': 'true', spellcheck: 'false' } },
            onUpdate: ({ editor }) => {
                if (initializing) return;
                json.value = JSON.stringify(editor.getJSON());
                onChanged(); preview();
            }
        });
        // Check both our preservation policy and ProseMirror's structural constraints before replacing the fallback UI.
        const value = editor.schema.nodeFromJSON(normalizeEmptyText(content)); value.check();
        // Loading a saved revision is not a user edit and must not be undoable into a blank document.
        editor.chain().setMeta('addToHistory', false)
            .setContent(value, { emitUpdate: false, errorOnInvalidContent: true }).run();
        initializing = false;
        const buttons = Array.from(document.querySelectorAll('.editor-toolbar button'));
        function updateToolbar() {
            for (const button of buttons) {
                const command = button.dataset.command;
                button.disabled = !editable || !editor.isEditable || (command === 'undo' && !editor.can().undo()) || (command === 'redo' && !editor.can().redo());
                if (button.hasAttribute('aria-pressed')) button.setAttribute('aria-pressed', String(editor.isActive(command, command === 'heading' ? { level: 2 } : undefined)));
            }
        }
        for (const button of buttons) {
            button.addEventListener('mousedown', event => event.preventDefault());
            button.addEventListener('click', () => {
                if (!editable || !editor.isEditable || workspace.dataset.editable !== 'true') return;
                const command = button.dataset.command, chain = editor.chain().focus();
                const operations = {
                    bold: () => chain.toggleBold(), italic: () => chain.toggleItalic(), underline: () => chain.toggleUnderline(),
                    paragraph: () => chain.setParagraph(), heading: () => chain.toggleHeading({ level: 2 }),
                    bulletList: () => chain.toggleBulletList(), orderedList: () => chain.toggleOrderedList(),
                    rtl: () => chain.setTextDirection('rtl'), ltr: () => chain.setTextDirection('ltr'), auto: () => chain.setTextDirection('auto'),
                    undo: () => chain.undo(), redo: () => chain.redo()
                };
                operations[command]?.().run(); updateToolbar();
            });
        }
        editor.on('selectionUpdate', updateToolbar); editor.on('transaction', updateToolbar); updateToolbar();
        for (const id of ['translation-text', 'legacy-format-notice']) {
            const element = document.getElementById(id); if (element) element.hidden = true;
        }
        for (const label of document.querySelectorAll('label[for="translation-text"], label[for="document-json"]')) label.hidden = true;
        json.hidden = true;
        document.querySelector('#draft-form input[name=mode]').value = 'rich';
        document.getElementById('rich-region').hidden = false;
        status.textContent = editable ? 'قالب‌بندی متن همراه هر نسخه ذخیره می‌شود.' : 'نسخه‌ی خواندنی؛ ویرایش غیرفعال است.';
        return editor;
    } catch {
        editor?.destroy();
        status.textContent = 'ساختار اصلی حفظ شده است؛ ویرایشگر نتوانست آن را باز کند. از فیلد متنی یا JSON موجود استفاده کنید.';
        return null;
    }
}
