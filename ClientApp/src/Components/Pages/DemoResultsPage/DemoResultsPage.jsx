import React, {useEffect,useState} from 'react';
import {Link} from 'react-router-dom';
import OrthoFinderDemoResults from './OrthoFinderDemoResults';
import GenomeWideDemoResults from './GenomeWideDemoResults';
import {readDemoAsset,parseMembership} from './demoData';
import styles from './DemoResultsPage.module.css';

export default function DemoResultsPage() {
    const [data,setData]=useState(null);const [error,setError]=useState('');const [attempt,setAttempt]=useState(0);
    useEffect(()=>{
        let active=true;setData(null);setError('');
        Promise.all([readDemoAsset('/demo/bats/genome-summary.json'),readDemoAsset('/demo/bats/species-tree.nwk',true),readDemoAsset('/demo/bats/silhouettes.json'),readDemoAsset('/demo/bats/orthogroups.tsv',true),readDemoAsset('/demo/bats/strict-N0.tsv',true),readDemoAsset('/demo/bats/examples.json')])
            .then(([snapshot,newick,icons,submitted,strict,examples])=>{
                const membership=parseMembership(submitted);const eligible=parseMembership(strict);
                if(membership.rows.length!==snapshot.requested||Object.values(snapshot.methods).some(states=>Object.values(states).reduce((a,b)=>a+b,0)!==snapshot.requested))throw new Error('Saved demo collection counts do not reconcile.');
                if(active)setData({snapshot,newick,icons,submitted:membership,strict:eligible,examples});
            }).catch(e=>{if(active)setError(e.message);});
        return()=>{active=false;};
    },[attempt]);
    return <main className={styles.page}>
        <h1>Explore the bat analysis demo</h1>
        <p className={styles.intro}>OrthoFinder outputs and a genome-wide HyPhy snapshot from an eight-species bat analysis. Saved results load locally without a Galaxy key or submitting jobs.</p>
        <div className={styles.actions}><Link className="btn btn-outline-success" to="/Analysis">Start or resume your own analysis</Link>{data&&<><a href="#demo-orthofinder-title">OrthoFinder results</a><a href="#genome-snapshot-title">Genome-wide selection results</a></>}</div>
        {!data&&!error&&<p role="status">Loading saved OrthoFinder and genome-wide results…</p>}
        {error&&<div role="alert"><p>{error}</p><button className="btn btn-outline-primary" onClick={()=>setAttempt(n=>n+1)}>Retry demo loading</button></div>}
        {data&&<>
            <OrthoFinderDemoResults {...data}/>
            <GenomeWideDemoResults snapshot={data.snapshot} examples={data.examples}/>
        </>}
    </main>;
}
