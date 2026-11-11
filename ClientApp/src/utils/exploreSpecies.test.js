import { describe, expect, it } from 'vitest';
import { groupExactSpecies } from './exploreSpecies';
const row = (name, commonName) => ({ organism: { name, commonName } });
const human = row('Homo sapiens', 'human');
const humanAssembly = row('Homo sapiens', '');
const chimp = row('Pan troglodytes', 'chimpanzee');

describe('exact Explore species grouping', () => {
    it.each(['human', ' HOMO SAPIENS '])('groups exact %s with all same-species assemblies', query => {
        expect(groupExactSpecies([chimp, human, humanAssembly], query)).toEqual({
            primary: [human, humanAssembly], related: [chimp],
        });
    });
    it.each(['primates', 'hom', '', '9606', 'GCF_123'])('leaves non-exact query %s intact', query => {
        expect(groupExactSpecies([human, chimp], query)).toEqual({ primary: [human, chimp], related: [] });
    });
    it('does not narrow ambiguous common names', () => {
        const rows = [row('Species one', 'shared'), row('Species two', 'shared')];
        expect(groupExactSpecies(rows, 'shared')).toEqual({ primary: rows, related: [] });
    });
});
