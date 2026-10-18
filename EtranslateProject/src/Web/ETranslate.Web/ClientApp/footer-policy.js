export function footerPolicy(json) {
    if (!json?.trim()) return 'AllPages';
    try {
        const root = JSON.parse(json);
        if (!root || typeof root !== 'object' || Array.isArray(root)) return null;
        const value = Object.hasOwn(root, 'footerPlacement') ? root.footerPlacement : 'AllPages';
        return ['AllPages', 'LastPage'].includes(value) ? value : null;
    } catch { return null; }
}
export function showFooter(policy, pageRole) { return policy !== 'LastPage' || pageRole !== 'Earlier'; }
