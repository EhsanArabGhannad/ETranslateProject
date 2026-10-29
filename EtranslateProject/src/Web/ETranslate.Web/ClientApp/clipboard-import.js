import { isRichDocument } from './document-schema.js';
import { bounded, clipboardColor, clipboardFont, columnPercentages, lengthPt, neutralizeClipboardStyles } from './clipboard-style.js';

const blockTags = /^(P|DIV|H[1-6]|UL|OL|BLOCKQUOTE|TABLE|LI|SECTION|ARTICLE|HEADER|FOOTER|PRE|HTML|BODY)$/;
const ignored = /^(SCRIPT|STYLE|META|LINK|BASE|TITLE|HEAD|XML|IFRAME|OBJECT|EMBED|SVG|MATH|TEMPLATE|NOSCRIPT|INPUT|BUTTON|SELECT|TEXTAREA)$/;
const paragraph = (content = [], attrs = {}) => ({ type: 'paragraph', ...(Object.keys(attrs).length ? { attrs } : {}), content });
export function plainClipboard(text) {
    return { type: 'doc', content: text.replace(/\r\n?/g, '\n').split('\n').map(line => paragraph(line ? [{ type: 'text', text: line }] : [])) };
}

/** Inert clipboard HTML -> application JSON, independent of a vendor or a server service. */
export function importClipboard({ html = '', text = '', mode = 'formatted' }, document, { allowTables = true, allowStarts = true } = {}) {
    const warnings = new Set();
    if (!['formatted', 'plain'].includes(mode) || typeof html !== 'string' || typeof text !== 'string') return { error: 'invalid', warnings: [] };
    if ((mode !== 'plain' && html.length > 2_000_000) || text.length > 1_000_000) return { error: 'limit', warnings: [] };
    if (mode === 'plain' && text || !html.trim()) {
        const content = plainClipboard(text);
        return text && isRichDocument(content, 10) ? { content, warnings: [], plain: true } : { error: text ? 'limit' : 'empty', warnings: [] };
    }
    // Template contents stay inert: URLs, stylesheets and embedded objects never enter live DOM.
    const inert = document.implementation.createHTMLDocument('');
    const template = inert.createElement('template'); template.innerHTML = neutralizeClipboardStyles(html);
    const root = template.content, owner = root.ownerDocument;
    let nodes = 0;
    function boundTree(node, depth = 0) {
        if (++nodes > 20_000 || depth > 32) throw new Error('limit');
        for (const child of node.childNodes) boundTree(child, depth + 1);
    }
    try { boundTree(root); } catch { return { error: 'limit', warnings: [] }; }
    const rules = [];
    for (const sheet of root.querySelectorAll('style')) {
        const css = sheet.textContent.replace(/\/\*[\s\S]*?\*\//g, '');
        for (const match of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
            if (/@page|@font-face/i.test(match[1])) { warnings.add('layout'); continue; }
            for (const selector of match[1].trim().split(',')) {
                if (/^(?:[a-z][\w-]*)?(?:\.[\w-]+)?$/i.test(selector.trim()) && selector.trim()) rules.push([selector.trim(), match[2]]);
                else if (!/@list/i.test(selector)) warnings.add('formatting');
            }
        }
        if (rules.length > 500) return { error: 'limit', warnings: [] };
    }
    const specificity = selector => (selector.includes('.') ? 10 : 0) + (/^[a-z]/i.test(selector) ? 1 : 0);
    rules.sort((a, b) => specificity(a[0]) - specificity(b[0]));
    if (root.querySelector('link[rel="stylesheet"]')) warnings.add('formatting');
    // Office can carry surrounding HTML as context. Keep its ancestors and CSS rules,
    // but import only the selected fragment when explicit boundaries are present.
    const comments = owner.createTreeWalker(root, 128), starts = [], ends = [];
    for (let node = comments.nextNode(); node; node = comments.nextNode()) {
        if (node.data.trim() === 'StartFragment') starts.push(node);
        if (node.data.trim() === 'EndFragment') ends.push(node);
    }
    if (starts.length || ends.length) {
        if (starts.length !== 1 || ends.length !== 1 || !(starts[0].compareDocumentPosition(ends[0]) & 4)) return { error: 'invalid', warnings: [] };
        const after = owner.createRange(); after.setStartAfter(ends[0]); after.setEnd(root, root.childNodes.length); after.deleteContents();
        const before = owner.createRange(); before.setStart(root, 0); before.setEndBefore(starts[0]); before.deleteContents();
    }
    const styleCache = new WeakMap();
    function style(el) {
        if (styleCache.has(el)) return styleCache.get(el);
        const declarations = owner.createElement('span').style;
        for (const [selector, value] of rules) if (el.matches(selector)) declarations.cssText += ';' + value;
        declarations.cssText += ';' + (el.getAttribute('data-et-clipboard-style') ?? '');
        const result = Object.fromEntries([...declarations].map(key => [key, declarations.getPropertyValue(key).trim()]));
        for (const key of ['font-family', 'font-size', 'font-weight', 'font-style', 'color', 'background-color', 'text-align', 'line-height', 'text-indent', 'margin-top', 'margin-bottom', 'margin-left', 'margin-right', ...['top', 'right', 'bottom', 'left'].flatMap(side => ['color', 'width', 'style'].map(part => `border-${side}-${part}`))]) {
            const value = declarations.getPropertyValue(key); if (value) result[key] = value.trim();
        }
        // CSSOM drops Office-only properties; read just these bounded values without executing them.
        for (const name of ['mso-list', 'mso-bidi-font-weight', 'mso-bidi-font-style', 'mso-bidi-direction', 'mso-hide']) {
            const match = new RegExp('(?:^|;)\\s*' + name + '\\s*:\\s*([^;]+)', 'i').exec(el.getAttribute('data-et-clipboard-style') ?? '');
            if (match) result[name] = match[1].trim();
        }
        if (result.float && result.float !== 'none' || result.position && result.position !== 'static' || result['writing-mode']) warnings.add('layout');
        if (['letter-spacing', 'font-variant', 'text-transform'].some(key => result[key] && !['normal', 'none', '0px'].includes(result[key]))) warnings.add('formatting');
        styleCache.set(el, result); return result;
    }
    function context(el, inherited) {
        const s = style(el), next = { ...inherited, typography: { ...inherited.typography } }, tag = el.tagName;
        if (s['text-align']) next.textAlign = s['text-align'];
        if (s['line-height']) next.lineHeight = s['line-height'];
        if (tag === 'PRE' || /^(pre|pre-wrap|break-spaces)$/.test(s['white-space'] ?? '')) next.pre = true;
        const toggle = (key, value) => { if (value !== undefined) next[key] = value; };
        toggle('bold', /^(B|STRONG)$/.test(tag) ? true : undefined); toggle('italic', /^(I|EM)$/.test(tag) ? true : undefined);
        toggle('underline', tag === 'U' ? true : undefined); toggle('strike', /^(S|STRIKE|DEL)$/.test(tag) ? true : undefined);
        if (s['font-weight']) next.bold = /bold|[6-9]00/.test(s['font-weight']);
        if (s['font-style']) next.italic = /italic|oblique/.test(s['font-style']);
        if (s['mso-bidi-font-weight']) {
            const bold = s['mso-bidi-font-weight'] === 'bold';
            if (!s['font-weight'] && !/^(B|STRONG)$/.test(tag)) next.bold = bold;
            else if (next.bold !== bold) warnings.add('formatting');
        }
        if (s['mso-bidi-font-style']) {
            const italic = s['mso-bidi-font-style'] === 'italic';
            if (!s['font-style'] && !/^(I|EM)$/.test(tag)) next.italic = italic;
            else if (next.italic !== italic) warnings.add('formatting');
        }
        const decoration = s['text-decoration-line'] || s['text-decoration'];
        if (decoration) { next.underline = /underline/.test(decoration); next.strike = /line-through/.test(decoration); }
        if (tag === 'SUP' || s['vertical-align'] === 'super') next.script = 'superscript';
        if (tag === 'SUB' || s['vertical-align'] === 'sub') next.script = 'subscript';
        if (s['vertical-align'] === 'baseline') next.script = null;
        const family = s['font-family'] || (tag === 'FONT' ? el.getAttribute('face') : null);
        if (family) next.typography.fontFamily = clipboardFont(family, warnings);
        if (s['font-size']) { const size = lengthPt(s['font-size']); if (size > 0) next.typography.fontSize = bounded(size, 6, 72, 1, warnings); else warnings.add('formatting'); }
        for (const [css, field] of [['color', 'color'], ['background-color', 'highlight']]) {
            const value = s[css] || (css === 'color' && tag === 'FONT' ? el.getAttribute('color') : null);
            if (!value || field === 'highlight' && /^(TD|TH|TABLE|TR)$/.test(tag)) continue;
            const color = clipboardColor(value);
            if (color) next.typography[field] = color;
            else if (/transparent|rgba\([^)]*,\s*0\s*\)/.test(value)) delete next.typography[field];
            else warnings.add('formatting');
        }
        if (tag === 'MARK' && !s['background-color']) next.typography.highlight = '#ffff00';
        const dir = el.getAttribute('dir') || s.direction || s['mso-bidi-direction'];
        if (['rtl', 'ltr', 'auto'].includes(dir)) next.dir = dir;
        return next;
    }
    function attrs(el, ctx, top) {
        const s = style(el), result = ctx.dir ? { dir: ctx.dir } : {};
        const align = s['text-align'] || el.getAttribute('align') || ctx.textAlign; if (['left', 'center', 'right', 'justify'].includes(align)) result.textAlign = align;
        for (const [css, field] of [['margin-top', 'spaceBeforePt'], ['margin-bottom', 'spaceAfterPt']]) {
            if (!s[css]) continue; const value = lengthPt(s[css]);
            if (value !== null) result[field] = bounded(value, 0, 72, 1, warnings); else warnings.add('formatting');
        }
        if (s['text-indent']) { const pt = lengthPt(s['text-indent']); if (pt !== null && pt >= 0) result.firstLineIndentMm = bounded(pt * 25.4 / 72, 0, 40, .5, warnings); else warnings.add('formatting'); }
        const indent = s['margin-inline-start'] || s[ctx.dir === 'rtl' ? 'margin-right' : 'margin-left'];
        if (indent) { const pt = lengthPt(indent); if (pt !== null) result.indentLevel = bounded(pt * 25.4 / 72 / 5, 0, 8, 1, warnings); else warnings.add('formatting'); }
        const lineHeight = s['line-height'] || ctx.lineHeight;
        if (lineHeight && lineHeight !== 'normal') {
            const value = /^\d+(?:\.\d+)?$/.test(lineHeight) ? Number(lineHeight) : lineHeight.endsWith('%') ? parseFloat(lineHeight) / 100 : ctx.typography.fontSize ? lengthPt(lineHeight) / ctx.typography.fontSize : NaN;
            const choices = [1, 1.15, 1.5, 1.65, 2, 2.5, 3], closest = choices.reduce((a, b) => Math.abs(b - value) < Math.abs(a - value) ? b : a);
            if (Number.isFinite(value) && value > 0) { result.lineHeight = closest; if (Math.abs(value - closest) > .01) warnings.add('formatting'); } else warnings.add('formatting');
        }
        if (s['page-break-before'] === 'always' || /^(page|always)$/.test(s['break-before'] ?? '') || el.getAttribute('data-et-page-start') === 'true') {
            if (top && allowStarts) result.pageBreakBefore = true; else warnings.add('layout');
        }
        if (s['page-break-after'] === 'always' || /^(page|always)$/.test(s['break-after'] ?? '') || s['page-break-inside'] === 'avoid') warnings.add('layout');
        return result;
    }
    function textNode(text, ctx) {
        const marks = ['bold', 'italic', 'underline', 'strike'].filter(key => ctx[key]).map(type => ({ type }));
        if (ctx.script) marks.push({ type: ctx.script });
        if (Object.keys(ctx.typography).length) marks.push({ type: 'typography', attrs: { ...ctx.typography } });
        return { type: 'text', text, ...(marks.length ? { marks } : {}) };
    }
    function inline(node, ctx) {
        if (node.nodeType === 3) {
            if (ctx.pre) return node.data.replace(/\r\n?/g, '\n').split('\n').flatMap((text, i) => [...(i ? [{ type: 'hardBreak' }] : []), ...(text ? [textNode(text, ctx)] : [])]);
            const value = node.data.replace(/[\t\r\n ]+/g, ' '); return value ? [textNode(value, ctx)] : [];
        }
        if (node.nodeType !== 1) return [];
        if (ignored.test(node.tagName)) { if (/^(SVG|MATH|OBJECT|EMBED)$/.test(node.tagName)) warnings.add('images'); return []; }
        const s = style(node); if (/^ignore$/i.test(s['mso-list'] ?? '')) return [];
        if (s.display === 'none' || s.visibility === 'hidden' || s['mso-hide'] === 'all') { warnings.add('formatting'); return []; }
        if (/^(V:|M:|O:OLE)/.test(node.tagName)) warnings.add('images');
        if (node.tagName === 'IMG') { warnings.add('images'); return node.getAttribute('alt') ? [textNode(node.getAttribute('alt'), ctx)] : []; }
        if (node.tagName === 'A' && node.hasAttribute('href')) warnings.add('links');
        if (node.tagName === 'BR') { if (s['page-break-before'] || s['break-before']) warnings.add('layout'); return [{ type: 'hardBreak' }]; }
        const next = context(node, ctx); return [...node.childNodes].flatMap(child => inline(child, next));
    }
    function textBlock(el, ctx, top) {
        const heading = /^H([1-6])$/.exec(el.tagName) || /MsoHeading([1-6])/i.exec(el.className ?? '');
        const a = attrs(el, ctx, top), content = [...el.childNodes].flatMap(node => inline(node, ctx));
        return heading ? { type: 'heading', attrs: { ...a, level: Number(heading[1]) }, content } : paragraph(content, a);
    }
    function table(el, ctx, top) {
        const rows = [...el.querySelectorAll('tr')].filter(row => row.closest('table') === el);
        const cells = rows.map(row => [...row.children].filter(cell => /^(TD|TH)$/.test(cell.tagName)));
        const spans = cells.map(row => row.map(cell => Number(cell.getAttribute('colspan') ?? 1)));
        const columns = spans[0]?.reduce((a, b) => a + b, 0);
        const captions = [...el.children].filter(child => child.tagName === 'CAPTION').map(caption => textBlock(caption, context(caption, ctx), false));
        if (captions.length) warnings.add('formatting');
        const valid = allowTables && top && rows.length > 0 && rows.length <= 50 && columns > 0 && columns <= 12 && !el.querySelector('table') &&
            cells.every((row, i) => row.length && spans[i].every(n => Number.isInteger(n) && n >= 1 && n <= 12) && spans[i].reduce((a, b) => a + b, 0) === columns && row.every(cell => Number(cell.getAttribute('rowspan') ?? 1) === 1));
        if (!valid) { warnings.add('table'); return [...captions, ...cells.flatMap(row => row.flatMap(cell => blocks(cell, context(cell, ctx), false)))]; }
        const result = { type: 'table', attrs: ctx.dir ? { dir: ctx.dir } : {}, content: cells.map((row, i) => ({ type: 'tableRow', content: row.map((cell, j) => {
            const c = context(cell, ctx), s = style(cell), a = { colspan: spans[i][j], rowspan: 1, ...(c.dir ? { dir: c.dir } : {}) };
            const bg = clipboardColor(s['background-color'] || cell.getAttribute('bgcolor')); if (bg) a.backgroundColor = bg;
            const border = ['top', 'right', 'bottom', 'left'].map(side => [s[`border-${side}-color`], s[`border-${side}-width`], s[`border-${side}-style`]]);
            if (border.some(parts => parts.some(Boolean))) {
                if (border.every(parts => parts.join() === border[0].join())) {
                    const [color, width, kind] = border[0], pt = lengthPt(width);
                    if (clipboardColor(color)) a.borderColor = clipboardColor(color);
                    if (kind === 'none' || kind === 'hidden') a.borderWidthPt = 0;
                    else { if (['solid', 'dashed', 'dotted'].includes(kind)) a.borderStyle = kind; else warnings.add('formatting');
                        if (pt !== null && [0, .5, 1, 1.5, 2, 3].some(v => Math.abs(v - pt) < .01)) a.borderWidthPt = [0, .5, 1, 1.5, 2, 3].find(v => Math.abs(v - pt) < .01); else if (width) warnings.add('formatting'); }
                } else warnings.add('formatting');
            }
            const content = blocks(cell, c, false); return { type: cell.tagName === 'TH' ? 'tableHeader' : 'tableCell', attrs: a, content: content.length ? content : [paragraph()] };
        }) })) };
        const cols = [...el.querySelectorAll(':scope > colgroup > col, :scope > col')];
        const measurements = cols.length === columns ? cols.map(col => style(col).width || col.getAttribute('width')) : spans[0].every(n => n === 1) ? cells[0].map(cell => style(cell).width || cell.getAttribute('width')) : [];
        if (measurements.length) {
            const percent = measurements.every(v => /%$/.test(v ?? ''));
            const widths = columnPercentages(measurements.map(v => percent ? parseFloat(v) : lengthPt(v)));
            if (widths) result.attrs.columnWidths = widths; else if (measurements.some(Boolean)) warnings.add('formatting');
        }
        return [...captions, result];
    }
    function blocks(container, ctx, top) {
        const output = []; let pending = [];
        const flush = () => { if (pending.some(n => n.type !== 'text' || n.text.trim())) output.push(paragraph(pending, ctx.dir ? { dir: ctx.dir } : {})); pending = []; };
        for (const node of container.childNodes) {
            if (node.nodeType === 1 && (style(node).display === 'none' || style(node).visibility === 'hidden' || style(node)['mso-hide'] === 'all')) { warnings.add('formatting'); continue; }
            if (node.nodeType !== 1 || !blockTags.test(node.tagName)) { pending.push(...inline(node, ctx)); continue; }
            flush(); const next = context(node, ctx), tag = node.tagName;
            if (tag === 'TABLE') { output.push(...table(node, next, top)); continue; }
            if (tag === 'UL' || tag === 'OL') {
                const items = [...node.children].filter(n => n.tagName === 'LI').map(li => {
                    const content = blocks(li, context(li, next), false); if (!content.length || content[0].type !== 'paragraph') content.unshift(paragraph()); return { type: 'listItem', content };
                });
                const start = Number(node.getAttribute('start') ?? 1), type = node.getAttribute('type');
                if (items.length) output.push({ type: tag === 'UL' ? 'bulletList' : 'orderedList', attrs: { ...(next.dir ? { dir: next.dir } : {}), ...(tag === 'OL' ? { start: Number.isInteger(start) && start >= 1 && start <= 10000 ? start : 1, ...(['1', 'a', 'A', 'i', 'I'].includes(type) ? { type } : {}) } : {}) }, content: items });
                continue;
            }
            if (tag === 'BLOCKQUOTE') { const content = blocks(node, next, false); if (content.length) output.push({ type: 'blockquote', content }); continue; }
            if (/^(DIV|SECTION|ARTICLE|HEADER|FOOTER|LI|HTML|BODY)$/.test(tag) && [...node.children].some(child => blockTags.test(child.tagName))) { output.push(...blocks(node, next, top)); continue; }
            const value = textBlock(node, next, top), list = /\bl\d+\s+level(\d+)\s+lfo\d+/i.exec(style(node)['mso-list'] ?? '');
            if (list) {
                const marker = [...node.querySelectorAll('span')].find(span => /^ignore$/i.test(style(span)['mso-list'] ?? ''))?.textContent ?? '';
                const numbered = /^\s*(\d+)[.)]/.exec(marker), type = numbered ? 'orderedList' : 'bulletList';
                if (Number(list[1]) > 1 || !marker.trim() || !numbered && !/^[\s\u00a0]*[•·●○▪■\-]/.test(marker)) warnings.add('lists');
                if (value.attrs?.pageBreakBefore) { delete value.attrs.pageBreakBefore; warnings.add('layout'); }
                const previous = output.at(-1), key = style(node)['mso-list'];
                if (previous?.type === type && previous._listKey === key && (!numbered || previous.attrs.start + previous.content.length === Number(numbered[1]))) previous.content.push({ type: 'listItem', content: [value] });
                else output.push({ type, ...(numbered ? { attrs: { start: bounded(Number(numbered[1]), 1, 10000, 1, warnings) } } : {}), _listKey: key, content: [{ type: 'listItem', content: [value] }] });
            } else output.push(value);
        }
        flush(); for (const node of output) delete node._listKey; return output;
    }
    try {
        let content = { type: 'doc', content: blocks(root, { typography: {} }, true) };
        if (mode === 'plain') {
            const lines = []; const read = node => { if (['paragraph', 'heading'].includes(node.type)) lines.push((node.content ?? []).map(n => n.text ?? (n.type === 'hardBreak' ? '\n' : '')).join('')); else for (const child of node.content ?? []) read(child); }; read(content);
            content = plainClipboard(lines.join('\n')); warnings.clear();
        }
        if (!content.content.length || !content.content.some(node => node.type !== 'paragraph' || node.content.length)) return { error: 'empty', warnings: [...warnings] };
        if (!isRichDocument(content, 10) || JSON.stringify(content).length > 2_000_000) return { error: 'limit', warnings: [...warnings] };
        return { content, warnings: [...warnings], plain: mode === 'plain' };
    } catch { return { error: 'invalid', warnings: [...warnings] }; }
}
