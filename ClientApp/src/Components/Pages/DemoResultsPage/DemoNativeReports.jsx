import React, {useMemo, useState} from 'react';
import {MethodResultPanel} from '../../ToDo/AnalysisResults/AnalysisResults';
import {summarizeHyphyResult} from '../../../utils/hyphyResults';
import {buildNormalizedGeneResults, DEFAULT_THRESHOLDS} from '../../../utils/hyphyDashboard';

export default function DemoNativeReports({example, species}) {
    const [tab,setTab]=useState('BUSTED');
    const gene=useMemo(()=>{
        const group={groupId:example.gene,groupName:example.fullName,symbol:example.symbol,speciesGenes:example.speciesGenes};
        const cards=Object.entries(example.methods).map(([method,entry])=>({method,elementIdentifier:example.gene,json:{id:`saved-${example.gene}-${method}`,method,elementIdentifier:example.gene,state:'ok',nativeJson:entry.nativeJson},summary:summarizeHyphyResult(entry.nativeJson,method)}));
        return {...buildNormalizedGeneResults(cards,[group],{expectedSpeciesCount:species.length})[0],bustedQValue:example.bustedQ};
    },[example,species]);
    const markdown=Object.entries(example.methods).map(([method,entry])=>({id:`saved-md-${method}`,method,elementIdentifier:example.gene,markdownText:entry.markdownText}));
    return <section aria-label={`Native reports for ${example.gene}`}>
        <h3>{example.symbol || example.gene}: saved native reports</h3>
        <p>These are the native JSON outputs from the same saved snapshot. Markdown downloads are teaching summaries generated from those JSON files, not the original Galaxy Markdown. BUSTED q-values retain the full snapshot's {example.correctionFamilySize?.toLocaleString() || 'saved'}-test correction family.</p>
        <div role="group" aria-label="Choose a native report" className="d-flex flex-wrap gap-2 mb-3">{['BUSTED','aBSREL','MEME'].map(method=><button key={method} className={`btn ${tab===method?'btn-success':'btn-outline-success'}`} aria-pressed={tab===method} onClick={()=>setTab(method)}>{method}</button>)}</div>
        <MethodResultPanel key={`${example.gene}-${tab}`} gene={gene} method={tab} thresholds={DEFAULT_THRESHOLDS} markdownOutputs={markdown} artifacts={[]} galaxyApiKey={null}/>
    </section>;
}
