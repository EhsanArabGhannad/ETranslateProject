export function selectedTable(state) {
    function containing(position) {
        for (let depth = position.depth; depth > 0; depth--) {
            const node = position.node(depth);
            if (node.type.name === 'table') return depth === 1 ? { node, pos: position.before(depth) } : null;
        }
        return null;
    }
    const from = containing(state.selection.$from), to = containing(state.selection.$to);
    return from && to && from.pos === to.pos ? from : null;
}
