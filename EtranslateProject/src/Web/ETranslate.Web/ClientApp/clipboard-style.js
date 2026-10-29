// Bounded conversion into the application's existing vocabulary; no CSS is injected.
export const pasteWarningCodes = Object.freeze(['fonts', 'formatting', 'layout', 'images', 'links', 'table', 'lists', 'paragraphContext']);
export function neutralizeClipboardStyles(html) {
    // Rename style attributes before even an inert HTML parser sees them. Chromium reports
    // CSP violations for the original attributes; only whitelisted CSS is read afterwards.
    let output = '', index = 0; const lower = html.toLowerCase();
    while (index < html.length) {
        const start = html.indexOf('<', index); if (start < 0) return output + html.slice(index);
        output += html.slice(index, start);
        if (html.startsWith('<!--', start)) { const end = html.indexOf('-->', start + 4); const next = end < 0 ? html.length : end + 3; output += html.slice(start, next); index = next; continue; }
        const name = /^<([a-z][\w:.-]*)/i.exec(html.slice(start));
        if (!name) { output += '<'; index = start + 1; continue; }
        let end = start + name[0].length, quote = '';
        for (; end < html.length; end++) {
            const char = html[end]; if (quote) { if (char === quote) quote = ''; }
            else if (char === '"' || char === "'") quote = char; else if (char === '>') break;
        }
        const tag = html.slice(start, Math.min(html.length, end + 1));
        output += name[0] + tag.slice(name[0].length).replace(/(\s+)([^\s"'<>\/=]+)(\s*=\s*(?:"[^"]*"|'[^']*'|[^\s"'=<>`]+))?/g,
            (all, space, key, value = '') => key.toLowerCase() === 'style' ? space + 'data-et-clipboard-style' + value : all);
        index = end + 1;
        if (/^(style|script)$/i.test(name[1])) {
            const close = lower.indexOf('</' + name[1].toLowerCase(), index);
            if (close < 0) return output + html.slice(index);
            output += html.slice(index, close); index = close;
        }
    }
    return output;
}
export function lengthPt(value) {
    const match = /^(-?(?:\d+(?:\.\d*)?|\.\d+))(pt|px|cm|mm|in)?$/i.exec(String(value).trim());
    if (!match) return null;
    return Number(match[1]) * ({ pt: 1, px: .75, cm: 72 / 2.54, mm: 72 / 25.4, in: 72 }[match[2]?.toLowerCase() ?? 'px']);
}
const colors = { black: '#000000', white: '#ffffff', red: '#ff0000', green: '#008000', blue: '#0000ff', yellow: '#ffff00', gray: '#808080', grey: '#808080', silver: '#c0c0c0', maroon: '#800000', purple: '#800080', fuchsia: '#ff00ff', lime: '#00ff00', olive: '#808000', navy: '#000080', teal: '#008080', aqua: '#00ffff', orange: '#ffa500' };
export function clipboardColor(value) {
    value = String(value).trim().toLowerCase();
    if (colors[value]) return colors[value];
    if (/^#[0-9a-f]{6}$/.test(value)) return value;
    if (/^#[0-9a-f]{3}$/.test(value)) return '#' + [...value.slice(1)].map(c => c + c).join('');
    const rgb = /^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*(1(?:\.0+)?))?\s*\)$/.exec(value);
    return rgb && rgb.slice(1, 4).every(c => Number(c) <= 255) ? '#' + rgb.slice(1, 4).map(c => Number(c).toString(16).padStart(2, '0')).join('') : null;
}
export function clipboardFont(value, warnings) {
    const name = String(value).split(',')[0].replace(/["']/g, '').trim().toLowerCase();
    const own = { 'noto sans': 'sans', 'noto serif': 'serif', 'noto sans arabic': 'arabic' };
    if (own[name]) return own[name];
    warnings.add('fonts');
    return /times|cambria|georgia|garamond|^serif$/.test(name) ? 'serif' : /arabic|amiri|tahoma/.test(name) ? 'arabic' : 'sans';
}
export function bounded(value, min, max, step, warnings) {
    const result = Math.max(min, Math.min(max, Math.round(value / step) * step));
    if (Math.abs(result - value) > .01) warnings.add('formatting');
    return result;
}
export function columnPercentages(values) {
    if (!values.length || values.length > 12 || values.some(v => !Number.isFinite(v) || v <= 0)) return null;
    const sum = values.reduce((a, b) => a + b), raw = values.map(v => v / sum * 100), result = raw.map(Math.floor);
    if (result.some(v => v < 1)) return null;
    const order = raw.map((v, i) => ({ i, fraction: v - result[i] })).sort((a, b) => b.fraction - a.fraction || a.i - b.i);
    const remainder = 100 - result.reduce((a, b) => a + b);
    for (let i = 0; i < remainder; i++) result[order[i].i]++;
    return result;
}
