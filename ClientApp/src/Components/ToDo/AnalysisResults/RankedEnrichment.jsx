import React,{useMemo,useState,useRef,useEffect} from 'react';
import {parseGmt,prepareRankedFamily,runningScore} from '../../../utils/rankedEnrichment';
import {downloadSvg,downloadSvgPng,downloadText} from '../../../utils/downloads';
import {rowsToDelimited} from '../../../utils/hyphyDashboard';
import styles from './AnalysisResults.module.css';

export default function RankedEnrichment({genes,geneSets=null,familyReady=true}) {
 const [uploaded,setUploaded]=useState(null),[results,setResults]=useState(null),[selected,setSelected]=useState(null),[error,setError]=useState(''),[progress,setProgress]=useState(null);const worker=useRef(null),svg=useRef(null);
 const annotated=useMemo(()=>{
  if(geneSets)return geneSets;
  const sets=new Map(),ids=[];
  genes.forEach(g=>{if(g.goTerms?.length)ids.push(g.orthogroupId);g.goTerms?.forEach(t=>{const id=typeof t==='string'?t:t.id;if(!id)return;if(!sets.has(id))sets.set(id,{id,name:t.name||id,genes:[]});sets.get(id).genes.push(g.orthogroupId);});});
  return {sets:[...sets.values()],annotatedIds:ids,source:'GO annotations supplied with this analysis'};
 },[genes,geneSets]);
 const family=useMemo(()=>prepareRankedFamily(genes,uploaded||annotated.sets,uploaded?null:annotated.annotatedIds),[genes,uploaded,annotated]);
 useEffect(()=>{
  worker.current?.terminate();setProgress(null);
  const cached=!uploaded&&geneSets?.precomputed;
  const matches=cached?.methodVersion===1&&cached.ranking.length===family.ranked.length&&cached.ranking.every((row,i)=>row.id===family.ranked[i].id&&row.score===family.ranked[i].score);
  setResults(matches?cached.results:null);setSelected(matches?(cached.results.find(r=>r.nes>0)||cached.results[0]||null):null);
  return()=>worker.current?.terminate();
 },[family,geneSets,uploaded]);
 const run=()=>{setError('');setProgress({done:0,total:family.sets.length});const w=new Worker(new URL('../../../utils/rankedEnrichment.worker.js',import.meta.url),{type:'module'});worker.current=w;w.onmessage=({data})=>{if(data.error){setError(data.error);setProgress(null);w.terminate();}if(data.progress)setProgress(data.progress);if(data.results){setResults(data.results);setSelected(data.results[0]||null);setProgress(null);w.terminate();}};w.onerror=()=>{setError('Enrichment worker could not finish. Try a smaller gene-set file.');setProgress(null);w.terminate();};w.postMessage({ranked:family.ranked,sets:family.sets,options:{permutations:999,minSize:15,maxSize:500}});};
 const importFile=async event=>{try{const file=event.target.files?.[0];if(!file)return;if(file.size>10*1024*1024)throw Error('Use a GMT file smaller than 10 MB.');setUploaded(parseGmt(await file.text()));setError('');}catch(e){setError(e.message);}event.target.value='';};
 const curve=useMemo(()=>{if(!selected)return null;const hits=new Set(selected.genes);return runningScore(family.ranked.map(g=>g.score),family.ranked.flatMap((g,i)=>hits.has(g.id)?[i]:[]),true);},[family,selected]);
 const max=Math.max(.01,...(curve?.points||[]).map(p=>Math.abs(p.value))),x=rank=>60+rank/Math.max(1,family.ranked.length)*660,y=value=>145-value/max*105;
 return <section className={styles.dashboardFigure} aria-labelledby="ranked-enrichment-title"><h5 id="ranked-enrichment-title">Ranked gene-set enrichment</h5>
  <p>Exploratory weighted preranked enrichment using −log10(BUSTED p). The ranking has no direction of biological change. This asks whether a functional set is concentrated toward stronger or weaker selection evidence.</p>
  <p>{family.ranked.length.toLocaleString()} ranked tested genes · {family.sets.length.toLocaleString()} supplied sets. {uploaded?'User-supplied GMT; identifiers match HOG IDs or unique gene symbols.':annotated.source}. {annotated.sourceUrl&&!uploaded&&<a href={annotated.sourceUrl} target="_blank" rel="noreferrer">Annotation source</a>}</p>
  {!uploaded&&<p>Only tested genes with reference annotations enter this enrichment background; the genome-wide selection figures still include every tested gene. Mapped annotations do not establish experimental validation.</p>}
  <label>Import gene sets (GMT)<input className="form-control" type="file" accept=".gmt,.txt" onChange={importFile}/></label>
  <div className={styles.figureActions}><button className="btn btn-outline-primary" disabled={!familyReady||!family.sets.length||family.ranked.length<16||Boolean(progress)} onClick={run}>Calculate enrichment</button>{uploaded&&<button className="btn btn-outline-secondary" onClick={()=>setUploaded(null)}>Use supplied GO annotations</button>}{progress&&<button className="btn btn-outline-secondary" onClick={()=>{worker.current?.terminate();setProgress(null);}}>Cancel enrichment</button>}</div>
  {!familyReady&&<p>Load the full result family before enrichment. A displayed search subset is never used as the ranking background.</p>}
  {!family.sets.length&&<p>Functional gene sets are not present. Import a GMT file with suitable annotations for these species to calculate enrichment.</p>}
  {progress&&<p role="status">Calculating enrichment: {progress.done} of {progress.total} sets processed…</p>}{error&&<p role="alert">{error}</p>}
  <p className={styles.figureNote}>Uses all ranked genes in the stated background, 999 seeded gene-set permutations, weighted running-sum ES, sign-matched NES and permutation p-values. BH q-values correct all tested sets; these are not the pooled GSEA FDR statistic. Sets must contain 15–500 ranked genes. Alignment quality and gene-length bias require independent review. <a href="https://docs.gsea-msigdb.org/GSEA/GSEA_User_Guide/" target="_blank" rel="noreferrer">Method guidance</a></p>
  {results&&<><p>{results.length} eligible sets tested; {results.filter(r=>r.qValue<=.05).length} pass BH q ≤ 0.05.</p>{!results.length&&<p>No sets meet the size and mapping requirements.</p>}{results.length>0&&<><label>Gene set<select className="form-select" value={selected?.id||''} onChange={e=>setSelected(results.find(r=>r.id===e.target.value))}>{results.map(r=><option key={r.id} value={r.id}>{r.name} · q={r.qValue.toPrecision(3)}</option>)}</select></label>
  {selected&&curve&&<><p>{selected.name}: ES {selected.es.toPrecision(3)}, NES {selected.nes?.toPrecision(3)??'unavailable'}, p {selected.pValue.toPrecision(3)}, BH q {selected.qValue.toPrecision(3)}; {selected.size} genes.</p><svg ref={svg} viewBox="0 0 760 350" className={styles.figureSvg} role="img" aria-label="Gene-set enrichment running-score plot"><line x1="60" x2="720" y1="145" y2="145" stroke="#8d99a5"/><polyline points={curve.points.map(p=>`${x(p.rank)},${y(p.value)}`).join(' ')} fill="none" stroke="#167660" strokeWidth="2.5"/>{family.ranked.map((g,i)=>selected.genes.includes(g.id)?<line key={g.id} x1={x(i)} x2={x(i)} y1="275" y2="292" stroke="#172b46"/>:null)}<text x="390" y="333" textAnchor="middle" className={styles.axisLabel}>Genes ranked by BUSTED evidence (stronger → weaker)</text><text x="16" y="145" transform="rotate(-90 16 145)" textAnchor="middle" className={styles.axisLabel}>Running enrichment score</text><text x="60" y="314" className={styles.axisText}>1</text><text x="720" y="314" textAnchor="end" className={styles.axisText}>{family.ranked.length}</text></svg><div className={styles.figureActions}><button className="btn btn-sm btn-outline-secondary" onClick={()=>downloadSvg(svg.current,'gene_set_enrichment.svg')}>SVG</button><button className="btn btn-sm btn-outline-secondary" onClick={()=>downloadSvgPng(svg.current,'gene_set_enrichment.png')}>PNG</button></div></>}
  <button className="btn btn-sm btn-outline-primary" onClick={()=>downloadText(rowsToDelimited(results,['id','name','size','es','nes','pValue','qValue','permutations'].map(k=>({label:k,value:k}))),'ranked_enrichment.csv','text/csv;charset=utf-8')}>Download enrichment results</button></>}</>}
 </section>;
}
