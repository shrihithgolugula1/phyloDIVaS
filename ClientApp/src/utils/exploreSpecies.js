const normalize = value => String(value ?? '').trim().toLowerCase();

// Only split when an exact name identifies a single scientific species.
export function groupExactSpecies(rows, query) {
    const term = normalize(query);
    const matches = rows.filter(row => term && (
        normalize(row.organism.name) === term || normalize(row.organism.commonName) === term
    ));
    const species = new Set(matches.map(row => normalize(row.organism.name)).filter(name => name && name !== 'n/a'));
    if (species.size !== 1) return { primary: rows, related: [] };
    const [name] = species;
    const canonical = rows.find(row => normalize(row.organism.name) === name);
    return {
        primary: [canonical],
        related: rows.filter(row => row !== canonical),
    };
}
