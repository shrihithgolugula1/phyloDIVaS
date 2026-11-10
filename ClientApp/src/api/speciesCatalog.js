// Simple species catalog: common name -> scientific name
export const speciesCatalog = [
  { common: 'Human', scientific: 'Homo sapiens' },
  { common: 'Mouse', scientific: 'Mus musculus' },
  { common: 'Dog', scientific: 'Canis lupus familiaris' },
  { common: 'Cat', scientific: 'Felis catus' },
  { common: 'Chimpanzee', scientific: 'Pan troglodytes' },
  { common: 'Rat', scientific: 'Rattus norvegicus' },
  { common: 'Zebrafish', scientific: 'Danio rerio' },
  { common: 'Fruit fly', scientific: 'Drosophila melanogaster' },
  { common: 'Mosquito', scientific: 'Anopheles gambiae' },
];

export function filterSpecies(query) {
  if (!query) return speciesCatalog;
  const q = query.toLowerCase();
  return speciesCatalog.filter(s => s.common.toLowerCase().includes(q) || s.scientific.toLowerCase().includes(q));
}
