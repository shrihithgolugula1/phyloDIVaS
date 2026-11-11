import React, {useMemo, useRef, useState} from 'react';
import {downloadSvg, downloadSvgPng, downloadText} from '../../../utils/downloads';
import {rowsToDelimited} from '../../../utils/hyphyDashboard';
import styles from './AnalysisResults.module.css';

export function parseGeneTree(text) {
    const tokens=String(text||'').trim().replace(/;?$/, ';').match(/\(|\)|,|:|;|'(?:[^']|'')*'|"(?:[^"]|"")*"|[^\s(),:;]+/g)||[];
    let cursor=0;
    const node=()=>{
        const result={name:'',length:0,children:[]};
        if(tokens[cursor]==='('){cursor++;result.children.push(node());while(tokens[cursor]===','){cursor++;result.children.push(node());}if(tokens[cursor++]!==')')throw Error('Unbalanced gene tree');}
        if(tokens[cursor]&&![':',',',')',';'].includes(tokens[cursor]))result.name=tokens[cursor++].replace(/^['"]|['"]$/g,'');
        if(tokens[cursor]===':'){cursor++;result.length=Number(tokens[cursor++]);}
        if(!Number.isFinite(result.length)||result.length<0)throw Error('Invalid gene-tree branch length');
        if(!result.children.length&&!result.name)throw Error('Missing gene-tree tip');
        return result;
    };
    const root=node();if(tokens[cursor]!==';'||cursor!==tokens.length-1)throw Error('Invalid gene tree');return root;
}
const key=value=>String(value||'').replace(/\{[^}]*\}/g,'').replace(/[_\.]\d+$/,'').toLowerCase();
const label=(gene,name)=>{
    for(const [species,ids] of Object.entries(gene.speciesGenes||{}))if((Array.isArray(ids)?ids:String(ids).split(',')).some(id=>key(id)===key(name)))return species.replaceAll('_',' ');
    return name;
};
function ExportButtons({svg,filename,rows}) {
    return <div className={styles.figureActions}><button className="btn btn-sm btn-outline-secondary" onClick={()=>downloadSvg(svg.current,filename+'.svg')}>SVG</button><button className="btn btn-sm btn-outline-secondary" onClick={()=>downloadSvgPng(svg.current,filename+'.png')}>PNG</button>{rows&&<button className="btn btn-sm btn-outline-secondary" onClick={()=>downloadText(rowsToDelimited(rows,Object.keys(rows[0]||{}).map(k=>({label:k,value:k}))),filename+'.csv','text/csv;charset=utf-8')}>CSV</button>}</div>;
}
export function MemeManhattan({sites,threshold,geneId}) {
    const svg=useRef(null);const [chosen,setChosen]=useState(null);
    const partitions=[...new Set(sites.map(s=>s.partition))];const [partition,setPartition]=useState(partitions[0]);
    const points=useMemo(()=>sites.filter(s=>s.partition===partition&&s.pValue!=null&&Number.isFinite(s.pValue)&&s.pValue>=0&&s.pValue<=1),[sites,partition]);
    const maximum=Math.max(1,...points.map(s=>Math.min(12,-Math.log10(Math.max(s.pValue,1e-12))))),last=Math.max(1,...points.map(s=>s.site));
    const x=s=>62+(s.site-1)/Math.max(1,last-1)*690,y=p=>250-Math.min(12,-Math.log10(Math.max(p,1e-12)))/maximum*220;
    return <section className={styles.dashboardFigure} aria-label="MEME Manhattan plot">
        <h5>MEME codon selection</h5><p>Every readable codon is plotted. Red points pass raw p ≤ {threshold}; these are not FDR-adjusted site calls.</p>
        <label>Alignment partition <select value={partition} onChange={e=>{setPartition(e.target.value);setChosen(null);}}>{partitions.map(p=><option key={p} value={p}>{p}</option>)}</select></label>
        <svg ref={svg} viewBox="0 0 790 305" className={styles.figureSvg} role="img" aria-label="MEME Manhattan plot by codon position">
            {[0,.25,.5,.75,1].map(f=><g key={f}><line x1="62" x2="752" y1={250-f*220} y2={250-f*220} stroke="#ccd3db"/><text x="52" y={254-f*220} textAnchor="end" className={styles.axisText}>{(f*maximum).toFixed(1)}</text></g>)}
            <line x1="62" x2="752" y1={y(threshold)} y2={y(threshold)} stroke="#a52a2a" strokeDasharray="5 4"/>
            {points.map(s=><circle key={`${s.partition}-${s.site}`} cx={x(s)} cy={y(s.pValue)} r={s.pValue<=threshold?3.4:2} fill={s.pValue<=threshold?'#c52c38':'#718799'} onClick={()=>setChosen(s)}><title>Codon {s.site}: p={s.pValue}; LRT={s.lrt??'unavailable'}</title></circle>)}
            {[1,Math.round(last/2),last].map((n,i)=><text key={i} x={x({site:n})} y="272" textAnchor="middle" className={styles.axisText}>{n}</text>)}
            <text x="405" y="297" textAnchor="middle" className={styles.axisLabel}>Codon position</text><text x="17" y="140" transform="rotate(-90 17 140)" textAnchor="middle" className={styles.axisLabel}>−log10(raw p-value)</text>
        </svg>
        {chosen&&<p role="status">Codon {chosen.site}: p = {chosen.pValue}; {chosen.pValue<=threshold?'selection evidence detected':'threshold not reached'}.</p>}
        <p className={styles.figureNote}>Zero p-values use a 1e−12 plotting floor. Exported values are unchanged. {sites.filter(s=>s.partition===partition).length-points.length} unreadable site values excluded from this partition.</p>
        <ExportButtons svg={svg} filename={`${geneId}_MEME_manhattan`} rows={points.map(s=>({codon:s.site,partition:s.partition,p:s.pValue,LRT:s.lrt,selected:s.pValue<=threshold}))}/>
    </section>;
}
export function AbsrelGeneTree({gene,summary,threshold}) {
    const svg=useRef(null);const [zoom,setZoom]=useState(1);
    const layout=useMemo(()=>{
        try {
            const root=parseGeneTree(summary.geneTree);const nodes=[];let leaves=0;
            const walk=(n,parent,distance)=>{const current={...n,parent,distance};nodes.push(current);current.children=n.children.map(c=>walk(c,current,distance+c.length));current.y=current.children.length?(Math.min(...current.children.map(c=>c.y))+Math.max(...current.children.map(c=>c.y)))/2:40+leaves++*48;return current;};
            walk(root,null,0);const maximum=Math.max(...nodes.map(n=>n.distance));
            nodes.forEach(n=>{n.x=30+(maximum?n.distance/maximum:0)*480;});
            return {nodes,maximum,height:leaves*48+80,width:Math.max(880,560+Math.max(...nodes.filter(n=>!n.children.length).map(n=>label(gene,n.name).length))*9)};
        }catch{return null;}
    },[summary.geneTree,gene.speciesGenes]);
    if(!layout)return <p role="status">A readable gene tree is not present in this aBSREL output. Branch statistics are available below.</p>;
    const branches=new Map((summary.absrelBranchResults||[]).filter(b=>b.partition===(summary.geneTreePartition||'0')).map(b=>[key(b.branch),b]));
    return <section className={styles.dashboardFigure} aria-label="aBSREL selected-branch gene tree"><h5>aBSREL gene tree</h5><p>Red, thick branches pass corrected p ≤ {threshold}. Other tested branches are gray; dashed branches lack a mapped test. Branch lengths show substitutions per site.</p>
        <div className={styles.figureActions}><button className="btn btn-sm btn-outline-secondary" disabled={zoom<=.5} onClick={()=>setZoom(Math.max(.5,zoom-.25))}>Zoom out</button><button className="btn btn-sm btn-outline-secondary" disabled={zoom>=3} onClick={()=>setZoom(Math.min(3,zoom+.25))}>Zoom in</button></div>
        <div style={{overflowX:'auto'}} tabIndex="0" aria-label="Scrollable selected-branch gene tree"><svg ref={svg} viewBox={`0 0 ${layout.width} ${layout.height}`} style={{width:layout.width*zoom,height:layout.height*zoom}} role="img" aria-label="aBSREL gene tree with selected branches highlighted">
            {layout.nodes.filter(n=>n.children.length).map((n,i)=><line key={'v'+i} x1={n.x} x2={n.x} y1={Math.min(...n.children.map(c=>c.y))} y2={Math.max(...n.children.map(c=>c.y))} stroke="#8997a5"/>)}
            {layout.nodes.filter(n=>n.parent).map((n,i)=>{const b=branches.get(key(n.name)),selected=b?.tested&&b.correctedPValue!=null&&b.correctedPValue<=threshold;return <line key={'h'+i} x1={n.parent.x} x2={n.x} y1={n.y} y2={n.y} stroke={selected?'#dc243b':'#8997a5'} strokeWidth={selected?4:1.5} strokeDasharray={b?.tested?undefined:'4 3'}><title>{label(gene,n.name)||n.name}: corrected p={b?.correctedPValue??'unmapped'}; baseline ω={b?.baselineOmega??'unavailable'}</title></line>;})}
            {layout.nodes.filter(n=>!n.children.length).map(n=><g key={n.name}><line x1={n.x} x2="525" y1={n.y} y2={n.y} stroke="#d3dbe1" strokeDasharray="2 3"/><text x="537" y={n.y+5} className={styles.axisLabel}>{label(gene,n.name)}</text></g>)}
            <line x1="30" x2="126" y1={layout.height-35} y2={layout.height-35} stroke="#54616f"/><text x="30" y={layout.height-14} className={styles.axisText}>{(layout.maximum/5).toPrecision(3)} substitutions/site</text>
        </svg></div><ExportButtons svg={svg} filename={`${gene.orthogroupId}_aBSREL_tree`}/>
    </section>;
}
