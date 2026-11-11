import PropTypes from "prop-types";
import ResultsWorkspace from "./ResultsWorkspace";
import {MemeManhattan, AbsrelGeneTree} from "./GeneResultFigures";
import RankedEnrichment from "./RankedEnrichment";
import MarkdownReport from "./MarkdownReport";
import { readResultSummary, cacheResultSummary, clearResultCaches } from "../../../utils/hyphyResultCache";
import React, { useEffect, useLayoutEffect, useMemo, useState } from "react";
import { Alert, Button, Form, Spinner, Table } from "react-bootstrap";
import "bootstrap/dist/css/bootstrap.min.css";
import {
    downloadOrthoFinderOutput,
    downloadOrthoFinderOutputFile,
    downloadOrthoFinderOutputZip,
    getAnalysisResultArtifacts,
    getOrthogroupGenes,
    getOrthogroupAnnotations
} from "../../../api/dndsApi";
import { downloadJson, downloadText } from "../../../utils/downloads";
import {
    buildLineageSelectionMatrix,
    buildNormalizedGeneResults,
    calculateAnalysisCompletion,
    reconcileHyphyCollections,
    calculateGoEnrichment,
    DEFAULT_THRESHOLDS,
    evaluateGeneEvidence,
    GENE_EXPORT_COLUMNS,
    interpretGene,
    METHODS,
    normalizedGeneRows,
    normalizeIdentifier,
    rowsToDelimited
} from "../../../utils/hyphyDashboard";
import { summarizeHyphyResult } from "../../../utils/hyphyResults";
import {
    BustedHistogram,
    RankedSelectionPlot,
    EvidenceScatterplot,
    GoEnrichmentPlot,
    LineageHeatmap,
    MethodOverlapPlot
} from "./HyphyDashboardFigures";
import styles from "./AnalysisResults.module.css";

const RESULT_PAGE_SIZES = [25, 50, 100];
const NATIVE_LOAD_CONCURRENCY = 6;
const NAMED_SETS_KEY = "phylodivas-results-gene-sets-v1";
const summaryCache = new Map();
const rawJsonCache = new Map();
const markdownCache = new Map();

const formatNumber = (value, digits = 3) => {
    if (value === null || value === undefined || value === "") return "N/A";
    const number = Number(value);
    if (!Number.isFinite(number)) return "N/A";
    if (number === 0) return "0";
    return Math.abs(number) < 0.001 ? number.toExponential(2) : number.toPrecision(digits);
};

const formatPercent = (value) => Number.isFinite(value) ? `${value>=1?100:Math.min(99.9,Math.round(value*1000)/10)}%` : "N/A";

const safeDownloadName = (value) => String(value || "result")
    .replace(/[^a-z0-9._-]+/gi, "_")
    .replace(/^_+|_+$/g, "");

const resultMethod = (entry) => {
    const raw = String(entry?.method || entry?.name || entry?.datasetName || "").toLowerCase();
    if (raw.includes("absrel")) return "aBSREL";
    if (raw.includes("meme")) return "MEME";
    return "BUSTED";
};

const resultIdentifier = (entry) => String(
    entry?.elementIdentifier || entry?.element_identifier || entry?.name || entry?.datasetName || entry?.id || "Unknown orthogroup"
).replace(/^(BUSTED|aBSREL|MEME)\s*[-_:]\s*/i, "").replace(/\.(json|md|markdown)$/i, "");

const canonicalOrthogroupIdentifier = (value) => {
    const match = String(value || "").match(/N\d+\.HOG\d+/i);
    return match ? match[0].toUpperCase() : String(value || "").trim();
};

const isCanonicalOrthogroupIdentifier = (value) => /^N\d+\.HOG\d+$/i.test(String(value || "").trim());

const orthogroupIdFromNativeJson = (parsed) => {
    const match = JSON.stringify(parsed || {}).match(/N\d+\.HOG\d+/i);
    return match ? match[0].toUpperCase() : null;
};

const outputMatchesGene = (output, gene, method = null) => {
    if (!output || !gene) return false;
    if (method && resultMethod(output) !== method) return false;
    const outputKey = normalizeIdentifier(canonicalOrthogroupIdentifier(resultIdentifier(output)));
    return outputKey === normalizeIdentifier(gene.orthogroupId) || outputKey === gene.id;
};

const artifactMatchesGene = (artifact, gene, kind) => artifact?.kind === kind &&
    [artifact.elementIdentifier, artifact.name].some(value => {
        const match = String(value || "").match(/\bN\d+\.HOG\d+\b/i);
        return match && normalizeIdentifier(match[0]) === gene.id;
    });

const redactSensitiveValues = (value) => {
    if (Array.isArray(value)) return value.map(redactSensitiveValues);
    if (!value || typeof value !== "object") return value;
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [
        key,
        /api.?key|authorization|access.?token/i.test(key) ? "[redacted]" : redactSensitiveValues(item)
    ]));
};

const mapWithConcurrency = async (items, limit, mapper, onProgress) => {
    const results = new Array(items.length);
    let cursor = 0;
    let completed = 0;
    const worker = async () => {
        while (cursor < items.length) {
            const index = cursor;
            cursor += 1;
            results[index] = await mapper(items[index], index);
            completed += 1;
            onProgress?.(completed, items.length, results);
        }
    };
    await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
    return results;
};

const loadNativeSummary = async (output, galaxyApiKey) => {
    if (output?.nativeJson) return {
        summary: summarizeHyphyResult(output.nativeJson, resultMethod(output)),
        provenance: { analysis: output.nativeJson.analysis, input: output.nativeJson.input }
    };
    if (output?.summary) {
        return {
            summary: output.summary,
            provenance: output.provenance || null
        };
    }
    const cacheKey = `${output.id}:${resultMethod(output)}`;
    if (summaryCache.has(cacheKey)) return summaryCache.get(cacheKey);
    const promise = (async () => {
        const cached = await readResultSummary(cacheKey);
        if (cached?.summary?.resultAvailable) return cached;
        const text = await downloadOrthoFinderOutput(output.id, { galaxyApiKey });
        const parsed = JSON.parse(text || "{}");
        if (!parsed || Array.isArray(parsed) || typeof parsed !== "object" || !parsed.input || !parsed.analysis) throw new Error("This dataset is not a native HyPhy result (analysis/input metadata missing).");
        const nativeId = orthogroupIdFromNativeJson(parsed);
        if (nativeId && nativeId !== canonicalOrthogroupIdentifier(resultIdentifier(output))) throw new Error("The native result and collection orthogroup identifiers do not match.");
        const summary = summarizeHyphyResult(parsed, resultMethod(output));
        const result = {
            summary,
            orthogroupId: orthogroupIdFromNativeJson(parsed),
            provenance: {
                analysis: parsed?.analysis || null,
                input: parsed?.input || null,
                timers: parsed?.timers || null
            }
        };
        if (summary?.resultAvailable) await cacheResultSummary(cacheKey, result);
        return result;
    })();
    summaryCache.set(cacheKey, promise);
    try {
        return await promise;
    } catch (error) {
        summaryCache.delete(cacheKey);
        throw error;
    }
};

const loadRawJson = async (output, galaxyApiKey) => {
    if (output?.nativeJson) return output.nativeJson;
    if (!output?.id) return null;
    if (rawJsonCache.has(output.id)) return rawJsonCache.get(output.id);
    const promise = downloadOrthoFinderOutput(output.id, { galaxyApiKey })
        .then((text) => JSON.parse(text || "{}"));
    rawJsonCache.set(output.id, promise);
    try {
        return await promise;
    } catch (error) {
        rawJsonCache.delete(output.id);
        throw error;
    }
};

const loadMarkdownText = async (output, galaxyApiKey) => {
    if (typeof output?.markdownText === "string") return output.markdownText;
    if (!output?.id) return null;
    if (markdownCache.has(output.id)) return markdownCache.get(output.id);
    const promise = downloadOrthoFinderOutput(output.id, { galaxyApiKey });
    markdownCache.set(output.id, promise);
    try {
        return await promise;
    } catch (error) {
        markdownCache.delete(output.id);
        throw error;
    }
};

const branchDisplayName = (gene, branchName) => {
    const normalized = String(branchName || "").replace(/[_.]\d+$/, "").toLowerCase();
    for (const [species, identifiers] of Object.entries(gene?.speciesGenes || {})) {
        const values = Array.isArray(identifiers) ? identifiers : String(identifiers || "").split(",");
        if (values.some((identifier) => String(identifier).trim().replace(/[_.]\d+$/, "").toLowerCase() === normalized)) {
            return `${species.replaceAll("_", " ")} (${branchName})`;
        }
    }
    return branchName;
};

const methodQuestion = {
    BUSTED: "Is there gene-wide evidence that at least one site on at least one tested branch experienced episodic positive selection?",
    aBSREL: "Which tested branches show episodic positive selection after HyPhy's branch-level correction?",
    MEME: "Which codon sites show evidence of episodic positive selection across a subset of branches?"
};

const methodLimit = {
    BUSTED: "BUSTED is a gene-wide test and does not identify the responsible branch or codon.",
    aBSREL: "aBSREL corrected p-values apply to branches within this gene tree; internal nodes are not automatically comparable across genes.",
    MEME: "MEME site p-values are shown at the configured raw threshold and are not presented as an across-site FDR correction."
};

function SelectionBadge({ available, selected, children }) {
    const tone = !available ? styles.callUnavailable : selected ? styles.callPositive : styles.callNegative;
    return <span className={`${styles.selectionBadge} ${tone}`}>{children}</span>;
}

