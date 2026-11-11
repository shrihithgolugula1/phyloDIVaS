import React from "react";
import { Link } from "react-router-dom";
import { Alert } from "react-bootstrap";
import AnalysisResults from "../../ToDo/AnalysisResults/AnalysisResults";
import batDemo from "../../../data/batDemo.json";

export const demoJobInfo = {
    state: "workflow_complete",
    hyphyJsonOutputs: Object.entries(batDemo.methods).map(([method, output]) => ({
        id: `demo-${method}-json`, method, elementIdentifier: batDemo.gene,
        state: "ok", nativeJson: output.nativeJson
    })),
    hyphyMarkdownOutputs: Object.entries(batDemo.methods).map(([method, output]) => ({
        id: `demo-${method}-markdown`, method, elementIdentifier: batDemo.gene,
        state: "ok", markdownText: output.markdownText
    }))
};

export default function DemoResultsPage() {
    return <main className="container py-4">
        <h1>Explore demo results</h1>
        <Alert variant="info">
            <strong>Saved eight-bat example — one verified orthogroup.</strong>
            <p className="mb-1">Explore real BUSTED, aBSREL, and MEME results for {batDemo.gene}, an alignment of eight sequences and 739 codons. No Galaxy key is required and no analysis is submitted.</p>
            <p className="mb-0">This small teaching sample is not a genome-wide analysis. Its tests did not detect positive selection at the displayed thresholds. Working-file paths and private Galaxy identifiers have been removed; statistical values are unchanged.</p>
        </Alert>
        <p><Link to="/Analysis">Start or resume your own analysis</Link></p>
        <AnalysisResults demoMode jobInfo={demoJobInfo} genomes={batDemo.species} requestedGeneCount={1} />
    </main>;
}
