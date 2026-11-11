// Reproducible optional enrichment cache, using the same implementation as the UI worker.
import {readFileSync,writeFileSync} from 'node:fs';
import {prepareRankedFamily,runRankedEnrichment} from '../ClientApp/src/utils/rankedEnrichment.js';
const [summaryPath,setsPath]=process.argv.slice(2);
if(!summaryPath||!setsPath)throw Error('Usage: node scripts/compute_demo_enrichment.mjs SUMMARY_JSON GENE_SETS_JSON');
const snapshot=JSON.parse(readFileSync(summaryPath,'utf8')),sets=JSON.parse(readFileSync(setsPath,'utf8'));
const genes=snapshot.genes.map(g=>({orthogroupId:g.orthogroup,symbol:g.consensus_bat_symbol,bustedPValue:g.BUSTED_p,methodStatus:{BUSTED:g.busted_status==='valid'?'complete':g.busted_status}}));
const family=prepareRankedFamily(genes,sets.sets,sets.annotatedIds),options={permutations:999,minSize:15,maxSize:500,seed:1729};
const results=runRankedEnrichment(family.ranked,family.sets,options);
sets.precomputed={methodVersion:1,ranking:family.ranked.map(({id,score})=>({id,score})),options,results};writeFileSync(setsPath,JSON.stringify(sets)+'\n');
console.log(JSON.stringify({rankedGenes:family.ranked.length,eligibleSets:results.length,significant:results.filter(r=>r.qValue<=.05).length}));