SelectionBadge.propTypes = {
    available: PropTypes.bool.isRequired,
    selected: PropTypes.bool.isRequired,
    children: PropTypes.node.isRequired
};

function MethodResultPanel({ gene, method, thresholds, markdownOutputs, artifacts, galaxyApiKey }) {
    const card = gene.methods[method];
    const summary = card?.summary;
    const evidence = evaluateGeneEvidence(gene, thresholds);
    const [rawJson, setRawJson] = useState(null);
    const [rawError, setRawError] = useState(null);
    const [showRaw, setShowRaw] = useState(false);
    const markdown = markdownOutputs.find((output) => outputMatchesGene(output, gene, method));
    const alignment = artifacts.find((artifact) => artifactMatchesGene(artifact, gene, "codon_alignment"));
    const tree = artifacts.find((artifact) => artifactMatchesGene(artifact, gene, "gene_tree"));
    const available = gene.methodStatus[method] === "complete";
    const selected = method === "BUSTED" ? evidence.bustedFdr : method === "aBSREL" ? evidence.absrel : evidence.meme;
    const fdrPending=method==="BUSTED"&&available&&gene.bustedQValue==null;

    useEffect(() => {
        let cancelled = false;
        setRawJson(null);
        setRawError(null);
        if (!showRaw || !card?.json?.id) return () => { cancelled = true; };
        loadRawJson(card.json, galaxyApiKey)
            .then((value) => { if (!cancelled) setRawJson(value); })
            .catch((error) => { if (!cancelled) setRawError(error); });
        return () => { cancelled = true; };
    }, [showRaw, card?.json, galaxyApiKey]);

    const downloadArtifact = async (item, filename) => {
        if (!item?.id) return;
        if (item.nativeJson) return downloadJson(item.nativeJson, filename);
        if (typeof item.markdownText === "string") return downloadText(item.markdownText, filename);
        await downloadOrthoFinderOutputFile(item.id, filename, galaxyApiKey);
    };

    if (!card) {
        return (
            <Alert variant="secondary" className={styles.methodUnavailable}>
                <Alert.Heading>{method} output is unavailable</Alert.Heading>
                <p className="mb-0">Galaxy status for this gene’s {method} method: {gene.methodStatus[method] || "missing"}. No successful report is available.</p>
            </Alert>
        );
    }

    return (
        <div className={styles.methodWorkspace}>
            <div className={styles.methodIntro}>
                <div>
                    <span className={styles.hyphyLabel}>Native HyPhy {method} result</span>
                    <h5>{methodQuestion[method]}</h5>
                    <p>{methodLimit[method]}</p>
                </div>
                <SelectionBadge available={available&&!fdrPending} selected={selected}>
                    {!available ? "Result unavailable" : fdrPending?"Across-gene FDR pending":selected ? "Selection detected" : "No selection detected"}
                </SelectionBadge>
            </div>

            <div className={`${styles.selectionCallout} ${!available ? styles.callUnavailable : selected ? styles.callPositive : styles.callNegative}`}>
                <strong>{!available?"Result unavailable":fdrPending?"Across-gene correction requires the full BUSTED family":selected?"Evidence detected at the selected threshold":"The selected threshold is not reached"}</strong>
                <p><b>Native tool summary at its default threshold:</b> {summary?.conclusion || card.error?.message || "The Galaxy dataset could not be interpreted."}</p>
                {method === "BUSTED" && available && (
                    <p><b>Across-gene correction:</b> p = {formatNumber(gene.bustedPValue)}; BH q = {formatNumber(gene.bustedQValue)} across all readable BUSTED tests.</p>
                )}
                {method === "aBSREL" && available && (
                    <p><b>Where:</b> {evidence.absrelBranches.length
                        ? evidence.absrelBranches.map((branch) => branchDisplayName(gene, branch.branch)).join(", ")
                        : "No tested branch passes the selected corrected-p threshold."}</p>
                )}
                {method === "MEME" && available && (
                    <p><b>Where:</b> {evidence.memeSites.length
                        ? `Codons ${evidence.memeSites.map((site) => site.site).join(", ")}`
                        : "No codon passes the selected raw-p threshold."}</p>
                )}
            </div>

            <div className={styles.artifactActions} aria-label={`${method} downloads`}>
                <Button size="sm" variant="outline-primary" onClick={() => downloadArtifact(card.json, `${safeDownloadName(gene.displayName)}_${method}.json`)}>{card.json?.compact?"Report data (JSON)":"JSON"}</Button>
                <Button size="sm" variant="outline-primary" disabled={!markdown} onClick={() => downloadArtifact(markdown, `${safeDownloadName(gene.displayName)}_${method}.md`)}>Markdown</Button>
                <Button size="sm" variant="outline-primary" disabled={!alignment} onClick={() => downloadArtifact(alignment, `${safeDownloadName(gene.displayName)}_codon_alignment.fasta`)}>Alignment</Button>
                <Button size="sm" variant="outline-primary" disabled={!tree} onClick={() => downloadArtifact(tree, `${safeDownloadName(gene.displayName)}_gene_tree.nwk`)}>Tree</Button>
                <Button size="sm" variant="outline-secondary" href="https://vision.hyphy.org/" target="_blank" rel="noopener noreferrer">Open HyPhy Vision</Button>
            </div>
            <p className={styles.visionNote}>{card.json?.compact?"Saved report data retain the original values needed for these figures; bulk auxiliary arrays are omitted. The original Galaxy Markdown reports are included below. Use the complete original Galaxy JSON for HyPhy Vision.":"HyPhy Vision cannot receive private Galaxy data automatically. Download this JSON and open it in HyPhy Vision for the complete upstream interactive report."}</p>

            {method === "BUSTED" && summary?.omegaClasses?.length > 0 && (
                <section className={styles.nativeResultSection} aria-labelledby="busted-rates-title">
                    <h6 id="busted-rates-title">Test-branch omega distribution</h6>
                    <div className={styles.omegaBar} aria-label="BUSTED omega category proportions">
                        <span className={styles.omegaPurifying} style={{ width: `${summary.omegaLT1 * 100}%` }}>ω &lt; 1</span>
                        <span className={styles.omegaNeutral} style={{ width: `${summary.omegaEQ1 * 100}%` }}>ω = 1</span>
                        <span className={styles.omegaPositive} style={{ width: `${summary.omegaGT1 * 100}%` }}>ω &gt; 1</span>
                    </div>
                    <div className={styles.nativeTableWrap}>
                        <table className={styles.nativeTable}>
                            <thead><tr><th scope="col">Omega</th><th scope="col">Proportion</th><th scope="col">Category</th></tr></thead>
                            <tbody>{summary.omegaClasses.map((rateClass, index) => (
                                <tr key={`${rateClass.omega}-${index}`}>
                                    <td>{formatNumber(rateClass.omega, 4)}</td>
                                    <td>{formatNumber(rateClass.proportion)}</td>
                                    <td>{rateClass.category}</td>
                                </tr>
                            ))}</tbody>
                        </table>
                    </div>
                </section>
            )}

            {method === "aBSREL" && summary?.absrelBranchResults?.length > 0 && (
                <section className={styles.nativeResultSection} aria-labelledby="absrel-results-title">
                    <h6 id="absrel-results-title">Branch-level results</h6>
                    <AbsrelGeneTree gene={gene} summary={summary} threshold={thresholds.absrelP}/>
                    <div className={styles.nativeTableWrapTall}>
                        <table className={styles.nativeTable}>
                            <thead><tr><th scope="col">Branch</th><th scope="col">Corrected p</th><th scope="col">Uncorrected p</th><th scope="col">LRT</th><th scope="col">Rate classes</th><th scope="col">Call</th></tr></thead>
                            <tbody>{summary.absrelBranchResults.map((branch) => {
                                const significant = branch.tested && Number(branch.correctedPValue) <= thresholds.absrelP;
                                return (
                                    <tr key={`${branch.partition}-${branch.branch}`} className={significant ? styles.significantRow : ""}>
                                        <th scope="row">{branchDisplayName(gene, branch.branch)}</th>
                                        <td>{formatNumber(branch.correctedPValue)}</td>
                                        <td>{formatNumber(branch.uncorrectedPValue)}</td>
                                        <td>{formatNumber(branch.lrt)}</td>
                                        <td>{branch.rateClasses ?? "N/A"}</td>
                                        <td>{!branch.tested ? "Not tested" : significant ? "Selected" : "Not selected"}</td>
                                    </tr>
                                );
                            })}</tbody>
                        </table>
                    </div>
                </section>
            )}

            {method === "MEME" && summary?.memeSiteResults?.length > 0 && (
                <section className={styles.nativeResultSection} aria-labelledby="meme-results-title">
                    <h6 id="meme-results-title">Site-level results</h6>
                    <MemeManhattan sites={summary.memeSiteResults} threshold={thresholds.memeP} geneId={gene.orthogroupId}/>
                    <div className={styles.nativeTableWrapTall}>
                        <table className={styles.nativeTable}>
                            <thead><tr><th scope="col">Codon</th><th scope="col">Raw p-value</th><th scope="col">LRT</th><th scope="col">Beta+</th><th scope="col">Call</th></tr></thead>
                            <tbody>{summary.memeSiteResults.map((site) => {
                                const significant = Number(site.pValue) <= thresholds.memeP;
                                return (
                                    <tr key={`${site.partition}-${site.site}`} className={significant ? styles.significantRow : ""}>
                                        <th scope="row">{site.site}</th>
                                        <td>{formatNumber(site.pValue)}</td>
                                        <td>{formatNumber(site.lrt)}</td>
                                        <td>{formatNumber(site.betaPlus)}</td>
                                        <td>{significant ? "Selected" : "Not selected"}</td>
                                    </tr>
                                );
                            })}</tbody>
                        </table>
                    </div>
                </section>
            )}

            <GeneMarkdownSummaries gene={gene} markdownOutputs={markdownOutputs} galaxyApiKey={galaxyApiKey} onlyMethod={method}/>
            <details className={styles.rawNativeResult} onToggle={(event) => setShowRaw(event.currentTarget.open)}>
                <summary>{card.json?.compact?"Inspect saved report data (JSON)":"Inspect raw native JSON"}</summary>
                {!showRaw && <p>Expand to retrieve this result directly from Galaxy.</p>}
                {showRaw && !rawJson && !rawError && <div className={styles.rawLoading}><Spinner animation="border" size="sm" /> Loading native JSON...</div>}
                {rawError && <Alert variant="warning">Unable to retrieve the raw JSON: {rawError.message}</Alert>}
                {rawJson && <pre>{JSON.stringify(rawJson, null, 2)}</pre>}
            </details>
        </div>
    );
}

