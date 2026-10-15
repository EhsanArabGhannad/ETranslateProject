export const alignments = ['auto', 'left', 'center', 'right'];
export function plainRegion(text, type = 'doc', alignment = []) {
    return { type, content: text === '' ? [] : text.replace(/\r\n/g, '\n').split('\n').map((line, i) => ({
        type: 'paragraph', ...(alignment[i] && alignment[i] !== 'auto' && alignments.includes(alignment[i]) ? { attrs: { textAlign: alignment[i] } } : {}),
        content: [{ type: 'text', text: line }]
    })) };
}
export function previewHeader(text, alignment, original, { replace = false, imageId = null, imageAlign = '', imageLayout = '' } = {}) {
    const root = plainRegion(text, 'header', alignment);
    const images = replace && imageId ? [] : structuredClone(original?.content?.filter(n => n.type === 'image') ?? []);
    if (imageId && !images.some(n => n.attrs?.assetId === imageId)) images.push({ type: 'image', attrs: { assetId: imageId } });
    for (const image of images) {
        if (['left', 'center', 'right'].includes(imageAlign)) image.attrs.align = imageAlign;
        if (['logo', 'banner'].includes(imageLayout)) image.attrs.layout = imageLayout;
    }
    root.content.push(...images); return root;
}
