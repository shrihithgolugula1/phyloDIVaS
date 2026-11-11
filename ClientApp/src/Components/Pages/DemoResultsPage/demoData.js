export function parseMembership(text) {
    const lines = text.trim().split(/\r?\n/);
    const header = lines.shift().split('\t');
    if (header[0] !== 'HOG' || header.length < 4) throw new Error('Invalid saved orthogroup table.');
    const species = header.slice(3).map(name => name.replace(/_proteome_filtered$/i, '').replaceAll('_', ' ').replace(/^[a-z]/, c => c.toUpperCase()));
    const rows = lines.map(line => line.split('\t'));
    if (rows.some(row => row.length !== header.length || !/^N\d+\.HOG\d+$/.test(row[0]))) throw new Error('Incomplete saved membership rows.');
    return {species, rows};
}

export function parseTree(text) {
    const tokens = text.match(/\(|\)|,|:|;|[^\s(),:;]+/g) || [];
    let cursor = 0;
    const node = () => {
        const result = {children: [], name: '', length: 0};
        if (tokens[cursor] === '(') {
            cursor++;
            do { result.children.push(node()); } while (tokens[cursor++] === ',');
        }
        if (tokens[cursor] && ![':',',',')',';'].includes(tokens[cursor])) result.name = tokens[cursor++];
        if (tokens[cursor] === ':') { cursor++; result.length = Number(tokens[cursor++]); }
        if (!Number.isFinite(result.length) || result.length < 0) throw new Error('Invalid tree branch length.');
        return result;
    };
    const root = node();
    if (tokens[cursor] !== ';' || cursor !== tokens.length - 1) throw new Error('Invalid saved Newick tree.');
    return root;
}

export function layoutTree(root) {
    const nodes = [];
    let leaf = 0;
    const walk = (node, distance, parent = null) => {
        const current = {...node, distance, parent}; nodes.push(current);
        current.children = node.children.map(child => walk(child, distance + child.length, current));
        current.y = current.children.length ? current.children.reduce((sum, child) => sum + child.y, 0) / current.children.length : 40 + leaf++ * 62;
        return current;
    };
    walk(root, 0);
    const maximum = Math.max(...nodes.map(n => n.distance));
    nodes.forEach(n => { n.x = 30 + (maximum ? n.distance / maximum * 480 : 0); });
    return {nodes, maximum, height: leaf * 62 + 75};
}

export async function readDemoAsset(path, text = false) {
    if (!path.startsWith('/demo/bats/')) throw new Error('Invalid demo asset.');
    const response = await fetch(path);
    if (!response.ok) throw new Error('A saved demo file could not be loaded. Refresh to retry.');
    return text ? response.text() : response.json();
}
