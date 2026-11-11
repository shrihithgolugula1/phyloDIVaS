import React,{useMemo} from 'react';
import AnalysisResults from '../../ToDo/AnalysisResults/AnalysisResults';
import {buildSavedAnalysis} from './demoData';

// Saved demonstrations and connected histories render the exact same dashboard.
export default function GenomeWideDemoResults({snapshot,submitted,geneSets,reportIndex}) {
    const savedResults=useMemo(()=>buildSavedAnalysis(snapshot,submitted,geneSets,reportIndex),[snapshot,submitted,geneSets,reportIndex]);
    const jobInfo=useMemo(()=>({state:'workflow_complete',hyphyJsonOutputs:savedResults.cards.map(c=>c.json),hyphyMarkdownOutputs:[]}),[savedResults]);
    return <section id="genome-snapshot-title" aria-label="Genome-wide selection results"><AnalysisResults jobInfo={jobInfo} analysisGroups={savedResults.groups} genomes={snapshot.species} requestedGeneCount={snapshot.requested} demoMode savedResults={savedResults}/></section>;
}
