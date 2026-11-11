import {expect,it} from 'vitest';
import {readFileSync} from 'node:fs';
import {parseMembership,parseTree,layoutTree} from './demoData';
import {benjaminiHochberg} from '../../../utils/hyphyDashboard';
import {summarizeHyphyResult} from '../../../utils/hyphyResults';
const file=name=>readFileSync(new URL(`../../../../public/demo/bats/${name}`,import.meta.url),'utf8');
it('reconciles the complete saved cohort and preserves missing tests as unavailable',()=>{
    const snapshot=JSON.parse(file('genome-summary.json'));
    expect(snapshot.requested).toBe(9978);expect(snapshot.genes).toHaveLength(3177);
    expect(snapshot.genes.filter(g=>['busted_status','meme_status','absrel_status'].every(k=>g[k]==='valid'))).toHaveLength(288);
    for(const method of ['busted','absrel','meme']) {
        expect(Object.values(snapshot.methods[method]).reduce((a,b)=>a+b,0)).toBe(snapshot.requested);
        expect(snapshot.genes.filter(g=>g[`${method}_status`]==='valid')).toHaveLength(snapshot.methods[method].ok);
    }
    const q=benjaminiHochberg(snapshot.genes.map(g=>({id:g.orthogroup,pValue:g.BUSTED_p})));
    for(const g of snapshot.genes) {
        if(g.busted_status==='valid')expect(q.get(g.orthogroup)).toBeCloseTo(g.BUSTED_q_BH,12);
        else expect(g.BUSTED_p).toBeNull();
    }
});
it('includes downloadable strict and submitted memberships without conflating their counts',()=>{
    const submitted=parseMembership(file('orthogroups.tsv'));const strict=parseMembership(file('strict-N0.tsv'));
    expect(submitted.rows).toHaveLength(9978);expect(strict.rows).toHaveLength(9261);
    expect(new Set(strict.rows.map(row=>row[0])).size).toBe(strict.rows.length);
    expect(strict.rows.every(row=>row[2].toLowerCase()==='n0'&&row.slice(3).every(cell=>cell&&!cell.includes(',')))).toBe(true);
    expect(submitted.species).toHaveLength(8);
});
it('preserves a rectangular scaled tree and eight credited local pictures',()=>{
    const tree=layoutTree(parseTree(file('species-tree.nwk')));const icons=JSON.parse(file('silhouettes.json'));
    expect(tree.nodes.filter(n=>!n.children.length)).toHaveLength(8);
    for(const node of tree.nodes)if(node.parent)expect((node.x-node.parent.x)/480*tree.maximum).toBeCloseTo(node.length,10);
    for(const node of tree.nodes.filter(n=>!n.children.length)) {
        const icon=icons[node.name.replaceAll('_',' ')];expect(icon.url).toMatch(/^\/demo\/bats\/silhouette-/);expect(icon.credit).toBeTruthy();expect(icon.license).toMatch(/creativecommons.org/);
    }
    expect(()=>parseTree('(A:NaN,B:1);')).toThrow();expect(()=>parseMembership('wrong\nrow')).toThrow();
});
it('cross-checks every packaged native report against its genome-wide record',()=>{
    const snapshot=JSON.parse(file('genome-summary.json'));const examples=JSON.parse(file('examples.json'));
    for(const item of examples) {
        const example=JSON.parse(file(item.gene+'.json'));const record=snapshot.genes.find(g=>g.orthogroup===example.gene);
        expect(record).toBeTruthy();
        for(const [method,entry] of Object.entries(example.methods)) {
            const summary=summarizeHyphyResult(entry.nativeJson,method);expect(summary.resultAvailable).toBe(true);expect(summary.sequences).toBe(8);
            if(method==='BUSTED')expect(summary.pValue).toBeCloseTo(record.BUSTED_p,12);
            if(method==='aBSREL')expect(summary.selectedBranches).toBe(record.aBSREL_branches_within_gene005);
            if(method==='MEME')expect(summary.selectedSites).toBe(record.MEME_sites_raw01);
            expect(JSON.stringify(entry)).not.toMatch(/\/Users\/|\/data\/|[a-f0-9]{32}/);
        }
    }
});