function GeneMarkdownSummaries({ gene, markdownOutputs, galaxyApiKey, onlyMethod=null }) {
    const [summaries, setSummaries] = useState({});
    const [error, setError] = useState(null);
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        let cancelled = false;
        setSummaries({});
        setError(null);
        setLoading(true);
        const matches = METHODS.filter(method=>!onlyMethod||method===onlyMethod).map((method) => ({
            method,
            output: markdownOutputs.find((item) => outputMatchesGene(item, gene, method))
        })).filter((item) => item.output);
        if (!matches.length) { setLoading(false); return () => { cancelled = true; }; }
        Promise.allSettled(matches.map(async ({ method, output }) => ({
            method,
            text: await loadMarkdownText(output, galaxyApiKey)
        }))).then((results) => {
            if (cancelled) return;
            setSummaries(Object.fromEntries(results.filter(result => result.status === "fulfilled").map(({ value }) => [value.method, value.text])));
            const failed = results.filter(result => result.status === "rejected");
            if (failed.length) setError(new Error(`${failed.length} tool summary file(s) could not be read. Other available summaries are shown.`));
            setLoading(false);
        }).catch((loadError) => {
            if (!cancelled) { setError(loadError); setLoading(false); }
        });
        return () => { cancelled = true; };
    }, [gene.id, markdownOutputs, galaxyApiKey, onlyMethod]);

    return (
        <section className={styles.geneMarkdownSummaries} aria-labelledby="galaxy-markdown-summary-title">
            <h5 id="galaxy-markdown-summary-title">Native HyPhy summaries from Galaxy</h5>
            <p>These summaries are loaded from the separate Markdown result files for this gene. Use the tool tabs for the interactive JSON outputs.</p>
            {error && <Alert variant="warning">The Galaxy Markdown summaries could not be loaded: {error.message}</Alert>}
            {METHODS.map((method) => summaries[method] ? (
                <details key={method} className={styles.markdownSummary}>
                    <summary>{method} summary</summary>
                    <MarkdownReport text={summaries[method]}/>
                </details>
            ) : null)}
            {loading && <p role="status">Loading tool summaries...</p>}
            {!loading && !error && !Object.keys(summaries).length && <p className={styles.inlineWarning}>No separate Markdown summaries were found for this gene in Galaxy.</p>}
        </section>
    );
}

GeneMarkdownSummaries.propTypes = {
    gene: PropTypes.object.isRequired,
    markdownOutputs: PropTypes.array.isRequired,
    galaxyApiKey: PropTypes.string,
    onlyMethod: PropTypes.string
};

GeneMarkdownSummaries.defaultProps = { galaxyApiKey: null };

MethodResultPanel.propTypes = {
    gene: PropTypes.object.isRequired,
    method: PropTypes.oneOf(METHODS).isRequired,
    thresholds: PropTypes.object.isRequired,
    markdownOutputs: PropTypes.arrayOf(PropTypes.object).isRequired,
    artifacts: PropTypes.arrayOf(PropTypes.object).isRequired,
    galaxyApiKey: PropTypes.string
};

MethodResultPanel.defaultProps = { galaxyApiKey: null };

