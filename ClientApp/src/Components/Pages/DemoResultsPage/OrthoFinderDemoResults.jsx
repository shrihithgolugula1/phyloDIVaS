import React, {useMemo, useState} from 'react';
import {parseTree, layoutTree} from './demoData';
import styles from './DemoResultsPage.module.css';

export default function OrthoFinderDemoResults({newick, icons, submitted, strict}) {
    const [zoom, setZoom] = useState(1);
    const [pictures, setPictures] = useState(true);
    const [lengths, setLengths] = useState(true);
    const [table, setTable] = useState('strict');
    const [query, setQuery] = useState('');
    const [page, setPage] = useState(1);
    const tree = useMemo(() => layoutTree(parseTree(newick)), [newick]);
    const active = table === 'strict' ? strict : submitted;
    const matches = useMemo(() => active.rows.filter(row => row.join(' ').toLowerCase().includes(query.trim().toLowerCase())), [active, query]);
    const lastPage = Math.max(1, Math.ceil(matches.length / 25));
    const current = Math.min(page, lastPage);
    return <section className={styles.section} aria-labelledby="demo-orthofinder-title">
        <h2 id="demo-orthofinder-title">OrthoFinder results</h2>
        <div className={styles.stats}>
            <div><strong>{tree.nodes.filter(node => !node.children.length).length}</strong><span>species in the exported tree</span></div>
            <div><strong>{strict.rows.length.toLocaleString()}</strong><span>strict root N0 orthogroups</span></div>
            <div><strong>{submitted.rows.length.toLocaleString()}</strong><span>groups submitted in the historical run</span></div>
        </div>
        <p>The historical submission and today's strict N0 filter are different sets. They are preserved separately; the demo does not imply that every historically submitted group passes the current filter.</p>
        <div className={styles.actions}>
            <a className="btn btn-outline-primary" href="/demo/bats/species-tree.nwk" download>Download species tree (Newick)</a>
            <a className="btn btn-outline-primary" href="/demo/bats/raw-N0.tsv" download>Download raw N0 table</a>
            <a className="btn btn-outline-primary" href="/demo/bats/strict-N0.tsv" download>Download strict 1:1 table</a>
            <a className="btn btn-outline-primary" href="/demo/bats/orthogroups.tsv" download>Download submitted orthogroups</a>
        </div>
        <h3>Species phylogeny</h3>
        <p>Original OrthoFinder topology and branch lengths, scaled in substitutions per site. This is an inferred tree, not a dated TimeTree.</p>
        <div className={styles.actions}>
            <button className="btn btn-outline-secondary" disabled={zoom <= .5} onClick={() => setZoom(z => Math.max(.5,z-.25))} aria-label="Zoom out of species tree">−</button>
            <button className="btn btn-outline-secondary" disabled={zoom >= 2.5} onClick={() => setZoom(z => Math.min(2.5,z+.25))} aria-label="Zoom into species tree">+</button>
            <button className="btn btn-outline-secondary" onClick={() => setZoom(1)}>Reset tree zoom</button>
            <label><input type="checkbox" checked={pictures} onChange={event => setPictures(event.target.checked)}/> Species pictures</label>
            <label><input type="checkbox" checked={lengths} onChange={event => setLengths(event.target.checked)}/> Branch lengths</label>
        </div>
        <div className={styles.treeViewport} tabIndex="0" aria-label="Scrollable OrthoFinder phylogeny">
            <svg role="img" aria-label="OrthoFinder species phylogeny with eight bat pictures and branch lengths to scale" viewBox={`0 0 870 ${tree.height}`} style={{width:870*zoom,height:tree.height*zoom}}>
                {tree.nodes.filter(node => node.children.length).map((node,index) => <line key={`v${index}`} x1={node.x} x2={node.x} y1={Math.min(...node.children.map(c=>c.y))} y2={Math.max(...node.children.map(c=>c.y))} className={styles.branch}/>)}
                {tree.nodes.filter(node => node.parent).map((node,index) => <g key={`h${index}`}><title>{`${node.name || 'Internal branch'}: ${node.length} substitutions/site`}</title><line x1={node.parent.x} x2={node.x} y1={node.y} y2={node.y} className={styles.branch}/>{lengths && node.x-node.parent.x>52 && <text x={(node.parent.x+node.x)/2} y={node.y-8} textAnchor="middle" className={styles.branchLabel}>{node.length.toPrecision(3)}</text>}</g>)}
                {tree.nodes.filter(node => !node.children.length).map(node => {
                    const name=node.name.replaceAll('_',' ');const icon=icons[name];
                    return <g key={node.name}><line x1={node.x} x2="530" y1={node.y} y2={node.y} className={styles.guide}/>{pictures && icon && <image href={icon.url} x="537" y={node.y-19} width="42" height="38"><title>{`${icon.taxon}${icon.fallback ? ' (related-taxon silhouette)' : ''}`}</title></image>}<text x={pictures?590:540} y={node.y+5} className={styles.speciesLabel}>{name}</text></g>;
                })}
                <g transform={`translate(30,${tree.height-30})`}><line x1="0" x2="96" y1="0" y2="0" className={styles.branch}/><text x="0" y="20" className={styles.branchLabel}>{(tree.maximum/5).toPrecision(3)} substitutions/site</text></g>
            </svg>
        </div>
        <details><summary>Species picture credits</summary><ul>{Object.entries(icons).map(([name,icon])=>icon && <li key={name}>{name}: {icon.credit} — <a href={icon.source} target="_blank" rel="noreferrer">PhyloPic</a>, <a href={icon.license} target="_blank" rel="noreferrer">license</a>{icon.fallback ? `; ${icon.taxon} fallback` : ''}</li>)}</ul></details>
        <h3>Orthogroup membership</h3>
        <p>One protein accession per species in each displayed row. Submitted-table species headings were corrected against the saved original protein metadata; accession values were unchanged.</p>
        <div className={styles.actions}>
            <label>Table <select aria-label="Orthogroup table" value={table} onChange={e=>{setTable(e.target.value);setPage(1);}}><option value="strict">Strict root N0 groups</option><option value="submitted">Historical submitted groups</option></select></label>
            <label>Find orthogroup or accession <input type="search" value={query} onChange={e=>{setQuery(e.target.value);setPage(1);}}/></label>
        </div>
        <p role="status">{matches.length.toLocaleString()} matching orthogroups · showing 25 per page</p>
        <div className={styles.tableViewport}><table className="table table-striped table-sm" aria-label="OrthoFinder orthogroup membership"><thead><tr><th>Orthogroup</th>{active.species.map(s=><th key={s}>{s}</th>)}</tr></thead><tbody>{matches.slice((current-1)*25,current*25).map(row=><tr key={row[0]}><th scope="row">{row[0]}</th>{row.slice(3).map((id,i)=><td key={i}>{id}</td>)}</tr>)}</tbody></table></div>
        <div className={styles.actions}><button className="btn btn-outline-secondary" disabled={current<=1} onClick={()=>setPage(current-1)}>Previous orthogroups</button><span>Page {current} of {lastPage}</span><button className="btn btn-outline-secondary" disabled={current>=lastPage} onClick={()=>setPage(current+1)}>Next orthogroups</button></div>
    </section>;
}
