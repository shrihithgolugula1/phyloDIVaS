import {benjaminiHochberg} from './hyphyDashboard.js';

export function parseGmt(text) {
    const lines=String(text).trim().split(/\r?\n/);if(lines.length>5000)throw Error('Use at most 5,000 gene sets.');
    const sets=lines.filter(Boolean).map(line=>{const [name,description,...genes]=line.split('\t');if(!name||!genes.length)throw Error('GMT rows need a name, description, and gene identifiers separated by tabs.');return {id:name,name,description,genes:[...new Set(genes.filter(Boolean))]};});
    if(new Set(sets.map(s=>s.id)).size!==sets.length)throw Error('Gene-set names must be unique.');return sets;
}
export function prepareRankedFamily(genes, geneSets, annotatedIds=null) {
    const allowed=annotatedIds?new Set(annotatedIds):null;
    const ranked=genes.filter(g=>g.methodStatus?.BUSTED==='complete'&&g.bustedPValue!=null&&Number.isFinite(g.bustedPValue)&&g.bustedPValue>=0&&g.bustedPValue<=1&&(!allowed||allowed.has(g.orthogroupId))).map(g=>({id:g.orthogroupId,symbol:g.symbol,score:-Math.log10(Math.max(g.bustedPValue,1e-12))})).sort((a,b)=>b.score-a.score||a.id.localeCompare(b.id));
    const ids=new Set(ranked.map(g=>g.id)),symbols=new Map();
    for(const g of ranked)if(g.symbol){const key=g.symbol.toUpperCase();symbols.set(key,symbols.has(key)?null:g.id);}
    const sets=geneSets.map(set=>({...set,genes:[...new Set(set.genes.map(value=>ids.has(value)?value:symbols.get(String(value).toUpperCase())).filter(Boolean))]}));
    return {ranked,sets};
}
export function runningScore(scores,positions,curve=false) {
    const n=scores.length,k=positions.length;if(!k||k>=n)return null;
    const sorted=[...positions].sort((a,b)=>a-b);let total=sorted.reduce((s,i)=>s+scores[i],0),equal=total===0;if(equal)total=k;
    let hit=0,max=0,min=0,peak=0;const points=[{rank:0,value:0}];
    for(let j=0;j<sorted.length;j++){
        const position=sorted[j],before=hit-(position-j)/(n-k),after=before+(equal?1:scores[position])/total;
        if(before<min){min=before;if(Math.abs(min)>Math.abs(max))peak=position;}
        if(after>max){max=after;if(Math.abs(max)>=Math.abs(min))peak=position+1;}
        if(curve)points.push({rank:position,value:before},{rank:position+1,value:after});hit+=(equal?1:scores[position])/total;
    }
    const es=Math.abs(max)>=Math.abs(min)?max:min;if(curve)points.push({rank:n,value:0});return {es,peak,points};
}
export function runRankedEnrichment(ranked,sets,{permutations=999,minSize=15,maxSize=500,seed=1729}={},onProgress=()=>{}) {
    if(ranked.length<minSize+1||!ranked.some(g=>g.score>0))return [];
    let state=seed>>>0;const random=()=>{state=(1664525*state+1013904223)>>>0;return state/4294967296;};
    const scores=ranked.map(g=>g.score),index=new Map(ranked.map((g,i)=>[g.id,i])),eligible=sets.map(s=>({...s,positions:[...new Set(s.genes.map(id=>index.get(id)).filter(i=>i!==undefined))]})).filter(s=>s.positions.length>=minSize&&s.positions.length<=maxSize&&s.positions.length<ranked.length);
    const results=[];
    for(const [ordinal,set] of eligible.entries()){
        const observed=runningScore(scores,set.positions);let same=0,extreme=0,nullTotal=0;
        for(let p=0;p<permutations;p++){
            const chosen=new Set();for(let j=ranked.length-set.positions.length;j<ranked.length;j++){const t=Math.floor(random()*(j+1));chosen.add(chosen.has(t)?j:t);}
            const es=runningScore(scores,[...chosen]).es;
            if((es>=0)===(observed.es>=0)){same++;nullTotal+=Math.abs(es);if(Math.abs(es)>=Math.abs(observed.es)-1e-12)extreme++;}
        }
        results.push({id:set.id,name:set.name,description:set.description,size:set.positions.length,es:observed.es,nes:same&&nullTotal?observed.es/(nullTotal/same):null,pValue:(extreme+1)/(same+1),genes:set.positions.map(i=>ranked[i].id),peak:observed.peak,permutations});
        onProgress(ordinal+1,eligible.length);
    }
    const q=benjaminiHochberg(results.map(r=>({id:r.id,pValue:r.pValue})));return results.map(r=>({...r,qValue:q.get(r.id)})).sort((a,b)=>a.qValue-b.qValue||Math.abs(b.nes)-Math.abs(a.nes));
}