function AnalysisResults({
    jobInfo,
    statusMessage,
    errorMessage,
    orthologousGroups = [],
    analysisGroups = [],
    genomes = [],
    historyId: historyIdProp = null,
    requestedGeneCount = 0,
    demoMode = false,
    savedResults = null
}) {
    const jobState = jobInfo?.state || (jobInfo ? "unknown" : "idle");
    const historyId = historyIdProp || jobInfo?.historyId || null;
    const galaxyApiKey = jobInfo?.galaxyApiKey;
    useLayoutEffect(() => {
        // Clear before child passive effects can consult raw/markdown caches.
        clearResultCaches(summaryCache, rawJsonCache, markdownCache);
        return () => clearResultCaches(summaryCache, rawJsonCache, markdownCache);
    }, [galaxyApiKey, historyId]);
    const hyphyJsonOutputs = useMemo(() => jobInfo?.hyphyJsonOutputs || [], [jobInfo?.hyphyJsonOutputs]);
    const hyphyMarkdownOutputs = useMemo(() => jobInfo?.hyphyMarkdownOutputs || [], [jobInfo?.hyphyMarkdownOutputs]);
    const analysisSpecies = useMemo(() => {
        const source = genomes.length ? genomes : jobInfo?.genomes || [];
        return [...new Set(source.map((genome) => typeof genome === "string" ? genome : genome?.species).filter(Boolean))];
    }, [genomes, jobInfo?.genomes]);
    const [hyphyCards, setHyphyCards] = useState([]);
    const [detailReport,setDetailReport]=useState(null);
    const [detailError,setDetailError]=useState("");
    const [detailAttempt,setDetailAttempt]=useState(0);
    const [showDiagnostics,setShowDiagnostics]=useState(false);
    const [resultAnnotations, setResultAnnotations] = useState({});
    const [nameLoading, setNameLoading] = useState(false);
    const [loadProgress, setLoadProgress] = useState({ complete: 0, total: 0 });
    const [isPreparingResults, setIsPreparingResults] = useState(true);
    const [resolvedOutputs, setResolvedOutputs] = useState(null);
    const [loadError, setLoadError] = useState(null);
    const [retryResults, setRetryResults] = useState(0);
    const [artifacts, setArtifacts] = useState([]);
    const [artifactError, setArtifactError] = useState(null);
    const [resultView, setResultView] = useState(demoMode ? "genome-wide" : "gene-specific");
    const [loadAllResults, setLoadAllResults] = useState(demoMode);
    const [selectedGeneId, setSelectedGeneId] = useState(null);
    const [geneTab, setGeneTab] = useState("summary");
    const [geneSearch, setGeneSearch] = useState("");
    const [query, setQuery] = useState("");
    const [evidenceFilter, setEvidenceFilter] = useState("all");
    const [statusFilter, setStatusFilter] = useState("all");
    const [sortKey, setSortKey] = useState("gene");
    const [sortDirection, setSortDirection] = useState("asc");
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(50);
    const [thresholds, setThresholds] = useState({ ...DEFAULT_THRESHOLDS });
    const [subsetIds, setSubsetIds] = useState(null);
    const [namedSets, setNamedSets] = useState(() => {
        try { return JSON.parse(window.localStorage.getItem(NAMED_SETS_KEY) || "{}"); } catch { return {}; }
    });
    const [setName, setSetName] = useState("");
    const [selectedNamedSet, setSelectedNamedSet] = useState("");
    const [isBundling, setIsBundling] = useState(false);

    const allGroups = useMemo(() => {
        const byId = new Map();
        [...orthologousGroups, ...analysisGroups].forEach((group) => {
            if (group?.groupId) byId.set(group.groupId, group);
        });
        Object.entries(resultAnnotations).forEach(([id, recovered]) => {
            byId.set(id, { ...(byId.get(id) || { groupId: id }), ...recovered });
        });
        return [...byId.values()];
    }, [orthologousGroups, analysisGroups, resultAnnotations]);

    const orthogroupMap = useMemo(() => {
        const map = new Map();
        allGroups.forEach((group) => {
            [group.groupId, group.groupName].filter(Boolean).forEach((key) => {
                map.set(String(key), group);
                map.set(normalizeIdentifier(key), group);
            });
        });
        return map;
    }, [allGroups]);

    const geneToRetrieve = loadAllResults ? null : selectedGeneId;
    useEffect(() => {
        let cancelled = false;
        let lastProgressUpdate = 0;
        if(savedResults) {
            setHyphyCards(savedResults.cards);setIsPreparingResults(false);setResolvedOutputs(hyphyJsonOutputs);setLoadError(null);setLoadProgress({complete:savedResults.cards.length,total:savedResults.cards.length});
            return()=>{cancelled=true;};
        }
        if (jobInfo?.hyphyOutputsLoading) {
            setIsPreparingResults(true);
            return () => { cancelled = true; };
        }
        const outputs = [];
        const seen = new Set();
        hyphyJsonOutputs.forEach((output) => {
            const identity = output?.id || `${resultMethod(output)}:${resultIdentifier(output)}`;
            if (!identity || seen.has(identity)) return;
            // Never render tool or collection labels as genes. The backend
            // applies the same rule, but keep the UI safe for older histories.
            const orthogroupId = canonicalOrthogroupIdentifier(resultIdentifier(output));
            if (!isCanonicalOrthogroupIdentifier(orthogroupId)) return;
            seen.add(identity);
            outputs.push(output);
        });
        const gene = geneToRetrieve || normalizeIdentifier(resultIdentifier(outputs[0]));
        const requested = loadAllResults ? outputs : outputs.filter(output => normalizeIdentifier(resultIdentifier(output)) === gene);
        setLoadProgress({ complete: 0, total: requested.length });
        setIsPreparingResults(true);
        setHyphyCards([]);
        setLoadError(null);
        if (!outputs.length) {
            setHyphyCards([]);
            setIsPreparingResults(false);
            setResolvedOutputs(outputs.length ? null : hyphyJsonOutputs);
            return () => { cancelled = true; };
        }

        mapWithConcurrency(requested, NATIVE_LOAD_CONCURRENCY, async (output) => {
            if (cancelled) return null;
            const method = resultMethod(output);
            let elementIdentifier = canonicalOrthogroupIdentifier(resultIdentifier(output));
            let summary = null;
            let provenance = null;
            let error = null;
            try {
                const loaded = output.state && output.state !== "ok"
                    ? { summary: null, provenance: null }
                    : await loadNativeSummary(output, galaxyApiKey);
                summary = loaded.summary;
                provenance = loaded.provenance;
                // Galaxy's collection element is the gene identity; a JSON
                // filename or input path must never relabel that element.
            } catch (nativeError) {
                error = nativeError;
            }
            const normalized = normalizeIdentifier(elementIdentifier);
            let geneSet = orthogroupMap.get(elementIdentifier) || orthogroupMap.get(normalized);
            return { method, elementIdentifier, json: output, summary, provenance, error, geneSet };
        }, (complete, total) => {
            if (cancelled) return;
            // Update the lightweight loading screen, not a growing 10,000-gene
            // dashboard. Normalization and BH correction run once per batch.
            const now = Date.now();
            if (complete === total || now - lastProgressUpdate >= 250) {
                lastProgressUpdate = now;
                setLoadProgress({ complete, total });
            }
        }).then((cards) => {
            if (cancelled) return;
            const loaded = new Map(cards.filter(Boolean).map(card => [card.json.id, card]));
            setHyphyCards(outputs.map(output => loaded.get(output.id) || {
                method: resultMethod(output), elementIdentifier: canonicalOrthogroupIdentifier(resultIdentifier(output)),
                json: output, pending: true
            }));
            if (cards.some((card) => card?.error)) setLoadError(`${cards.filter((card) => card?.error).length} native result files could not be read.`);
            setIsPreparingResults(false);
            setResolvedOutputs(hyphyJsonOutputs);
        }).catch(() => {
            if (cancelled) return;
            setLoadError("Results preparation could not finish. Retry loading the results.");
            setIsPreparingResults(false);
            setResolvedOutputs(hyphyJsonOutputs);
        });
        return () => { cancelled = true; };
    }, [hyphyJsonOutputs, galaxyApiKey, retryResults, loadAllResults, geneToRetrieve, jobInfo?.hyphyOutputsLoading, savedResults]);

    useEffect(() => {
        let cancelled = false;
        setArtifacts([]);
        setArtifactError(null);
        if (!historyId || !galaxyApiKey || jobInfo?.hyphyOutputsLoading || !hyphyJsonOutputs.length) return () => { cancelled = true; };
        getAnalysisResultArtifacts(historyId, galaxyApiKey)
            .then((items) => { if (!cancelled) setArtifacts(items); })
            .catch((error) => { if (!cancelled) setArtifactError(error); });
        return () => { cancelled = true; };
    }, [historyId, galaxyApiKey, jobInfo?.hyphyOutputsLoading, hyphyJsonOutputs.length]);

    const collectionInventory = useMemo(() => reconcileHyphyCollections(hyphyJsonOutputs), [hyphyJsonOutputs]);
    const requestedGroupIds = collectionInventory.geneIds;

    const genes = useMemo(() => isPreparingResults ? [] : buildNormalizedGeneResults(hyphyCards, allGroups, {
        expectedSpeciesCount: analysisSpecies.length || null,
        requestedGroupIds,
        loading: !loadAllResults
    }), [hyphyCards, allGroups, analysisSpecies.length, requestedGroupIds, isPreparingResults, loadAllResults]);

    useEffect(() => {
        if (!genes.length) return;
        if (!selectedGeneId || !genes.some((gene) => gene.id === selectedGeneId)) {
            setSelectedGeneId((genes.find((gene) => gene.analysisStatus === "complete") || genes[0]).id);
        }
    }, [genes, selectedGeneId]);

    const selectedGeneBase = genes.find((gene) => gene.id === selectedGeneId) || null;
    useEffect(()=>{
        let active=true;setDetailReport(null);setDetailError("");
        if(!savedResults||resultView!=="gene-specific"||!selectedGeneBase)return()=>{active=false;};
        savedResults.loadReport(selectedGeneBase.orthogroupId).then(value=>{if(active)setDetailReport(value);}).catch(error=>{if(active)setDetailError(error.message);});
        return()=>{active=false;};
    },[savedResults,selectedGeneBase?.id,resultView,detailAttempt]);
    const selectedGene=useMemo(()=>{
        if(!selectedGeneBase||!savedResults||detailReport?.gene!==selectedGeneBase.orthogroupId)return selectedGeneBase;
        const cards=Object.entries(detailReport.methods).map(([method,entry])=>({method,elementIdentifier:detailReport.gene,json:{id:`saved-report-${detailReport.gene}-${method}`,method,elementIdentifier:detailReport.gene,state:'ok',nativeJson:entry.nativeJson,compact:true},summary:summarizeHyphyResult(entry.nativeJson,method)}));
        const updated=buildNormalizedGeneResults(cards,[selectedGeneBase.group],{expectedSpeciesCount:analysisSpecies.length,requestedGroupIds:[selectedGeneBase.orthogroupId]})[0];
        return {...selectedGeneBase,...updated,bustedQValue:selectedGeneBase.bustedQValue,analysisStatus:selectedGeneBase.analysisStatus,warnings:selectedGeneBase.warnings,methodStatus:{...selectedGeneBase.methodStatus,...Object.fromEntries(cards.map(c=>[c.method,'complete']))}};
    },[selectedGeneBase,savedResults,detailReport,analysisSpecies.length]);
    const selectedMarkdownOutputs=useMemo(()=>savedResults&&detailReport&&selectedGeneBase&&detailReport.gene===selectedGeneBase.orthogroupId?Object.entries(detailReport.methods).map(([method,entry])=>({id:`saved-md-${detailReport.gene}-${method}`,method,elementIdentifier:detailReport.gene,markdownText:entry.markdownText})):hyphyMarkdownOutputs,[savedResults,detailReport,selectedGeneBase?.orthogroupId,hyphyMarkdownOutputs]);

    const nameAccessions = (selectedGene?.accessions || []).flatMap((value) =>
        String(value).match(/(?:[A-Z]{1,4}_?\d+)(?:\.\d+)?/g) || []);
    const nameLookupKey = `${historyId}:${selectedGene?.orthogroupId}:${nameAccessions[0] || ""}`;
    useEffect(() => {
        let cancelled = false;
        setNameLoading(false);
        if (demoMode || !selectedGene || selectedGene.fullName || !nameAccessions.length) return;
        const groupId = selectedGene.orthogroupId;
        const accession = nameAccessions[0];
        setNameLoading(true);
        getOrthogroupAnnotations([accession]).then((annotations) => {
            const annotation = annotations[accession] || annotations[accession.split(".")[0]];
            if (!cancelled && annotation) setResultAnnotations((previous) => ({ ...previous, [groupId]: {
                annotation, speciesGenes: selectedGene.speciesGenes, geneIds: selectedGene.accessions
            } }));
        }).catch(() => {}).finally(() => { if (!cancelled) setNameLoading(false); });
        return () => { cancelled = true; };
    }, [nameLookupKey]);

    useEffect(() => {
        if (demoMode || !selectedGene || Object.keys(selectedGene.speciesGenes || {}).length || !historyId) return;
        let cancelled = false;
        getOrthogroupGenes(historyId, selectedGene.orthogroupId, galaxyApiKey)
            .then((speciesGenes) => {
                if (cancelled) return;
                setHyphyCards((current) => current.map((card) => normalizeIdentifier(card.elementIdentifier) === selectedGene.id
                    ? { ...card, geneSet: { ...(card.geneSet || {}), speciesGenes, geneIds: Object.values(speciesGenes || {}).flat() } }
                    : card));
            })
            .catch(() => {});
        return () => { cancelled = true; };
    }, [selectedGene?.id, historyId, galaxyApiKey]);

    const matchingGenes = useMemo(() => {
        const normalizedQuery = query.trim().toLowerCase();
        return genes.filter((gene) => {
            const evidence = evaluateGeneEvidence(gene, thresholds);
            if (normalizedQuery && !gene.searchText.includes(normalizedQuery)) return false;
            if (statusFilter !== "all" && gene.analysisStatus !== statusFilter) return false;
            if (evidenceFilter === "any" && !evidence.any) return false;
            if (evidenceFilter === "busted" && !evidence.bustedFdr) return false;
            if (evidenceFilter === "absrel" && !evidence.absrel) return false;
            if (evidenceFilter === "meme" && !evidence.meme) return false;
            if (evidenceFilter === "none" && evidence.any) return false;
            return true;
        });
    }, [genes, query, statusFilter, evidenceFilter, thresholds]);

    const activeGenes = useMemo(() => subsetIds
        ? matchingGenes.filter((gene) => subsetIds.has(gene.id))
        : matchingGenes, [matchingGenes, subsetIds]);

    const sortedGenes = useMemo(() => [...activeGenes].sort((left, right) => {
        const leftEvidence = evaluateGeneEvidence(left, thresholds);
        const rightEvidence = evaluateGeneEvidence(right, thresholds);
        const value = (gene, evidence) => {
            if (sortKey === "p") return gene.bustedPValue ?? Number.POSITIVE_INFINITY;
            if (sortKey === "q") return gene.bustedQValue ?? Number.POSITIVE_INFINITY;
            if (sortKey === "branches") return evidence.selectedBranches;
            if (sortKey === "sites") return evidence.selectedSites;
            if (sortKey === "alignment") return gene.alignmentLength ?? -1;
            if (sortKey === "status") return gene.analysisStatus;
            return gene.displayName.toLowerCase();
        };
        const leftValue = value(left, leftEvidence);
        const rightValue = value(right, rightEvidence);
        if (leftValue === rightValue) return left.displayName.localeCompare(right.displayName);
        const comparison = leftValue > rightValue ? 1 : -1;
        return sortDirection === "asc" ? comparison : -comparison;
    }), [activeGenes, sortKey, sortDirection, thresholds]);

    useEffect(() => setPage(1), [query, evidenceFilter, statusFilter, subsetIds, pageSize, sortKey, sortDirection]);
    const pageCount = Math.max(1, Math.ceil(sortedGenes.length / pageSize));
    const safePage = Math.min(page, pageCount);
    const visibleGenes = sortedGenes.slice((safePage - 1) * pageSize, safePage * pageSize);

    const geneSelectorOptions = useMemo(() => {
        const normalized = geneSearch.trim().toLowerCase();
        return genes.filter((gene) => !normalized || gene.searchText.includes(normalized)).slice(0, 200);
    }, [genes, geneSearch]);

    const completion = calculateAnalysisCompletion(genes, collectionInventory.geneIds.length);
    const qualityWarningCount = genes.filter((gene) => gene.warnings.length > 0).length;
    const allEvidence = genes.map((gene) => evaluateGeneEvidence(gene, thresholds));
    const metrics = {
        requested: completion.requested,
        complete: completion.complete,
        incomplete: completion.incomplete,
        busted: allEvidence.filter((evidence) => evidence.bustedFdr).length,
        absrel: allEvidence.filter((evidence) => evidence.absrel).length,
        meme: allEvidence.filter((evidence) => evidence.meme).length
    };

    const lineageMatrix = useMemo(() => buildLineageSelectionMatrix(activeGenes, thresholds), [activeGenes, thresholds]);
    const evidencePositiveGenes = useMemo(() => activeGenes.filter((gene) => evaluateGeneEvidence(gene, thresholds).any), [activeGenes, thresholds]);
    const enrichmentBackground = useMemo(() => genes.filter((gene) => gene.analysisStatus === "complete"), [genes]);
    const enrichmentResults = useMemo(() => calculateGoEnrichment(evidencePositiveGenes, enrichmentBackground), [evidencePositiveGenes, enrichmentBackground]);
    const hasGoAnnotations = enrichmentBackground.some((gene) => gene.goTerms.length > 0);

    const openGene = (gene, tab = "summary") => {
        setSelectedGeneId(gene.id);
        setGeneTab(tab);
        setResultView("gene-specific");
        window.requestAnimationFrame(() => document.getElementById("gene-specific-panel")?.scrollIntoView({ behavior: "smooth", block: "start" }));
    };

    const chooseGenes = (selected) => {
        const ids = selected.map((gene) => typeof gene === "string" ? gene : gene.id);
        setSubsetIds(new Set(ids));
    };

    const toggleGeneSubset = (geneId) => {
        setSubsetIds((current) => {
            const next = current ? new Set(current) : new Set(genes.map((gene) => gene.id));
            if (next.has(geneId)) next.delete(geneId); else next.add(geneId);
            return next;
        });
    };

    const persistNamedSets = (next) => {
        setNamedSets(next);
        window.localStorage.setItem(NAMED_SETS_KEY, JSON.stringify(next));
    };

    const saveNamedSet = () => {
        const name = setName.trim();
        if (!name) return;
        const ids = subsetIds ? [...subsetIds] : activeGenes.map((gene) => gene.id);
        persistNamedSets({ ...namedSets, [name]: ids });
        setSelectedNamedSet(name);
        setSetName("");
    };

    const loadNamedSet = (name) => {
        setSelectedNamedSet(name);
        if (!name) return;
        setSubsetIds(new Set(namedSets[name] || []));
    };

    const deleteNamedSet = () => {
        if (!selectedNamedSet) return;
        const next = { ...namedSets };
        delete next[selectedNamedSet];
        persistNamedSets(next);
        setSelectedNamedSet("");
    };

    const geneSummaryCsv = useMemo(() => rowsToDelimited(
        normalizedGeneRows(genes, thresholds),
        GENE_EXPORT_COLUMNS
    ), [genes, thresholds]);

    const provenance = useMemo(() => ({
        schemaVersion: 1,
        application: "phyloDIVaS",
        dashboardGeneratedAt: new Date().toISOString(),
        galaxyHistoryId: historyId,
        workflowState: jobState,
        species: analysisSpecies,
        genesRequested: metrics.requested,
        genesComplete: metrics.complete,
        methods: METHODS,
        thresholds: {
            BUSTED: { rawP: thresholds.bustedP, acrossGeneBhQ: thresholds.bustedQ },
            aBSREL: { correctedBranchP: thresholds.absrelP, correction: "HyPhy-provided branch-level correction" },
            MEME: { rawSiteP: thresholds.memeP, multipleTestingNote: "No additional across-site correction performed by phyloDIVaS" }
        },
        methodMetadata: Object.fromEntries(METHODS.map((method) => [method, hyphyCards.find((card) => card.method === method)?.provenance?.analysis || null])),
        warnings: genes.flatMap((gene) => gene.warnings.map((warning) => ({ gene: gene.displayName, orthogroup: gene.orthogroupId, warning })))
    }), [historyId, jobState, analysisSpecies, metrics.requested, metrics.complete, thresholds, hyphyCards, genes]);

    const downloadAnalysisBundle = async () => {
        setIsBundling(true);
        try {
            if (demoMode) {
                downloadJson({ demo: true, scope: "Genome-wide saved results; complete tool reports load per gene", species: analysisSpecies,
                    geneSummary: normalizedGeneRows(genes,thresholds),
                    jsonOutputs: hyphyJsonOutputs.map(output => ({ orthogroup: resultIdentifier(output), method: resultMethod(output), state: output.state })),
                    markdownOutputs: hyphyMarkdownOutputs.map(output => ({ orthogroup: resultIdentifier(output), method: resultMethod(output), report: output.markdownText }))
                }, "phylodivas_bat_demo_results.json");
                return;
            }
            const datasetMap = new Map();
            hyphyCards.forEach((card) => {
                if (!card.json?.id) return;
                datasetMap.set(card.json.id, {
                    id: card.json.id,
                    filename: `raw/hyphy/${safeDownloadName(card.elementIdentifier)}_${card.method}.json`
                });
            });
            hyphyMarkdownOutputs.forEach((output) => {
                if (!output?.id) return;
                datasetMap.set(output.id, {
                    id: output.id,
                    filename: `raw/hyphy/${safeDownloadName(resultIdentifier(output))}_${resultMethod(output)}.md`
                });
            });
            artifacts.forEach((artifact) => {
                if (!artifact?.id) return;
                const extension = artifact.kind === "gene_tree" ? "nwk" : "fasta";
                datasetMap.set(artifact.id, {
                    id: artifact.id,
                    filename: `raw/${artifact.kind}/${safeDownloadName(artifact.elementIdentifier)}.${extension}`
                });
            });
            const readme = [
                "phyloDIVaS analysis bundle",
                "",
                "Raw Galaxy/HyPhy files remain the source of truth.",
                "tables/gene_summary.csv contains normalized cross-method statistics.",
                "metadata/provenance.json records thresholds, method metadata, species, and quality warnings.",
                "BUSTED q-values use Benjamini-Hochberg correction across readable BUSTED tests.",
                "aBSREL uses HyPhy's corrected branch p-values.",
                "MEME site calls use raw site p-values and are not labeled as FDR-adjusted."
            ].join("\n");
            await downloadOrthoFinderOutputZip([], "phylodivas_complete_analysis.zip", galaxyApiKey, {
                datasets: [...datasetMap.values()],
                generatedFiles: [
                    { filename: "README.txt", content: readme },
                    { filename: "tables/gene_summary.csv", content: geneSummaryCsv },
                    { filename: "metadata/provenance.json", content: JSON.stringify(provenance, null, 2) }
                ]
            });
        } catch (bundleError) {
            window.alert(`Unable to build the analysis bundle: ${bundleError.message || bundleError}`);
        } finally {
            setIsBundling(false);
        }
    };

    const handlePrimaryTabKeyDown = (event) => {
        if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
        event.preventDefault();
        const next = event.key === "ArrowLeft" || event.key === "Home" ? "genome-wide" : "gene-specific";
        setResultView(next);
        window.requestAnimationFrame(() => document.getElementById(`${next}-tab`)?.focus());
    };

    const handleGeneTabKeyDown = (event) => {
        const tabs = ["summary", "BUSTED", "aBSREL", "MEME"];
        const current = tabs.indexOf(geneTab);
        if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
        event.preventDefault();
        let next = current;
        if (event.key === "Home") next = 0;
        if (event.key === "End") next = tabs.length - 1;
        if (event.key === "ArrowLeft") next = (current - 1 + tabs.length) % tabs.length;
        if (event.key === "ArrowRight") next = (current + 1) % tabs.length;
        setGeneTab(tabs[next]);
        window.requestAnimationFrame(() => document.getElementById(`gene-tab-${tabs[next]}`)?.focus());
    };

    const loading = loadProgress.total > 0 && loadProgress.complete < loadProgress.total;
    const safeJobInfo = useMemo(() => redactSensitiveValues(jobInfo), [jobInfo]);

    if (jobInfo?.hyphyDiscoveryError) return <Alert variant="warning">Result discovery is unavailable: {jobInfo.hyphyDiscoveryError} Use “Refresh now” above to retry. No biological conclusion can be drawn from this retrieval failure.</Alert>;
    if (jobInfo?.hyphyOutputsLoading || isPreparingResults || resolvedOutputs !== hyphyJsonOutputs) return (
        <section className={styles.resultsLoading} aria-labelledby="results-loading-title" aria-busy="true">
            <Spinner animation="border" aria-hidden="true" />
            <h4 id="results-loading-title">{demoMode?"Preparing saved results":"Preparing your Galaxy results"}</h4>
            <p>{demoMode?"Reading the saved local result family for genome-wide statistics.":loadAllResults ? "Retrieving the requested full result family for genome-wide statistics." : "Retrieving the selected gene’s BUSTED, aBSREL, and MEME outputs. Other genes load when selected."}</p>
            <progress aria-label="Result files processed" value={loadProgress.complete} max={loadProgress.total || 1} />
            <p role="status" aria-live="polite">{loadProgress.total > 0
                ? `${loadProgress.complete.toLocaleString()} of ${loadProgress.total.toLocaleString()} result files processed`
                : "Discovering result files…"}</p>
            <p>{demoMode?"Detailed tool reports load locally when a gene is opened.":"Large histories may take several minutes. Successfully read results are reused within this connected session. No API key or private result cache is saved for another session."}</p>
            <small>This is viewer loading progress, not Galaxy analysis progress. Unreadable files will be identified separately once loading finishes.</small>
        </section>
    );

    return (
        <ResultsWorkspace><div className={`${styles.alignment} ${styles.demoWorkspace}`}>
            {(statusMessage || errorMessage) && <Alert variant={errorMessage ? "danger" : "info"}>{errorMessage || statusMessage}</Alert>}

            <section className={styles.analysisOverview} aria-labelledby="analysis-overview-title">
                <div className={styles.overviewHeader}>
                    <div>
                        <p className={styles.eyebrow}>Comparative analysis results</p>
                        <h4 id="analysis-overview-title">Analysis overview</h4>
                    </div>
                    <div className={styles.overviewActions}>
                        <Button size="sm" variant="outline-primary" disabled={!genes.length} onClick={() => downloadText(geneSummaryCsv, "phylodivas_gene_summary.csv", "text/csv;charset=utf-8")}>Download table</Button>
                        <Button size="sm" variant="primary" disabled={!genes.length || isBundling} onClick={downloadAnalysisBundle}>
                            {isBundling ? <><Spinner size="sm" className="me-2" />Building bundle...</> : demoMode ? "Download demo results" : "Download complete analysis"}
                        </Button>
                    </div>
                </div>
                <dl className={styles.overviewGrid}>
                    <div><dt>Species</dt><dd>{analysisSpecies.length || "Not recovered"}</dd></div>
                    <div><dt>Genes requested</dt><dd>{metrics.requested.toLocaleString()}</dd></div>
                    <div><dt>Methods</dt><dd>BUSTED, aBSREL, MEME</dd></div>
                    <div><dt>Parsed three-method coverage</dt><dd>{metrics.complete.toLocaleString()} parsed ({formatPercent(metrics.requested ? metrics.complete / metrics.requested : 0)}) — not Galaxy job completion</dd></div>
                    <div><dt>{demoMode ? "Results source" : "Galaxy state"}</dt><dd>{demoMode ? "Saved demonstration" : jobState.replaceAll("_", " ")}</dd></div>
                    <div><dt>{demoMode ? "Scope" : "History"}</dt><dd>{demoMode ? `${metrics.requested.toLocaleString()} submitted orthogroups` : historyId ? <code>{historyId}</code> : "Not available"}</dd></div>
                </dl>
                {analysisSpecies.length > 0 && <p className={styles.speciesLine}><strong>Species:</strong> {analysisSpecies.join(", ")}</p>}
                {artifactError && <p className={styles.inlineWarning}>Alignment and tree download links could not be indexed: {artifactError.message}</p>}
            </section>

            <section className={styles.hyphySection} aria-labelledby="hyphy-results-title">
                <div className={styles.hyphyHeader}>
                    <div>
                        <h4 id="hyphy-results-title">Selection results dashboard</h4>
                        <p>Move from set-wide patterns to traceable gene, branch, and codon-level evidence. Raw HyPhy outputs remain the source of truth.</p>
                    </div>
                    {loading && <div className={styles.loadingStatus} role="status"><Spinner size="sm" /><span>Normalizing {loadProgress.complete.toLocaleString()} of {loadProgress.total.toLocaleString()} results</span></div>}
                </div>
                {loadError && <Alert variant="warning">{loadError} Retrieval failures are distinct from failed Galaxy jobs. <Button size="sm" onClick={() => setRetryResults(value => value + 1)}>Retry unreadable results</Button></Alert>}
                {!loadAllResults && <Alert variant="info">Gene-first view: only the selected gene is retrieved. Other gene files remain pending, not failed or nonsignificant. <Button size="sm" onClick={() => setLoadAllResults(true)}>Load full result family for genome-wide statistics</Button></Alert>}
                {loading && <Alert variant="info">Partial results: files are still being retrieved. Unloaded methods are pending, not negative tests. BUSTED BH q-values will be calculated after retrieval finishes, using all valid BUSTED tests, independently of display filters.</Alert>}
                <p role="status">Galaxy collection orthogroups: {METHODS.map((method) => `${method}: ${collectionInventory.counts[method]}`).join(" · ")}. {collectionInventory.consistent ? "All three tools contain the same genes." : "Collection gene sets do not yet match; review missing tool outputs."}</p>
                <Table size="sm" responsive aria-label="Method result retrieval status"><thead><tr><th>Method</th><th>Parsed</th><th>Pending retrieval / Galaxy processing</th><th>Paused</th><th>Galaxy failures</th><th>Unreadable</th><th>Missing / inaccessible</th></tr></thead><tbody>{METHODS.map(method => <tr key={method}><th>{method}</th>{["complete", "pending", "paused", "failed", "unreadable"].map(state => <td key={state}>{genes.filter(gene => gene.methodStatus[method] === state).length.toLocaleString()}</td>)}<td>{genes.filter(gene => ["missing", "deleted", "inaccessible"].includes(gene.methodStatus[method])).length.toLocaleString()}</td></tr>)}</tbody></Table>
                <p>Unique orthogroups with confirmed Galaxy failures: {genes.filter(gene => METHODS.some(method => gene.methodStatus[method] === "failed")).length.toLocaleString()}. BUSTED correction family: {genes.filter(gene => gene.methodStatus.BUSTED === "complete").length.toLocaleString()} valid tests{loading ? " retrieved so far; q-values withheld until retrieval finishes" : "; excludes unreadable and failed tests"}.</p>

                <div className={styles.resultTabs} role="tablist" aria-label="Results dashboard views">
                    <button type="button" id="genome-wide-tab" role="tab" aria-selected={resultView === "genome-wide"} aria-controls="genome-wide-panel"
                        tabIndex={resultView === "genome-wide" ? 0 : -1} className={`${styles.resultTab} ${resultView === "genome-wide" ? styles.activeResultTab : ""}`}
                        onClick={() => {setLoadAllResults(true);setResultView("genome-wide");}} onKeyDown={handlePrimaryTabKeyDown}>Genome-wide Insights</button>
                    <button type="button" id="gene-specific-tab" role="tab" aria-selected={resultView === "gene-specific"} aria-controls="gene-specific-panel"
                        tabIndex={resultView === "gene-specific" ? 0 : -1} className={`${styles.resultTab} ${resultView === "gene-specific" ? styles.activeResultTab : ""}`}
                        onClick={() => setResultView("gene-specific")} onKeyDown={handlePrimaryTabKeyDown}>Gene-Specific Analysis</button>
                </div>

                {!loading && genes.length === 0 && <Alert variant="secondary">No readable native HyPhy outputs are available for this completed history.</Alert>}

                {genes.length > 0 && resultView === "genome-wide" && !loadAllResults && <Alert variant="info">Load the full result family above before calculating genome-wide summaries. Individual genes remain available in Gene-Specific Analysis.</Alert>}
                {genes.length > 0 && resultView === "genome-wide" && loadAllResults && (
                    <div id="genome-wide-panel" role="tabpanel" aria-labelledby="genome-wide-tab" className={styles.resultPanel}>
                        <div className={styles.metricGrid}>
                            <article><span>Successfully analyzed</span><strong>{metrics.complete.toLocaleString()}</strong><small>all three methods readable</small></article>
                            <article><span>Gene-level exceptions</span><strong>{metrics.incomplete.toLocaleString()}</strong><small>paused, failed or unavailable methods</small></article>
                            <article><span>BUSTED FDR</span><strong>{metrics.busted.toLocaleString()}</strong><small>genes at BH q {"<="} {thresholds.bustedQ}</small></article>
                            <article><span>aBSREL</span><strong>{metrics.absrel.toLocaleString()}</strong><small>genes with selected branches</small></article>
                            <article><span>MEME</span><strong>{metrics.meme.toLocaleString()}</strong><small>genes with selected sites</small></article>
                            <article><span>Quality warnings</span><strong>{qualityWarningCount.toLocaleString()}</strong><small>genes requiring review</small></article>
                        </div>

                        <section className={styles.filterPanel} aria-labelledby="filter-title">
                            <div className={styles.filterHeader}>
                                <div><h5 id="filter-title">Genes and statistical thresholds</h5><p>Aggregate figures currently include <strong>{activeGenes.length.toLocaleString()}</strong> of {genes.length.toLocaleString()} genes.</p></div>
                                <Button size="sm" variant="outline-secondary" onClick={() => { setQuery(""); setEvidenceFilter("all"); setStatusFilter("all"); setSubsetIds(null); }}>Reset filters</Button>
                            </div>
                            <div className={styles.filterGrid}>
                                <Form.Group controlId="result-gene-search"><Form.Label>Gene, alias, or accession</Form.Label><Form.Control type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="e.g., SLFN11 or schlafen" /></Form.Group>
                                <Form.Group controlId="result-evidence-filter"><Form.Label>Selection evidence</Form.Label><Form.Select value={evidenceFilter} onChange={(event) => setEvidenceFilter(event.target.value)}>
                                    <option value="all">All evidence states</option><option value="any">Any method</option><option value="busted">BUSTED FDR</option><option value="absrel">aBSREL branch</option><option value="meme">MEME site</option><option value="none">No detected evidence</option>
                                </Form.Select></Form.Group>
                                <Form.Group controlId="result-status-filter"><Form.Label>Analysis status</Form.Label><Form.Select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
                                    <option value="all">All statuses</option><option value="complete">Complete</option><option value="partial">Partial</option><option value="failed">Failed</option><option value="missing">Missing</option><option value="paused">Paused</option><option value="pending">Pending</option><option value="unreadable">Unreadable</option>
                                </Form.Select></Form.Group>
                                <Form.Group controlId="threshold-busted-p"><Form.Label>BUSTED raw p</Form.Label><Form.Control type="number" min="0.0001" max="1" step="0.01" value={thresholds.bustedP} onChange={(event) => setThresholds((current) => ({ ...current, bustedP: Number(event.target.value) }))} /></Form.Group>
                                <Form.Group controlId="threshold-busted-q"><Form.Label>BUSTED BH q</Form.Label><Form.Control type="number" min="0.0001" max="1" step="0.01" value={thresholds.bustedQ} onChange={(event) => setThresholds((current) => ({ ...current, bustedQ: Number(event.target.value) }))} /></Form.Group>
                                <Form.Group controlId="threshold-absrel"><Form.Label>aBSREL corrected p</Form.Label><Form.Control type="number" min="0.0001" max={savedResults?.summaryThresholdCaps.absrelP||1} step="0.01" value={thresholds.absrelP} onChange={(event) => setThresholds((current) => ({ ...current, absrelP: Math.min(savedResults?.summaryThresholdCaps.absrelP||1,Math.max(.0001,Number(event.target.value))) }))} /></Form.Group>
                                <Form.Group controlId="threshold-meme"><Form.Label>MEME raw site p</Form.Label><Form.Control type="number" min="0.0001" max={savedResults?.summaryThresholdCaps.memeP||1} step="0.01" value={thresholds.memeP} onChange={(event) => setThresholds((current) => ({ ...current, memeP: Math.min(savedResults?.summaryThresholdCaps.memeP||1,Math.max(.0001,Number(event.target.value))) }))} /></Form.Group>
                            </div>
                            <div className={styles.subsetControls}>
                                <Button size="sm" variant="outline-primary" onClick={() => setSubsetIds(new Set(matchingGenes.map((gene) => gene.id)))}>Use matching genes as subset</Button>
                                <Button size="sm" variant="outline-secondary" onClick={() => setSubsetIds(null)}>Use all matching genes</Button>
                                <Form.Control aria-label="New gene-set name" value={setName} onChange={(event) => setSetName(event.target.value)} placeholder="Name this gene set" />
                                <Button size="sm" variant="outline-primary" disabled={!setName.trim()} onClick={saveNamedSet}>Save set</Button>
                                <Form.Select aria-label="Saved gene sets" value={selectedNamedSet} onChange={(event) => loadNamedSet(event.target.value)}>
                                    <option value="">Saved gene sets</option>{Object.keys(namedSets).sort().map((name) => <option key={name} value={name}>{name}</option>)}
                                </Form.Select>
                                <Button size="sm" variant="outline-danger" disabled={!selectedNamedSet} onClick={deleteNamedSet}>Delete set</Button>
                            </div>
                        </section>

                        {savedResults?.summaryThresholdCaps&&<p className={styles.figureNote}>Saved aggregate site calls support thresholds up to MEME p = 0.1 and aBSREL corrected p = 0.05. Detailed reports include all codons and branches. BUSTED correction always uses the full readable family.</p>}
                        <div className={styles.figureGrid}>
                            <RankedSelectionPlot genes={activeGenes} thresholds={thresholds} onOpenGene={openGene} />
                            <BustedHistogram genes={activeGenes} thresholds={thresholds} onOpenGene={(gene) => openGene(gene, "BUSTED")} />
                            <EvidenceScatterplot genes={activeGenes} thresholds={thresholds} onOpenGene={openGene} />
                            <MethodOverlapPlot genes={activeGenes} thresholds={thresholds} onChooseGenes={chooseGenes} />
                            <LineageHeatmap matrix={lineageMatrix} count={activeGenes.length} thresholds={thresholds} onOpenGene={(gene) => openGene(gene, "aBSREL")} />
                            <RankedEnrichment genes={genes} geneSets={savedResults?.geneSets} familyReady={loadAllResults&&!loading}/>
                            {hasGoAnnotations && (
                                <GoEnrichmentPlot results={enrichmentResults} backgroundCount={enrichmentBackground.length} targetCount={evidencePositiveGenes.length} onChooseGenes={(ids) => chooseGenes(ids.map((id) => genes.find((gene) => gene.id === id)).filter(Boolean))} />
                            )}
                        </div>

                        <section className={styles.rankedExplorer} aria-labelledby="ranked-explorer-title">
                            <div className={styles.tableToolbar}>
                                <div><h5 id="ranked-explorer-title">Ranked gene explorer</h5><p>{sortedGenes.length.toLocaleString()} genes in the active view</p></div>
                                <div className={styles.tableControls}>
                                    <Form.Select aria-label="Sort genes" value={sortKey} onChange={(event) => setSortKey(event.target.value)}>
                                        <option value="gene">Gene name</option><option value="p">BUSTED p-value</option><option value="q">BUSTED q-value</option><option value="branches">aBSREL branches</option><option value="sites">MEME sites</option><option value="alignment">Alignment length</option><option value="status">Status</option>
                                    </Form.Select>
                                    <Button size="sm" variant="outline-secondary" onClick={() => setSortDirection((current) => current === "asc" ? "desc" : "asc")} aria-label={`Sort ${sortDirection === "asc" ? "descending" : "ascending"}`}>{sortDirection === "asc" ? "▲" : "▼"}</Button>
                                    <Form.Select aria-label="Rows per page" value={pageSize} onChange={(event) => setPageSize(Number(event.target.value))}>{RESULT_PAGE_SIZES.map((size) => <option key={size} value={size}>{size} rows</option>)}</Form.Select>
                                </div>
                            </div>
                            <div className={styles.nativeTableWrap}>
                                <table className={styles.nativeTable}>
                                    <caption className="visually-hidden">Normalized HyPhy selection results by gene</caption>
                                    <thead><tr><th scope="col">Include</th><th scope="col">Gene</th><th scope="col">Status</th><th scope="col">BUSTED p / q</th><th scope="col">aBSREL branches</th><th scope="col">MEME sites</th><th scope="col">Coverage</th><th scope="col"><span className="visually-hidden">Action</span></th></tr></thead>
                                    <tbody>{visibleGenes.map((gene) => {
                                        const evidence = evaluateGeneEvidence(gene, thresholds);
                                        const included = !subsetIds || subsetIds.has(gene.id);
                                        return (
                                            <tr key={gene.id}>
                                                <td><Form.Check checked={included} onChange={() => toggleGeneSubset(gene.id)} aria-label={`${included ? "Exclude" : "Include"} ${gene.displayName}`} /></td>
                                                <th scope="row"><span className={styles.geneFamilyName}>{gene.displayName}</span><span>{gene.orthogroupId}</span></th>
                                                <td><SelectionBadge available={["complete","partial"].includes(gene.analysisStatus)} selected={gene.analysisStatus === "complete"}>{gene.analysisStatus}</SelectionBadge></td>
                                                <td>{formatNumber(gene.bustedPValue)} / {formatNumber(gene.bustedQValue)}{evidence.bustedFdr && <span className={styles.tableEvidence}>FDR</span>}</td>
                                                <td>{evidence.selectedBranches.toLocaleString()}</td>
                                                <td>{evidence.selectedSites.toLocaleString()}</td>
                                                <td>{gene.speciesCount ?? "N/A"}{gene.expectedSpeciesCount ? ` / ${gene.expectedSpeciesCount}` : ""}</td>
                                                <td><Button size="sm" variant="outline-primary" aria-label={`View detailed report for ${gene.displayName} (${gene.orthogroupId})`} onClick={() => openGene(gene)}>Detailed report</Button></td>
                                            </tr>
                                        );
                                    })}</tbody>
                                </table>
                            </div>
                            {!visibleGenes.length && <p className={styles.emptyState}>No genes match the active filters and subset.</p>}
                            <div className={styles.pagination}><Button size="sm" variant="outline-secondary" disabled={safePage === 1} onClick={() => setPage((current) => Math.max(1, current - 1))}>Previous</Button><span>Page {safePage} of {pageCount}</span><Button size="sm" variant="outline-secondary" disabled={safePage === pageCount} onClick={() => setPage((current) => Math.min(pageCount, current + 1))}>Next</Button></div>
                        </section>
                    </div>
                )}

                {genes.length > 0 && resultView === "gene-specific" && (
                    <div id="gene-specific-panel" role="tabpanel" aria-labelledby="gene-specific-tab" className={`${styles.resultPanel} ${styles.geneSpecificLayout}`}>
                        <aside className={styles.geneSelector} aria-label="Select a gene">
                            <Form.Group controlId="gene-detail-search"><Form.Label>Find a gene</Form.Label><Form.Control type="search" value={geneSearch} onChange={(event) => setGeneSearch(event.target.value)} placeholder="Symbol, name, alias, or accession" /></Form.Group>
                            <div className={styles.geneOptionList} role="listbox" aria-label="Genes">
                                {geneSelectorOptions.map((gene) => (
                                    <button type="button" role="option" aria-selected={gene.id === selectedGeneId} key={gene.id}
                                        className={gene.id === selectedGeneId ? styles.activeGeneOption : styles.geneOption}
                                        onClick={() => { setSelectedGeneId(gene.id); setGeneTab("summary"); }}>
                                        <strong>{gene.displayName}</strong><span>{gene.orthogroupId}</span>
                                    </button>
                                ))}
                            </div>
                            {geneSelectorOptions.length === 200 && <small>Showing the first 200 matches. Refine the search to narrow the list.</small>}
                        </aside>

                        {selectedGene && (!savedResults||detailReport?.gene===selectedGene.orthogroupId) && (
                            <article className={styles.geneWorkspace}>
                                <header className={styles.geneHeader}>
                                    <div><p className={styles.eyebrow}>{demoMode ? "Saved orthogroup example" : "Strict 1:1 ortholog"}</p><h4>{selectedGene.displayName}</h4><p>{selectedGene.fullName && selectedGene.fullName !== selectedGene.displayName ? selectedGene.fullName : selectedGene.orthogroupId}</p></div>
                                    <div className={styles.geneHeaderActions}>
                                        <Button size="sm" variant="outline-primary" onClick={() => downloadJson({
                                            gene: normalizedGeneRows([selectedGene], thresholds)[0],
                                            interpretation: interpretGene(selectedGene, thresholds),
                                            speciesGenes: selectedGene.speciesGenes,
                                            warnings: selectedGene.warnings
                                        }, `${safeDownloadName(selectedGene.displayName)}_normalized_result.json`)}>Normalized result</Button>
                                    </div>
                                </header>

                                <div className={styles.geneResultTabs} role="tablist" aria-label={`${selectedGene.displayName} result sections`}>
                                    {["summary", "BUSTED", "aBSREL", "MEME"].map((tab) => (
                                        <button type="button" role="tab" id={`gene-tab-${tab}`} key={tab} aria-selected={geneTab === tab} tabIndex={geneTab === tab ? 0 : -1}
                                            className={`${styles.geneResultTab} ${geneTab === tab ? styles.activeGeneResultTab : ""}`} onClick={() => setGeneTab(tab)} onKeyDown={handleGeneTabKeyDown}>
                                            {tab === "summary" ? "Summary" : tab}
                                        </button>
                                    ))}
                                </div>

                                {geneTab === "summary" ? (
                                    <div className={styles.geneSummary}>
                                        <dl className={styles.geneMetadata}>
                                            <div><dt>Gene symbol / name</dt><dd>{selectedGene.symbol || selectedGene.displayName}</dd></div>
                                            <div><dt>Full name</dt><dd>{selectedGene.fullName || (nameLoading ? "Retrieving gene name from NCBI..." : "Name unavailable from NCBI")}</dd></div>
                                            <div><dt>Orthogroup</dt><dd><code>{selectedGene.orthogroupId}</code></dd></div>
                                            <div><dt>Orthology</dt><dd>{Object.keys(selectedGene.speciesGenes).length ? "Membership available; inspect species-specific accessions below" : "Membership not yet verified from the OrthoFinder source table"}</dd></div>
                                            <div><dt>Alignment length</dt><dd>{selectedGene.alignmentLength ? `${selectedGene.alignmentLength.toLocaleString()} codons` : "Not available"}</dd></div>
                                            <div><dt>Species coverage</dt><dd>{selectedGene.speciesCount ?? "N/A"}{selectedGene.expectedSpeciesCount ? ` of ${selectedGene.expectedSpeciesCount} (${formatPercent(selectedGene.speciesCoverage)})` : ""}</dd></div>
                                        </dl>
                                        <section className={styles.functionalAnnotation}><h5>Functional annotation</h5><p>{selectedGene.description || "No reference-species functional annotation is available."}</p>{selectedGene.aliases.length > 0 && <p><strong>Aliases:</strong> {selectedGene.aliases.join(", ")}</p>}{selectedGene.annotationSourceUrl && <a href={selectedGene.annotationSourceUrl} target="_blank" rel="noopener noreferrer">View source annotation</a>}</section>
                                        <GeneMarkdownSummaries gene={selectedGene} markdownOutputs={selectedMarkdownOutputs} galaxyApiKey={galaxyApiKey} />
                                        <div className={styles.geneEvidenceGrid}>
                                            {METHODS.map((method) => {
                                                const evidence = evaluateGeneEvidence(selectedGene, thresholds);
                                                const selected = method === "BUSTED" ? evidence.bustedFdr : method === "aBSREL" ? evidence.absrel : evidence.meme;
                                                const value = selectedGene.methodStatus[method] !== "complete" ? "Results not available yet" : method === "BUSTED" ? `p ${formatNumber(selectedGene.bustedPValue)} · q ${formatNumber(selectedGene.bustedQValue)}` : method === "aBSREL" ? `${evidence.selectedBranches} selected branches` : `${evidence.selectedSites} selected sites`;
                                                return <button type="button" key={method} onClick={() => setGeneTab(method)}><span>{method}</span><strong>{value}</strong><SelectionBadge available={selectedGene.methodStatus[method] === "complete"} selected={selected}>{selectedGene.methodStatus[method] !== "complete" ? selectedGene.methodStatus[method] : selected ? "Evidence detected" : "No evidence"}</SelectionBadge></button>;
                                            })}
                                        </div>
                                        <section className={styles.interpretation}><h5>Integrated interpretation</h5><p>{interpretGene(selectedGene, thresholds)}</p><small>This statement follows deterministic threshold rules and does not combine p-values into a consensus score.</small></section>
                                        {selectedGene.warnings.length > 0 && <Alert variant="warning"><Alert.Heading>Quality-control notes</Alert.Heading><ul>{selectedGene.warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></Alert>}
                                        <details className={styles.speciesGeneDetails}><summary>Species-specific accessions ({Object.keys(selectedGene.speciesGenes).length})</summary><dl>{Object.entries(selectedGene.speciesGenes).map(([species, identifiers]) => <div key={species}><dt>{species.replaceAll("_", " ")}</dt><dd>{String(identifiers)}</dd></div>)}</dl></details>
                                    </div>
                                ) : (
                                    <MethodResultPanel gene={selectedGene} method={geneTab} thresholds={thresholds} markdownOutputs={selectedMarkdownOutputs} artifacts={artifacts} galaxyApiKey={galaxyApiKey} />
                                )}
                            </article>
                        )}
                        {selectedGene&&savedResults&&detailReport?.gene!==selectedGene.orthogroupId&&<section className={styles.geneWorkspace} aria-busy={!detailError}>{detailError?<Alert variant="warning">{detailError} <Button onClick={()=>setDetailAttempt(n=>n+1)}>Retry gene report</Button></Alert>:<p role="status">Loading the three-tool report for {selectedGene.displayName}…</p>}</section>}
                    </div>
                )}
            </section>

            <details className={styles.jobDetails} onToggle={event=>setShowDiagnostics(event.currentTarget.open)}>
                <summary>Analysis provenance and diagnostic payload</summary>
                <div className={styles.jobDetailsHeader}><Button size="sm" variant="outline-secondary" onClick={() => downloadJson(provenance, "phylodivas_provenance.json")}>Download provenance</Button></div>
                {showDiagnostics&&<pre className={styles.jobPayload}>{JSON.stringify({ provenance, job: safeJobInfo }, null, 2)}</pre>}
            </details>
        </div></ResultsWorkspace>
    );
}

AnalysisResults.propTypes = {
    jobInfo: PropTypes.object,
    statusMessage: PropTypes.string,
    errorMessage: PropTypes.string,
    orthologousGroups: PropTypes.array,
    analysisGroups: PropTypes.array,
    genomes: PropTypes.array,
    historyId: PropTypes.string,
    requestedGeneCount: PropTypes.number,
    demoMode: PropTypes.bool,
    savedResults: PropTypes.object
};

AnalysisResults.defaultProps = {
    jobInfo: null,
    statusMessage: "",
    errorMessage: "",
    orthologousGroups: [],
    analysisGroups: [],
    genomes: [],
    historyId: null,
    requestedGeneCount: 0
};

export default AnalysisResults;
export { MethodResultPanel };
