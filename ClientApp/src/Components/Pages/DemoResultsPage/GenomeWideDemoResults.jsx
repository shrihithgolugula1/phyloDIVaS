import React, {useMemo,useState,useEffect} from 'react';
import {downloadJson,downloadText} from '../../../utils/downloads';
import {BustedHistogram} from '../../ToDo/AnalysisResults/HyphyDashboardFigures';
import {DEFAULT_THRESHOLDS,rowsToDelimited} from '../../../utils/hyphyDashboard';
import {readDemoAsset} from './demoData';
import DemoNativeReports from './DemoNativeReports';
import styles from './DemoResultsPage.module.css';
const fmt=value=>value==null?'Unavailable':Number(value).toPrecision(3);
const complete=gene=>['busted_status','absrel_status','meme_status'].every(key=>gene[key]==='valid');

export default function GenomeWideDemoResults({snapshot,examples}) {
    const [query,setQuery]=useState('');const [filter,setFilter]=useState('all');const [sort,setSort]=useState('p');const [reverse,setReverse]=useState(false);const [page,setPage]=useState(1);const [selected,setSelected]=useState(null);
    const [native,setNative]=useState(null);const [nativeError,setNativeError]=useState('');const [nativeLoading,setNativeLoading]=useState(false);
    const counts=useMemo(()=>({allThree:snapshot.genes.filter(complete).length,busted:snapshot.genes.filter(g=>g.BUSTED_q_BH!=null&&g.BUSTED_q_BH<=.05).length,meme:snapshot.genes.filter(g=>g.meme_status==='valid'&&g.MEME_sites_raw01>0).length,absrel:snapshot.genes.filter(g=>g.absrel_status==='valid'&&g.aBSREL_branches_within_gene005>0).length}),[snapshot]);
    const genes=useMemo(()=>snapshot.genes.filter(g=>{
        const matches=[g.orthogroup,g.consensus_bat_symbol,g.protein_name].join(' ').toLowerCase().includes(query.toLowerCase().trim());
        return matches&&(filter==='all'||filter==='complete'&&complete(g)||filter==='busted'&&g.BUSTED_q_BH!=null&&g.BUSTED_q_BH<=.05||filter==='meme'&&g.meme_status==='valid'&&g.MEME_sites_raw01>0||filter==='absrel'&&g.absrel_status==='valid'&&g.aBSREL_branches_within_gene005>0);
    }).sort((a,b)=>{
        const field={name:'consensus_bat_symbol',p:'BUSTED_p',q:'BUSTED_q_BH',sites:'MEME_sites_raw01',branches:'aBSREL_branches_within_gene005'}[sort];
        if(a[field]==null)return b[field]==null?0:1;if(b[field]==null)return -1;
        const result=typeof a[field]==='string'?a[field].localeCompare(b[field]):a[field]-b[field];return reverse?-result:result;
    }),[snapshot,query,filter,sort,reverse]);
    const histogram=useMemo(()=>snapshot.genes.map(g=>({id:g.orthogroup,displayName:g.consensus_bat_symbol||g.orthogroup,bustedPValue:g.BUSTED_p,bustedQValue:g.BUSTED_q_BH})),[snapshot]);
    const selectedExample=examples.find(item=>item.gene===selected?.orthogroup);
    useEffect(()=>{
        let active=true;setNative(null);setNativeError('');setNativeLoading(Boolean(selectedExample));
        if(selectedExample)readDemoAsset(selectedExample.url).then(data=>{if(active)setNative(data);}).catch(error=>{if(active)setNativeError(error.message);}).finally(()=>{if(active)setNativeLoading(false);});
        return()=>{active=false;};
    },[selectedExample]);
    const last=Math.max(1,Math.ceil(genes.length/25));const current=Math.min(page,last);
    const exportRows=()=>downloadText(rowsToDelimited(snapshot.genes.map(g=>({'Orthogroup':g.orthogroup,'Gene':g.consensus_bat_symbol,'Full name':g.protein_name,'BUSTED state':g.busted_status,'BUSTED p':g.BUSTED_p,'BUSTED BH q':g.BUSTED_q_BH,'aBSREL state':g.absrel_status,'aBSREL selected branches':g.aBSREL_branches_within_gene005,'MEME state':g.meme_status,'MEME selected sites':g.MEME_sites_raw01}))), 'phylodivas_bat_genome_snapshot.csv','text/csv;charset=utf-8');
    return <section className={styles.section} aria-labelledby="genome-snapshot-title">
        <h2 id="genome-snapshot-title">Genome-wide selection results</h2>
        <p><strong>Frozen partial snapshot, {new Date(snapshot.savedAt).toLocaleString()}.</strong> This view includes every readable saved result from one historical invocation. Later results and duplicate invocations are not pooled. Missing, queued, paused and failed tests are not negative findings.</p>
        <div className={styles.stats}>
            <div><strong>{snapshot.requested.toLocaleString()}</strong><span>historically submitted genes</span></div>
            <div><strong>{snapshot.genes.length.toLocaleString()}</strong><span>genes with ≥1 completed method</span></div>
            <div><strong>{counts.allThree.toLocaleString()}</strong><span>genes with all three completed</span></div>
            <div><strong>{counts.busted}</strong><span>BUSTED genes at snapshot BH q ≤ 0.05</span></div>
            <div><strong>{counts.absrel}</strong><span>genes with aBSREL selected branches</span></div>
            <div><strong>{counts.meme}</strong><span>genes with MEME selected sites</span></div>
        </div>
        <div className={styles.tableViewport}><table className="table table-sm" aria-label="Saved HyPhy snapshot coverage"><thead><tr><th>Method</th><th>Completed</th><th>Not started</th><th>Running</th><th>Paused</th><th>Failed</th></tr></thead><tbody>{Object.entries(snapshot.methods).map(([method,states])=><tr key={method}><th>{method==='absrel'?'aBSREL':method.toUpperCase()}</th>{['ok','new','running','paused','error'].map(state=><td key={state}>{(states[state]||0).toLocaleString()}</td>)}</tr>)}</tbody></table></div>
        <p>BUSTED BH correction uses all {snapshot.methods.busted.ok.toLocaleString()} readable BUSTED tests in this snapshot, not just the displayed search subset. aBSREL uses corrected branch p ≤ 0.05; MEME uses raw site p ≤ 0.1. No GO enrichment is inferred without an appropriate tested-gene background.</p>
        <div className={styles.actions}><button className="btn btn-primary" onClick={exportRows}>Download genome-wide results table</button><a className="btn btn-outline-primary" href="/demo/bats/genome-summary.json" download>Download saved summary (JSON)</a></div>
        <BustedHistogram genes={histogram} thresholds={DEFAULT_THRESHOLDS} onOpenGene={gene=>{setQuery(gene.id);setPage(1);}}/>
        <div className={styles.methodBars} aria-label="Method-specific selection findings">{[['BUSTED',counts.busted,snapshot.methods.busted.ok,'busted'],['aBSREL',counts.absrel,snapshot.methods.absrel.ok,'absrel'],['MEME',counts.meme,snapshot.methods.meme.ok,'meme']].map(([name,n,total,key])=><button key={name} onClick={()=>{setFilter(key);setPage(1);}}><strong>{name}: {n.toLocaleString()} / {total.toLocaleString()}</strong><span>{name==='BUSTED'?'genes passing BH correction':'readable genes with detected evidence'}</span><progress value={n} max={total} aria-label={`${name} genes with detected selection evidence`}/></button>)}</div>
        <h3>Search the gene results</h3>
        <div className={styles.actions}>
            <label>Gene name or orthogroup <input type="search" value={query} onChange={e=>{setQuery(e.target.value);setPage(1);}}/></label>
            <label>Show <select value={filter} onChange={e=>{setFilter(e.target.value);setPage(1);}}><option value="all">All available genes</option><option value="complete">All three methods completed</option><option value="busted">BUSTED BH evidence</option><option value="absrel">aBSREL evidence</option><option value="meme">MEME evidence</option></select></label>
            <label>Sort <select value={sort} onChange={e=>{setSort(e.target.value);setPage(1);}}><option value="p">BUSTED p-value</option><option value="q">BUSTED BH q-value</option><option value="name">Gene name</option><option value="branches">Selected branches</option><option value="sites">Selected sites</option></select></label>
            <button className="btn btn-outline-secondary" aria-label={reverse?'Sort ascending':'Sort descending'} onClick={()=>setReverse(v=>!v)}>{reverse?'▼':'▲'}</button>
        </div>
        <p role="status">{genes.length.toLocaleString()} matching genes · 25 per page</p>
        <div className={styles.tableViewport}><table className="table table-striped table-sm" aria-label="Genome-wide gene results"><thead><tr><th>Gene / orthogroup</th><th>Full name</th><th>Methods completed</th><th>BUSTED p / BH q</th><th>aBSREL branches</th><th>MEME sites</th><th>Details</th></tr></thead><tbody>{genes.slice((current-1)*25,current*25).map(g=><tr key={g.orthogroup}><th>{g.consensus_bat_symbol||g.orthogroup}<small className="d-block">{g.orthogroup}</small></th><td>{g.protein_name||'Annotation unavailable'}</td><td>{['busted_status','absrel_status','meme_status'].filter(k=>g[k]==='valid').length}/3</td><td>{fmt(g.BUSTED_p)} / {fmt(g.BUSTED_q_BH)}</td><td>{g.absrel_status==='valid'?g.aBSREL_branches_within_gene005:'Unavailable'}</td><td>{g.meme_status==='valid'?g.MEME_sites_raw01:'Unavailable'}</td><td><button className="btn btn-sm btn-outline-success" onClick={()=>setSelected(g)}>Inspect {g.consensus_bat_symbol||g.orthogroup}</button></td></tr>)}</tbody></table></div>
        <div className={styles.actions}><button className="btn btn-outline-secondary" disabled={current<=1} onClick={()=>setPage(current-1)}>Previous genes</button><span>Page {current} of {last}</span><button className="btn btn-outline-secondary" disabled={current>=last} onClick={()=>setPage(current+1)}>Next genes</button></div>
        <h3>Native three-tool report examples</h3><p>The genome-wide table includes every available gene summary. Full native JSON is bundled for these three representative genes so the demo stays lightweight; other rows still show their saved statistics.</p>
        <div className={styles.actions}>{examples.map(example=><button key={example.gene} className="btn btn-outline-success" onClick={()=>setSelected(snapshot.genes.find(g=>g.orthogroup===example.gene))}>Open {example.symbol||example.gene} reports</button>)}</div>
        {selected&&<aside className={styles.inspector} aria-label="Selected gene summary"><h3>{selected.consensus_bat_symbol||selected.orthogroup}</h3><p>{selected.protein_name} · {selected.orthogroup}</p><p>BUSTED p: {fmt(selected.BUSTED_p)}; snapshot BH q: {fmt(selected.BUSTED_q_BH)}. aBSREL: {selected.absrel_status==='valid'?`${selected.aBSREL_branches_within_gene005} selected branches`:'result unavailable'}. MEME: {selected.meme_status==='valid'?`${selected.MEME_sites_raw01} selected sites`:'result unavailable'}.</p><p>Annotation support: {selected.mapping_confidence}. Statistical evidence alone does not establish adaptation or validate alignment quality.</p><button className="btn btn-outline-primary" onClick={()=>downloadJson(selected,`${selected.orthogroup}_saved_summary.json`)}>Download selected gene summary</button>{!selectedExample&&<p className="mt-2">Full native JSON for this row is not packaged in the demo. Choose one of the three report examples above for the interactive tool panels.</p>}{nativeLoading&&<p role="status">Loading saved native reports…</p>}{nativeError&&<p role="alert">{nativeError}</p>}{native&&<DemoNativeReports example={native} species={snapshot.species}/>}</aside>}
    </section>;
}
