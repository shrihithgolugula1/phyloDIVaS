import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import Form from 'react-bootstrap/Form';
import Button from 'react-bootstrap/Button';
import Spinner from 'react-bootstrap/Spinner';
import Alert from 'react-bootstrap/Alert';
import Collapse from 'react-bootstrap/Collapse';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { faCircleInfo } from '@fortawesome/free-solid-svg-icons';
import 'bootstrap/dist/css/bootstrap.min.css';
import analysisPageStyles from './GenomeAnalysisPage.module.css';
import parametersStyles from './GenomeAnalysisPage.module.css';
import resultsStyles from './GenomeAnalysisPage.module.css';
import selectorStyles from '../OrthologousGroupSelector/OrthologousGroupSelector.module.css';
import { 
    startGenomeAnalysis, 
    subscribeToGenomeAnalysisProgress,
    getOrthologousGroups, 
    analyzeMultipleGroups, 
    analyzeAllGroups,
    pollJob,
    resumeGenomeAnalysis,
    inspectGenomeAnalysis,
    continuePreparedGenomeAnalysis,
    getOrthoFinderOutputs,
    getHyphyOutputs,
    downloadOrthoFinderOutput,
    downloadOrthoFinderOutputFile,
    suggestSpecies,
    getReferenceSpeciesTree,
    getSpeciesTreeTimes,
    validateGalaxyApiKey,
    cancelGenomeAnalysis,
    retryHyphyFromCompletedAlignments
} from "../../api/dndsApi";
import OrthologousGroupSelector from "../OrthologousGroupSelector/OrthologousGroupSelector";
import AnalysisResults from "../ToDo/AnalysisResults/AnalysisResults";
import { getSpeciesSilhouette } from "../../utils/phylopic";

const MIN_SPECIES_COUNT = 3;
const MAX_SPECIES_COUNT = 10;
const DEFAULT_DS_SATURATION_WARNING_MA = 50;

const WORKFLOW_STEPS = {
    SETUP: 'setup',
    GENOME_ANALYSIS: 'genome_analysis',
    GROUP_SELECTION: 'group_selection',
    RUNNING_ANALYSIS: 'running_analysis',
    COMPLETED: 'completed'
};

const WORKFLOW_MODES = {
    NEW: 'new',
    RESUME: 'resume'
};

const PIPELINE_STAGES = [
    "Retrieve genomes",
    "Prepare Galaxy data",
    "Run OrthoFinder",
    "Prepare selected orthogroups",
    "Align and run selection tests",
    "Results"
];

const extractGalaxyHistoryId = (value) => {
    const trimmed = (value || "").trim();
    if (!trimmed) return "";
    try {
        const url = new URL(trimmed);
        return url.searchParams.get("id")?.trim() || trimmed;
    } catch {
        return trimmed;
    }
};

const getDetectedPipelineStage = (state, currentStep) => {
    const normalized = (state || "").toLowerCase();
    if (normalized === "genome_preparation_failed") return 1;
    if (normalized === "workflow_complete") return 5;
    if (normalized === "workflow_failed") return 4;
    if (normalized === "hyphy_ready") return 4;
    if (normalized.startsWith("workflow_")) return 4;
    if (["orthofinder_complete", "orthogroups_ready"].includes(normalized)) return 3;
    if (normalized.includes("orthofinder") || (["new", "queued", "running", "waiting"].includes(normalized) && currentStep === WORKFLOW_STEPS.GENOME_ANALYSIS)) return 2;
    if (["uploading", "unzipped"].includes(normalized)) return 1;
    if (currentStep === WORKFLOW_STEPS.COMPLETED) return 5;
    if (currentStep === WORKFLOW_STEPS.RUNNING_ANALYSIS) return 4;
    if (currentStep === WORKFLOW_STEPS.GROUP_SELECTION) return 3;
    return 0;
};

const normalizeGalaxyStatus = (status) => {
    if (!status || status.stage?.startsWith("workflow_") || status.stage === "hyphy_ready") return status;
    const preparationEvidence = (status.evidence || []).filter((item) =>
        /orthogroup[_ ]ids|selected orthogroups|orthogroup_workflow_invoked/i.test(item?.name || "")
    );
    if (preparationEvidence.length === 0) return status;

    const activeStates = new Set(["new", "queued", "running", "waiting", "upload", "setting_metadata"]);
    const failedStates = new Set(["error", "failed", "discarded"]);
    const hasFailure = preparationEvidence.some((item) => failedStates.has((item.state || "").toLowerCase()));
    const hasActive = preparationEvidence.some((item) => activeStates.has((item.state || "").toLowerCase()));
    return {
        ...status,
        stage: hasFailure ? "workflow_failed" : hasActive ? "workflow_preparing" : "workflow_pending",
        detail: hasFailure
            ? "Galaxy failed while preparing the selected orthogroups for alignment."
            : hasActive
                ? "Galaxy is preparing the selected orthogroups and genome collections for the alignment workflow."
                : "Selected orthogroups are prepared; waiting for the alignment workflow to be invoked."
    };
};

const getPipelineTimeEstimate = ({ stage, currentStep, speciesCount, groupCount, jobs }) => {
    const normalized = (stage || "").toLowerCase();
    const queueNote = (jobs?.queued || 0) > 0
        ? " Galaxy queue time is included only approximately."
        : "";

    if (normalized === "workflow_complete" || currentStep === WORKFLOW_STEPS.COMPLETED && normalized !== "workflow_failed") {
        return { value: "Complete", detail: "Results are ready to review." };
    }
    if (normalized === "workflow_failed") {
        return { value: "Paused", detail: "The failed Galaxy step must be resolved before processing can continue." };
    }
    if (["orthogroups_ready", "orthofinder_complete", "workflow_pending"].includes(normalized)) {
        return { value: "Ready for your input", detail: "No compute time remains until orthogroups are selected and submitted." };
    }
    if (normalized === "workflow_preparing") {
        const value = groupCount <= 25
            ? "About 2–15 minutes"
            : groupCount <= 500
                ? "About 10 minutes–2 hours"
                : groupCount <= 2000
                    ? "About 30 minutes–6 hours"
                    : "About 2–12+ hours";
        return { value, detail: `Total estimate for preparing the complete ${Math.max(groupCount, 1).toLocaleString()}-orthogroup batch.${queueNote}` };
    }
    if (normalized === "workflow_running" || currentStep === WORKFLOW_STEPS.RUNNING_ANALYSIS) {
        const value = groupCount <= 5
            ? "About 10–60 minutes"
            : groupCount <= 25
                ? "About 30 minutes–3 hours"
                : groupCount <= 100
                    ? "About 1–8 hours"
                    : groupCount <= 500
                        ? "About 4–24 hours"
                        : groupCount <= 2000
                            ? "About 12 hours–3 days"
                            : "About 2–10+ days";
        return { value, detail: `Total estimate for the complete ${Math.max(groupCount, 1).toLocaleString()}-orthogroup batch, including alignment, back-translation, gene-tree inference, and all HyPhy tests.${queueNote}` };
    }
    if (normalized === "orthofinder_running" || currentStep === WORKFLOW_STEPS.GENOME_ANALYSIS) {
        const value = speciesCount <= 4
            ? "About 20–90 minutes"
            : speciesCount <= 8
                ? "About 45 minutes–4 hours"
                : "About 1–8+ hours";
        return { value, detail: `Estimate for ${Math.max(speciesCount, 1)} species; OrthoFinder time depends heavily on proteome size and Galaxy capacity.${queueNote}` };
    }
    if (["uploading", "unzipped"].includes(normalized)) {
        return { value: "About 5–30 minutes", detail: `Genome transfer and Galaxy collection preparation are in progress.${queueNote}` };
    }
    return { value: "Calculating…", detail: "An estimate will appear once Galaxy reports the active pipeline stage." };
};

const SESSION_KEY = "phyloAnalysisSession";
const saveSession = (payload) => {
    try {
        const existing = JSON.parse(localStorage.getItem(SESSION_KEY) || "{}");
        localStorage.setItem(SESSION_KEY, JSON.stringify({ ...existing, ...payload }));
    } catch {
        localStorage.setItem(SESSION_KEY, JSON.stringify(payload));
    }
};
const clearSession = () => {
    try { localStorage.removeItem(SESSION_KEY); } catch { /* ignore */ }
};

// Lightweight Newick parser and branch-length-aware layout for SVG trees.
const parseNewick = (newick) => {
    if (!newick) return null;
    let idx = 0;
    const skipWhitespace = () => {
        while (/\s/.test(newick[idx] || "")) idx += 1;
    };
    const readLabel = () => {
        skipWhitespace();
        if (newick[idx] === "'") {
            idx += 1;
            let label = "";
            while (idx < newick.length) {
                if (newick[idx] === "'" && newick[idx + 1] === "'") {
                    label += "'";
                    idx += 2;
                } else if (newick[idx] === "'") {
                    idx += 1;
                    break;
                } else {
                    label += newick[idx++];
                }
            }
            return label.trim();
        }
        let label = "";
        while (idx < newick.length && ![":", ",", "(", ")", ";"].includes(newick[idx])) {
            label += newick[idx++];
        }
        return label.trim();
    };
    const readBranchLength = () => {
        skipWhitespace();
        if (newick[idx] !== ":") return 0;
        idx += 1;
        let value = "";
        while (idx < newick.length && ![",", "(", ")", ";"].includes(newick[idx])) {
            value += newick[idx++];
        }
        const parsed = Number.parseFloat(value.trim());
        return Number.isFinite(parsed) && parsed >= 0 ? parsed : 0;
    };
    const readNode = () => {
        skipWhitespace();
        const node = {};
        if (newick[idx] === "(") {
            idx += 1;
            node.children = [];
            do {
                node.children.push(readNode());
                skipWhitespace();
                if (newick[idx] === ",") idx += 1;
                else break;
            } while (idx < newick.length);
            if (newick[idx] === ")") idx += 1;
        }
        const label = readLabel();
        if (label) node.name = label;
        node.branchLength = readBranchLength();
        return node;
    };
    return readNode();
};

const layoutTree = (node, targetWidth = 720) => {
    let maxDistance = 0;
    let maxDepth = 0;
    let hasBranchLengths = false;
    const inspect = (n, distance, depth) => {
        const length = Number.isFinite(n.branchLength) ? n.branchLength : 0;
        const nextDistance = distance + length;
        if (length > 0) hasBranchLengths = true;
        maxDistance = Math.max(maxDistance, nextDistance);
        maxDepth = Math.max(maxDepth, depth);
        (n.children || []).forEach((child) => inspect(child, nextDistance, depth + 1));
    };
    inspect(node, 0, 0);

    const distanceScale = hasBranchLengths && maxDistance > 0 ? targetWidth / maxDistance : 0;
    const depthScale = maxDepth > 0 ? targetWidth / maxDepth : targetWidth;
    const assign = (n, distance, depth, positions) => {
        const nextDistance = distance + (Number.isFinite(n.branchLength) ? n.branchLength : 0);
        const x = hasBranchLengths ? nextDistance * distanceScale : depth * depthScale;
        if (!n.children || n.children.length === 0) {
            const y = positions.count * 60;
            positions.count += 1;
            return { ...n, x, y };
        }
        const laidOut = n.children.map((child) => assign(child, nextDistance, depth + 1, positions));
        const y = laidOut.reduce((sum, c) => sum + c.y, 0) / laidOut.length;
        return { ...n, children: laidOut, x, y };
    };
    const laidOut = assign(node, 0, 0, { count: 0 });
    laidOut.layoutMeta = { hasBranchLengths, maxDistance, distanceScale };
    return laidOut;
};

const layoutTreeByTime = (node, targetWidth = 720) => {
    let maxAgeMa = 0;
    const inspect = (n) => {
        const age = Number(n.divergenceTime?.ageMa);
        if (Number.isFinite(age)) maxAgeMa = Math.max(maxAgeMa, age);
        (n.children || []).forEach(inspect);
    };
    inspect(node);
    if (maxAgeMa <= 0) return null;

    const assign = (n, positions) => {
        if (!n.children?.length) {
            const y = positions.count * 60;
            positions.count += 1;
            return { ...n, x: targetWidth, y, effectiveAgeMa: 0 };
        }
        const children = n.children.map((child) => assign(child, positions));
        const y = children.reduce((sum, child) => sum + child.y, 0) / children.length;
        const age = Number(n.divergenceTime?.ageMa);
        const childAge = Math.max(0, ...children.map((child) => child.effectiveAgeMa || 0));
        const effectiveAgeMa = Number.isFinite(age) ? Math.max(age, childAge) : childAge;
        const timedX = targetWidth * (1 - effectiveAgeMa / maxAgeMa);
        return {
            ...n,
            children,
            x: Math.max(0, Math.min(Math.min(...children.map((child) => child.x)), timedX)),
            y,
            effectiveAgeMa
        };
    };
    const laidOut = assign(node, { count: 0 });
    laidOut.layoutMeta = { isTimeScale: true, maxAgeMa, targetWidth };
    return laidOut;
};

const normalizeTreeTaxon = (name) => String(name || "")
    .replace(/^'|'$/g, "")
    .replace(/_(?:protein|proteome)(?:_filtered)?(?:\.faa)?$/i, "")
    .replace(/\.(?:faa|fasta|fa)$/i, "")
    .replace(/_/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .toLowerCase();

const collectLeafNames = (node, names = []) => {
    if (!node?.children?.length) {
        if (node?.name) names.push(node.name);
        return names;
    }
    node.children.forEach((child) => collectLeafNames(child, names));
    return names;
};

const annotateDivergenceTimes = (tree, estimates = []) => {
    const byClade = new Map(estimates.map((estimate) => [estimate.cladeKey, estimate]));
    const visit = (node) => {
        if (!node.children?.length) return [node.name].filter(Boolean);
        const leaves = node.children.flatMap(visit).sort((a, b) => a.localeCompare(b));
        node.divergenceTime = byClade.get(leaves.join("|")) || null;
        return leaves;
    };
    visit(tree);
    return tree;
};

const encodeBase64Url = (value) => {
    const bytes = new TextEncoder().encode(value);
    let binary = "";
    bytes.forEach((byte) => { binary += String.fromCharCode(byte); });
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
};

const buildBigTreeComparisonUrl = (selectedNewick, reference = null) => {
    const labels = [...new Set(collectLeafNames(parseNewick(selectedNewick))
        .map((name) => name.trim().replace(/\s+/g, "_")))];
    let highlightedLabels = labels;
    let referenceNewick = null;
    if (reference) {
        const root = parseNewick(reference.tree.newick);
        const selected = new Set(labels.map(normalizeTreeTaxon));
        const paths = [];
        let index = 0;
        const visit = (node, path) => {
            if (node.children?.length) node.name = `phyloDIVaS_internal_${index++}`;
            const next = [...path, node];
            if (!node.children?.length && selected.has(normalizeTreeTaxon(node.name))) paths.push(next);
            node.children?.forEach((child) => visit(child, next));
        };
        visit(root, []);
        if (!paths.length) throw new Error("None of the selected species occur in Big Tree Viewer's bundled reference tree.");
        let common = 0;
        while (paths.every((path) => path[common] && path[common] === paths[0][common])) common++;
        // A node's metadata colors its incoming edge. Exclude the MRCA's
        // incoming edge so highlighting stops precisely at the ancestor.
        highlightedLabels = [...new Set(paths.flatMap((path) => path.slice(common).map((node) => node.name)))];
        if (paths.length === 1) highlightedLabels = [paths[0].at(-1).name];
        const serialize = (node) => `${node.children?.length ? `(${node.children.map(serialize).join(",")})` : ""}'${String(node.name || "").replace(/'/g, "''")}':${node.branchLength || 0}`;
        referenceNewick = `${serialize(root)};`;
    }
    const metadataText = ["species\tselection\tmarker", ...highlightedLabels.map((label) =>
        `${label}\tSelected species\t${labels.some((name) => normalizeTreeTaxon(name) === normalizeTreeTaxon(label)) ? "Selected species" : ""}`
    )].join("\n");
    const session = {
        format: "big-tree-viewer-session",
        version: 1,
        savedAt: new Date().toISOString(),
        settings: {
            viewMode: "rectangular",
            order: "input",
            zoomAxisMode: "both",
            circularRotationDegrees: 0,
            spiralTurns: 5.5,
            showTimeStripes: false,
            timeStripeStyle: "bands",
            timeStripeLineWeight: 1.1,
            timeAxisScale: "linear",
            timeAxisLogBase: 4.2,
            showTipLabels: true,
            alignTipLabels: true,
            showScaleBars: true,
            showIntermediateScaleTicks: true,
            extendRectScaleToTick: false,
            showScaleZeroTick: false,
            scaleTickIntervalInput: "",
            useAutoCircularCenterScaleAngle: true,
            circularCenterScaleAngleDegrees: -5,
            showCircularCenterRadialScaleBar: false,
            showGenusLabels: false,
            showInternalNodeLabels: false,
            showBootstrapLabels: false,
            showNodeHeightLabels: false,
            showNodeErrorBars: false,
            errorBarThicknessPx: 1.2,
            errorBarCapSizePx: 7,
            figureStyles: {},
            taxonomyEnabled: false,
            taxonomyOverlayStyle: "ribbons",
            taxonomyRankVisibility: {},
            taxonomyRankDisplayModes: {},
            taxonomyCollapseRank: "species",
            useAutomaticTaxonomyRankVisibility: true,
            taxonomyBranchColoringEnabled: false,
            taxonomyColorJitter: 1,
            taxonomyColorPalette: "classic",
            taxonomyCustomPaletteInput: "",
            taxonomyColorRootRank: "auto",
            taxonomyColorJitterRank: "genus",
            phylopicEnabled: false,
            branchThicknessScale: 2,
            metadataEnabled: true,
            metadataFirstRowIsHeader: true,
            metadataKeyColumn: "species",
            metadataValueColumn: "selection",
            metadataLegendEnabled: true,
            metadataColorMode: "categorical",
            metadataApplyScope: "branch",
            metadataReverseScale: false,
            metadataContinuousPalette: "blueOrange",
            metadataContinuousTransform: "linear",
            metadataContinuousMinInput: "",
            metadataContinuousMaxInput: "",
            metadataLabelsEnabled: false,
            metadataLabelColumn: "",
            metadataMarkersEnabled: true,
            metadataMarkerColumn: "marker",
            metadataPiesEnabled: false,
            metadataPieStartColumn: "",
            metadataPieEndColumn: "",
            metadataPiePalette: "categorical",
            metadataPieColorOverrides: {},
            metadataPieSizePx: 50,
            metadataCategoryColorOverrides: { "Selected species": "#ff0000" },
            metadataMarkerStyleOverrides: { "Selected species": { color: "#ff0000", shape: "circle" } },
            metadataMarkerSizePx: 14,
            metadataLabelMaxCount: 240,
            metadataLabelMinSpacingPx: 10,
            metadataLabelOffsetXPx: 0,
            metadataLabelOffsetYPx: 0
        },
    };
    const launch = {
        ...(referenceNewick ? { newick: referenceNewick, label: "Selected species paths to MRCA" } : { sessionUrl: "https://bigtreeviewer.net/example_tree.btvsession" }),
        visual: session.settings,
        metadata: {
        text: metadataText,
        label: "phyloDIVaS selected species.tsv",
        firstRowIsHeader: true,
        enabled: true,
        keyColumn: "species",
        valueColumn: "selection",
        colorMode: "categorical",
        applyScope: "branch",
        labelsEnabled: false,
        markersEnabled: true,
        markerColumn: "marker",
        markerSizePx: 14,
        categoryColorOverrides: { "Selected species": "#ff0000" },
        markerStyleOverrides: { "Selected species": { color: "#ff0000", shape: "circle" } }
    } };
    if (reference) return launch;
    const payload = encodeBase64Url(JSON.stringify(launch));
    return `https://bigtreeviewer.net/?btv_payload=${payload}`;
};

const flattenEdges = (node) => {
    const edges = [];
    const walk = (n) => {
        if (!n.children) return;
        n.children.forEach((c) => {
            edges.push({ from: n, to: c });
            walk(c);
        });
    };
    walk(node);
    return edges;
};

const parseFilteredSingleCopy = (content) => {
    if (!content) return [];
    const lines = content.split(/\r?\n/).filter(Boolean);
    if (lines.length < 2) return [];
    const header = lines[0].split('\t');
    const metadataColumns = new Set(["hog", "orthogroup", "og", "gene tree parent clade"]);
    let speciesStart = 0;
    while (speciesStart < header.length && metadataColumns.has(header[speciesStart].trim().toLowerCase())) {
        speciesStart += 1;
    }
    const parentCladeIndex = header.findIndex((column) => column.trim().toLowerCase() === "gene tree parent clade");
    const speciesNames = header.slice(speciesStart);
    if (speciesNames.length === 0) return [];
    const groups = [];
    for (let i = 1; i < lines.length; i++) {
        const parts = lines[i].split('\t');
        if (parts.length < header.length) continue;
        if (parentCladeIndex >= 0 && (parts[parentCladeIndex] || "").trim().toLowerCase() !== "n0") continue;
        const groupId = parts[0].trim() || `og_${i}`;
        const cells = parts.slice(speciesStart, speciesStart + speciesNames.length);
        const speciesGenes = {};
        let valid = true;
        cells.forEach((cell, idx) => {
            const ids = (cell || "").split(',').map(s => s.trim()).filter(Boolean);
            if (ids.length !== 1 || ids[0] === "-" || ids[0] === "0") {
                valid = false;
                return;
            }
            speciesGenes[speciesNames[idx] || `species_${idx}`] = ids[0];
        });
        if (valid) {
            const geneIds = Object.values(speciesGenes)
                .flatMap(v => (v || "").split(','))
                .map(s => s.trim())
                .filter(Boolean);
            groups.push({
                groupId,
                groupName: groupId,
                geneIds,
                speciesGenes
            });
        }
    }
    return groups;
};

const extractWorkflowInvocationId = (results) => {
    if (!Array.isArray(results)) return null;
    for (const item of results) {
        const invocationId = item?.stepDatasets?.workflow_invocation || item?.stepDatasets?.workflow_invocation_id;
        if (invocationId) return invocationId;
    }
    return null;
};

const GenomeAnalysisPage = () => {
    const { historyId: routeHistoryId } = useParams();

    // Basic form state
    const [speciesQuery, setSpeciesQuery] = useState("");
    const [selectedSpecies, setSelectedSpecies] = useState([]);
    const [alignment, setAlignment] = useState("mafft");
    const [ncbiApiKey, setNcbiApiKey] = useState("");
    const [galaxyApiKey, setGalaxyApiKey] = useState("");
    const [showGalaxyApiKey, setShowGalaxyApiKey] = useState(false);
    const [showNcbiApiKey, setShowNcbiApiKey] = useState(false);
    const [showAdvancedOptions, setShowAdvancedOptions] = useState(false);
    const [galaxyKeyValidation, setGalaxyKeyValidation] = useState("idle");
    
    // Workflow state
    const [currentStep, setCurrentStep] = useState(WORKFLOW_STEPS.SETUP);
    const [genomeAnalysisData, setGenomeAnalysisData] = useState(null);
    const [orthologousGroups, setOrthologousGroups] = useState([]);
    const [selectedGroups, setSelectedGroups] = useState([]);
    const [submittedGroups, setSubmittedGroups] = useState([]);
    const [isCancelingAnalysis, setIsCancelingAnalysis] = useState(false);
    const [isRetryingHyphy, setIsRetryingHyphy] = useState(false);
    
    // Loading and status state
    const [isVerifyingSpecies, setIsVerifyingSpecies] = useState(false);
    const [isLoadingSpeciesSuggestions, setIsLoadingSpeciesSuggestions] = useState(false);
    const [speciesSuggestions, setSpeciesSuggestions] = useState([]);
    const [speciesSuggestionLimit, setSpeciesSuggestionLimit] = useState(25);
    const [queuedSpecies, setQueuedSpecies] = useState([]);
    const [speciesSort, setSpeciesSort] = useState("relevance");
    const [speciesSortDirection, setSpeciesSortDirection] = useState("desc");
    const [speciesDisplayMode, setSpeciesDisplayMode] = useState("list");
    const [inputTree, setInputTree] = useState(null);
    const [inputTreeLoading, setInputTreeLoading] = useState(false);
    const [inputTreeError, setInputTreeError] = useState(null);
    const [isStartingGenomeAnalysis, setIsStartingGenomeAnalysis] = useState(false);
    const [isLoadingGroups, setIsLoadingGroups] = useState(false);
    const [isAnalyzing, setIsAnalyzing] = useState(false);
    const [statusMessage, setStatusMessage] = useState("Add at least 3 species to begin analysis.");
    const [statusProgress, setStatusProgress] = useState(0);
    const [errorMessage, setErrorMessage] = useState(null);
    const [activeHistoryId, setActiveHistoryId] = useState(null);
    const [orthoOutputs, setOrthoOutputs] = useState(null);
    const [speciesTreeData, setSpeciesTreeData] = useState(null);
    const [speciesTreeTimeData, setSpeciesTreeTimeData] = useState(null);
    const [speciesTreeScaleMode, setSpeciesTreeScaleMode] = useState("distance");
    const [showSpeciesIcons, setShowSpeciesIcons] = useState(true);
    const [speciesIcons, setSpeciesIcons] = useState({});
    useEffect(() => {
        if (!showSpeciesIcons || !speciesTreeData) return;
        let cancelled = false;
        const names = collectLeafNames(speciesTreeData).map((name) => name.replace(/_protein\.faa$/i, "").replace(/_/g, " "));
        let cursor = 0;
        const worker = async () => {
            while (cursor < names.length) {
                const name = names[cursor++];
                try {
                    const icon = await getSpeciesSilhouette(name);
                    if (!cancelled) setSpeciesIcons((previous) => ({ ...previous, [name]: icon }));
                } catch { /* Optional icons never block the phylogeny. */ }
            }
        };
        void Promise.all(Array.from({ length: Math.min(3, names.length) }, worker));
        return () => { cancelled = true; };
    }, [speciesTreeData, showSpeciesIcons]);
    const [speciesTreeNewick, setSpeciesTreeNewick] = useState(null);
    const [contextTreeLoading, setContextTreeLoading] = useState(false);
    const [contextTreeError, setContextTreeError] = useState("");
    const [phylotreeReady, setPhylotreeReady] = useState(false);
    const [phylotreeFailed, setPhylotreeFailed] = useState(false);
    const treeContainerRef = useRef(null);
    const galaxyStatusRefreshInFlightRef = useRef(false);
    const analysisSubmissionInFlightRef = useRef(false);
    const resumeInFlightRef = useRef(false);
    const resumeRetryTimerRef = useRef(null);
    const resumeRetryCountRef = useRef(0);
    const hyphyResultsHydratedRef = useRef(false);
    const [outputsFetched, setOutputsFetched] = useState(false);
    
    // Job tracking
    const [currentJobInfo, setCurrentJobInfo] = useState(null);
    const [stopPolling, setStopPolling] = useState(null);

    // Workflow mode
    const [workflowMode, setWorkflowMode] = useState(() =>
        new URLSearchParams(window.location.search).get("mode") === "resume"
            ? WORKFLOW_MODES.RESUME : WORKFLOW_MODES.NEW);
    const [autoResumeAttempted, setAutoResumeAttempted] = useState(false);

    // Resume existing history
    const [resumeHistoryId, setResumeHistoryId] = useState("");
    const [resumeGalaxyApiKey, setResumeGalaxyApiKey] = useState("");
    const [showResumeGalaxyApiKey, setShowResumeGalaxyApiKey] = useState(false);
    const [showGalaxyCredentialHelp, setShowGalaxyCredentialHelp] = useState(false);
    const [liveGalaxyStatus, setLiveGalaxyStatus] = useState(null);
    const [galaxyStatusWarning, setGalaxyStatusWarning] = useState(null);
    const [isRefreshingGalaxyStatus, setIsRefreshingGalaxyStatus] = useState(false);
    const [isContinuingPreparation, setIsContinuingPreparation] = useState(false);
    const [autoRefreshGalaxyStatus, setAutoRefreshGalaxyStatus] = useState(true);
    const [showRunningOrthoResults, setShowRunningOrthoResults] = useState(false);
    const [orthoBrowseError, setOrthoBrowseError] = useState(null);

    useEffect(() => {
        return () => {
            stopPolling?.();
            if (resumeRetryTimerRef.current) window.clearTimeout(resumeRetryTimerRef.current);
        };
    }, [stopPolling]);

    // Dynamically load d3 and phylotree for interactive species tree rendering
    useEffect(() => {
        let isMounted = true;

        const loadScript = (src) =>
            new Promise((resolve, reject) => {
                const existing = document.querySelector(`script[src="${src}"]`);
                if (existing) {
                    resolve();
                    return;
                }
                const script = document.createElement("script");
                script.src = src;
                script.async = true;
                script.crossOrigin = "anonymous";
                script.onload = resolve;
                script.onerror = reject;
                document.body.appendChild(script);
            });

        (async () => {
            try {
                // Disable phylotree dynamic load to avoid runtime errors; fall back to static SVG renderer
                if (isMounted) {
                    setPhylotreeReady(false);
                    setPhylotreeFailed(true);
                }
            } catch (err) {
                console.error("Failed to load phylotree dependencies", err);
                if (isMounted) {
                    setPhylotreeReady(false);
                    setPhylotreeFailed(true);
                }
            }
        })();

        return () => {
            isMounted = false;
        };
    }, []);

    useEffect(() => {
        const query = speciesQuery.trim();
        const isSingleQuery = query && query.split(/[\n,;]+/).length === 1;

        if (!isSingleQuery || query.length < 2) {
            setSpeciesSuggestions([]);
            setIsLoadingSpeciesSuggestions(false);
            return;
        }

        let canceled = false;
        setIsLoadingSpeciesSuggestions(true);

        const timerId = setTimeout(async () => {
            try {
                const dynamicSuggestions = await suggestSpecies({
                    query,
                    ncbiApiKey: ncbiApiKey.trim() || undefined,
                    limit: speciesSuggestionLimit
                });
                if (canceled) return;

                setSpeciesSuggestions(
                    dynamicSuggestions.filter((s) => s.hasAnnotation && !selectedSpecies.includes(s.scientific))
                );
            } catch (err) {
                if (canceled) return;
                console.warn("NCBI species suggestions unavailable.", err);
                setSpeciesSuggestions([]);
            } finally {
                if (!canceled) setIsLoadingSpeciesSuggestions(false);
            }
        }, 300);

        return () => {
            canceled = true;
            clearTimeout(timerId);
        };
    }, [speciesQuery, selectedSpecies, ncbiApiKey, speciesSuggestionLimit]);

    const sortedSpeciesSuggestions = useMemo(() => {
        const assemblyRank = { "complete genome": 4, chromosome: 3, scaffold: 2, contig: 1 };
        const qualityScore = (item) => {
            const busco = Number(item.buscoComplete);
            const n50 = Number(item.scaffoldN50 || item.contigN50 || 0);
            return (Number.isFinite(busco) ? busco * 1e12 : 0) + n50;
        };
        const publicationTime = (item) => {
            const value = Date.parse(item.releaseDate || item.submissionDate || "");
            return Number.isNaN(value) ? 0 : value;
        };
        const indexed = speciesSuggestions.map((item, index) => ({ item, index }));
        indexed.sort((left, right) => {
            let result = 0;
            if (speciesSort === "quality") result = qualityScore(left.item) - qualityScore(right.item);
            else if (speciesSort === "alphabetical") result = left.item.scientific.localeCompare(right.item.scientific);
            else if (speciesSort === "publication") result = publicationTime(left.item) - publicationTime(right.item);
            else if (speciesSort === "assembly") {
                result = (assemblyRank[left.item.assemblyLevel?.toLowerCase()] || 0) - (assemblyRank[right.item.assemblyLevel?.toLowerCase()] || 0);
            } else result = right.index - left.index;
            return speciesSortDirection === "asc" ? result : -result;
        });
        return indexed.map(({ item }) => item);
    }, [speciesSuggestions, speciesSort, speciesSortDirection]);

    useEffect(() => {
        if (selectedSpecies.length < 2) {
            setInputTree(null);
            setInputTreeError(null);
            setInputTreeLoading(false);
            return;
        }
        let cancelled = false;
        setInputTreeLoading(true);
        setInputTreeError(null);
        const timer = setTimeout(() => {
            getReferenceSpeciesTree(selectedSpecies, ncbiApiKey.trim() || undefined)
                .then(async (tree) => {
                    if (cancelled) return;
                    const parsed = parseNewick(tree?.newick);
                    if (!parsed) setInputTreeError("The preview tree could not be generated.");
                    if (parsed) setInputTree({ ...tree, layout: layoutTree(parsed) });
                    if (parsed && tree?.leafTaxIds) {
                        let divergenceTimes = tree.divergenceTimes || [];
                        if (divergenceTimes.length === 0) {
                            try {
                                divergenceTimes = await getSpeciesTreeTimes(tree.newick, tree.leafTaxIds);
                            } catch (error) {
                                console.warn("TimeTree estimates unavailable.", error);
                            }
                        }
                        if (cancelled) return;
                        annotateDivergenceTimes(parsed, divergenceTimes);
                        setInputTree({
                            ...tree,
                            divergenceTimes,
                            layout: layoutTreeByTime(parsed) || layoutTree(parsed)
                        });
                    }
                })
                .catch((error) => { if (!cancelled) setInputTreeError(error?.message || "The preview tree could not be generated."); })
                .finally(() => { if (!cancelled) setInputTreeLoading(false); });
        }, 350);
        return () => { cancelled = true; clearTimeout(timer); };
    }, [selectedSpecies, ncbiApiKey]);

    const renderInputTree = useCallback(() => {
        if (!inputTree?.layout) return null;
        const nodes = [];
        const collect = (node) => { nodes.push(node); (node.children || []).forEach(collect); };
        collect(inputTree.layout);
        const leaves = nodes.filter((node) => !node.children?.length);
        const longestLabel = leaves.reduce((longest, node) => {
            const label = String(node.name || "").replace(/_/g, " ").replace(/^'|'$/g, "");
            return label.length > longest.length ? label : longest;
        }, "");
        const leftPadding = 32;
        const labelGap = 16;
        const labelWidth = Math.max(170, longestLabel.length * 8.2);
        const width = Math.max(620, Math.max(...nodes.map((node) => node.x)) + leftPadding + labelGap + labelWidth + 24);
        const nodeOffsetY = inputTree.layout.layoutMeta?.isTimeScale ? 52 : 28;
        const height = Math.max(180, Math.max(...nodes.map((node) => node.y)) + nodeOffsetY + 42);
        const timeScale = inputTree.layout.layoutMeta?.isTimeScale ? inputTree.layout.layoutMeta : null;
        return <div className={parametersStyles.inputTreeScroller}>
            <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} className={parametersStyles.inputTreeSvg} role="img" aria-label="Reference tree for selected species">
                {timeScale && <>
                    <line x1={leftPadding} y1="12" x2={timeScale.targetWidth + leftPadding} y2="12" className={parametersStyles.inputTreeTimeAxis} />
                    {Array.from({ length: 5 }, (_, index) => {
                        const fraction = index / 4;
                        const x = leftPadding + timeScale.targetWidth * fraction;
                        const age = timeScale.maxAgeMa * (1 - fraction);
                        return <g key={`input-time-${index}`}>
                            <line x1={x} y1="8" x2={x} y2="16" className={parametersStyles.inputTreeTimeAxis} />
                            <text x={x} y="27" textAnchor="middle" className={parametersStyles.inputTreeTime}>{age.toFixed(age >= 10 ? 0 : 1)} Ma</text>
                        </g>;
                    })}
                </>}
                {nodes.filter((node) => node.children?.length).map((node, index) => {
                    const childYs = node.children.map((child) => child.y + nodeOffsetY);
                    return <g key={`input-branch-${index}`}>
                        <line
                            x1={node.x + leftPadding}
                            y1={Math.min(...childYs)}
                            x2={node.x + leftPadding}
                            y2={Math.max(...childYs)}
                            className={parametersStyles.inputTreeEdge}
                        />
                        {node.children.map((child, childIndex) => <line
                            key={childIndex}
                            x1={node.x + leftPadding}
                            y1={child.y + nodeOffsetY}
                            x2={child.x + leftPadding}
                            y2={child.y + nodeOffsetY}
                            className={parametersStyles.inputTreeEdge}
                        />)}
                    </g>;
                })}
                {nodes.map((node, index) => <g key={index}>
                    {node.name && !node.children?.length && <text x={node.x + leftPadding + labelGap} y={node.y + nodeOffsetY + 5} className={parametersStyles.inputTreeLabel}>{node.name.replace(/_/g, " ").replace(/^'|'$/g, "")}</text>}
                </g>)}
            </svg>
        </div>;
    }, [inputTree]);

    const inputTreeMaxDivergenceMa = useMemo(() => {
        const reported = Number(inputTree?.maxDivergenceMa);
        if (Number.isFinite(reported) && reported > 0) return reported;
        return Math.max(0, ...(inputTree?.divergenceTimes || []).map((value) => Number(value.ageMa) || 0));
    }, [inputTree]);

    const dsSaturationWarningMa = Number(inputTree?.saturationWarningThresholdMa)
        || DEFAULT_DS_SATURATION_WARNING_MA;

    const handleOpenTreeOfLifeComparison = useCallback(async () => {
        if (!inputTree?.newick || contextTreeLoading) return;
        setContextTreeError("");
        const comparisonWindow = window.open("about:blank", "_blank");
        if (!comparisonWindow) {
            setContextTreeError("Your browser blocked the comparison window. Allow pop-ups for localhost and try again.");
            return;
        }
        comparisonWindow.opener = null;
        comparisonWindow.document.body.textContent = "Preparing selected species paths to their most recent common ancestor...";
        setContextTreeLoading(true);
        try {
            const response = await fetch("/bigtree-reference.btvsession");
            if (!response.ok) throw new Error("The bundled reference tree could not be loaded.");
            const bytes = new Uint8Array(await response.arrayBuffer());
            const blob = new Blob([bytes]);
            const text = bytes[0] === 31 && bytes[1] === 139
                ? await new Response(blob.stream().pipeThrough(new DecompressionStream("gzip"))).text()
                : await blob.text();
            const payload = buildBigTreeComparisonUrl(inputTree.newick, JSON.parse(text));
            await new Promise((resolve, reject) => {
                const cleanup = () => { window.removeEventListener("message", receive); window.clearTimeout(timer); };
                const receive = (event) => {
                    if (event.origin !== "https://bigtreeviewer.net" || event.source !== comparisonWindow) return;
                    if (event.data?.type === "big-tree-viewer:ready") comparisonWindow.postMessage({ type: "big-tree-viewer:load", payload }, event.origin);
                    if (event.data?.type === "big-tree-viewer:loaded") { cleanup(); resolve(); }
                    if (event.data?.type === "big-tree-viewer:error") { cleanup(); reject(new Error(event.data.message || "Big Tree Viewer could not load the highlighted tree.")); }
                };
                const timer = window.setTimeout(() => { cleanup(); reject(new Error("Big Tree Viewer did not confirm loading. Please try again.")); }, 120000);
                window.addEventListener("message", receive);
                comparisonWindow.location.replace("https://bigtreeviewer.net/?btv_api=1");
            });
        } catch (error) {
            setContextTreeError(error?.message || "The highlighted tree could not be opened.");
        } finally {
            setContextTreeLoading(false);
        }
    }, [contextTreeLoading, inputTree, ncbiApiKey, selectedSpecies]);
    const handleSpeciesSortChange = useCallback((event) => {
        const value = event.target.value;
        setSpeciesSort(value);
        setSpeciesSortDirection(value === "alphabetical" ? "asc" : "desc");
    }, []);

    const analysisSpecies = useMemo(() => {
        const fromMetadata = genomeAnalysisData?.genomes?.map((g) => g.species).filter(Boolean) ?? [];
        if (fromMetadata.length > 0) {
            return Array.from(new Set(fromMetadata));
        }
        return selectedSpecies;
    }, [genomeAnalysisData, selectedSpecies]);

    const normalizeOutputItem = (o) => {
        if (!o) return null;
        const label = o.label ?? o.Label ?? "";
        const datasetId = o.datasetId ?? o.DatasetId ?? "";
        if (!label || !datasetId) return null;
        return {
            label,
            datasetId,
            description: o.description ?? o.Description ?? "",
            suggestedExtension: o.suggestedExtension ?? o.SuggestedExtension
        };
    };

const renderSpeciesTree = useCallback(() => {
        const displayedTree = speciesTreeScaleMode === "time" && speciesTreeTimeData
            ? speciesTreeTimeData
            : speciesTreeData;
        if (!displayedTree) return null;

        // Use one deterministic renderer so the tree remains rectangular and readable.
        const allNodes = [];
        const collect = (n) => {
            allNodes.push(n);
            (n.children || []).forEach(collect);
        };
        collect(displayedTree);
        const leftPadding = 36;
        const isTimeScale = displayedTree.layoutMeta?.isTimeScale;
        const topPadding = isTimeScale ? 64 : 34;
        const labelGap = 14;
        const longestLabel = allNodes.filter((node) => !node.children?.length).reduce((longest, node) => {
            const label = (node.name || "").replace(/_protein\.faa$/i, "").replace(/_/g, " ");
            return label.length > longest.length ? label : longest;
        }, "");
        const treeWidth = Math.max(...allNodes.map((n) => n.x));
        const labelWidth = Math.max(180, longestLabel.length * 8.5);
        const iconSpace = showSpeciesIcons ? 44 : 0;
        const width = Math.ceil(leftPadding + treeWidth + labelGap + labelWidth + 30 + iconSpace);
        const height = Math.max(...allNodes.map((n) => n.y)) + 100;
        const scaleMeta = displayedTree.layoutMeta;
        const scaleDistance = scaleMeta?.hasBranchLengths && scaleMeta.maxDistance > 0
            ? scaleMeta.maxDistance / 5
            : null;
        const scalePixels = scaleDistance ? scaleDistance * scaleMeta.distanceScale : 0;

        return (
            <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} className={analysisPageStyles.treeSvg} role="img" aria-label="OrthoFinder species tree with branch lengths drawn to scale">
                {allNodes.filter((node) => node.children?.length).map((node, idx) => {
                    const childYs = node.children.map((child) => child.y + topPadding);
                    return <g key={`edge-${idx}`}>
                        <line x1={node.x + leftPadding} y1={Math.min(...childYs)} x2={node.x + leftPadding} y2={Math.max(...childYs)} className={analysisPageStyles.treeBranch} />
                        {node.children.map((child, childIndex) => <g key={childIndex}>
                            <line x1={node.x + leftPadding} y1={child.y + topPadding} x2={child.x + leftPadding} y2={child.y + topPadding} className={analysisPageStyles.treeBranch} />
                            {Number(child.branchLength) > 0 && (
                            <text
                                x={(node.x + child.x) / 2 + leftPadding}
                                y={child.y + topPadding - 7}
                                textAnchor="middle"
                                className={analysisPageStyles.treeBranchLabel}
                            >
                                {Number(child.branchLength).toPrecision(3)}
                            </text>
                            )}
                        </g>)}
                    </g>;
                })}
                {allNodes.map((n, idx) => (
                    <g key={`node-${idx}`}>
                        {n.name && !n.children?.length && (
                            <text x={n.x + leftPadding + labelGap + iconSpace} y={n.y + topPadding + 5} className={analysisPageStyles.treeLabel}>
                                <title>{n.name.replace(/_protein\.faa$/i, "").replace(/_/g, " ")}</title>
                                {n.name.replace(/_protein\.faa$/i, "").replace(/_/g, " ")}
                            </text>
                        )}
                        {showSpeciesIcons && !n.children?.length && speciesIcons[n.name?.replace(/_protein\.faa$/i, "").replace(/_/g, " ")] && (
                            <image href={speciesIcons[n.name.replace(/_protein\.faa$/i, "").replace(/_/g, " ")].url}
                                x={n.x + leftPadding + labelGap} y={n.y + topPadding - 17} width="36" height="34" preserveAspectRatio="xMidYMid meet">
                                <title>{speciesIcons[n.name.replace(/_protein\.faa$/i, "").replace(/_/g, " ")].taxon} silhouette</title>
                            </image>
                        )}
                    </g>
                ))}
                {isTimeScale && Array.from({ length: 6 }, (_, index) => {
                    const fraction = index / 5;
                    const x = leftPadding + fraction * scaleMeta.targetWidth;
                    const age = scaleMeta.maxAgeMa * (1 - fraction);
                    return (
                        <g key={`time-tick-${index}`} className={analysisPageStyles.treeTimeAxis}>
                            <line x1={x} y1="29" x2={x} y2="37" />
                            <text x={x} y="20" textAnchor="middle">{age.toFixed(age >= 10 ? 0 : 1)} Ma</text>
                        </g>
                    );
                })}
                {isTimeScale && <line x1={leftPadding} y1="33" x2={leftPadding + scaleMeta.targetWidth} y2="33" className={analysisPageStyles.treeTimeAxisLine} />}
                {scaleDistance && (
                    <g className={analysisPageStyles.treeScale} transform={`translate(${leftPadding}, ${height - 25})`}>
                        <line x1="0" y1="0" x2={scalePixels} y2="0" />
                        <line x1="0" y1="-4" x2="0" y2="4" />
                        <line x1={scalePixels} y1="-4" x2={scalePixels} y2="4" />
                        <text x={scalePixels / 2} y="17" textAnchor="middle">{scaleDistance.toPrecision(2)} substitutions/site</text>
                    </g>
                )}
            </svg>
        );
    }, [speciesTreeData, speciesTreeTimeData, speciesTreeScaleMode, showSpeciesIcons, speciesIcons]);

    const verifySpecies = useCallback(async (speciesName) => {
        try {
            const matches = await suggestSpecies({
                query: speciesName,
                ncbiApiKey: ncbiApiKey.trim() || undefined,
                limit: 10
            });
            const normalized = speciesName.trim().toLowerCase();
            const match = matches.find((candidate) =>
                candidate.hasAnnotation &&
                (candidate.scientific.toLowerCase() === normalized || candidate.common.toLowerCase() === normalized)
            );
            return match?.scientific || null;
        } catch (err) {
            console.error("verifySpecies error:", err);
            throw err;
        }
    }, [ncbiApiKey]);

    const normalizeSpeciesList = useCallback((text) => {
        return Array.from(
            new Set(
                text
                    .split(/[\n,;]+/)
                    .map((s) => s.trim())
                    .filter(Boolean)
            )
        );
    }, []);

    const handleAddSpeciesSubmit = useCallback(async (e) => {
        e.preventDefault();
        const typedSpecies = normalizeSpeciesList(speciesQuery);
        const isSingleSearchTerm = typedSpecies.length === 1 && !/[\n,;]/.test(speciesQuery);
        const submittedValues = queuedSpecies.length > 0 && isSingleSearchTerm
            ? queuedSpecies
            : [...queuedSpecies, ...typedSpecies];
        const list = normalizeSpeciesList(submittedValues.join(","));
        if (list.length === 0) return;
        const existingSpecies = new Set(selectedSpecies.map((species) => species.toLowerCase()));
        const newSpecies = list.filter((species) => !existingSpecies.has(species.toLowerCase()));
        const remainingSlots = MAX_SPECIES_COUNT - selectedSpecies.length;
        if (newSpecies.length > remainingSlots) {
            setErrorMessage(
                `A Galaxy analysis supports ${MIN_SPECIES_COUNT}-${MAX_SPECIES_COUNT} species. ` +
                `You can add ${Math.max(0, remainingSlots)} more.`
            );
            return;
        }
        setIsVerifyingSpecies(true);
        setErrorMessage(null);
        setStatusMessage("Verifying species...");

        const valid = [];
        const invalid = [];
        const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

        for (let i = 0; i < newSpecies.length; i++) {
            const sp = newSpecies[i];
            try {
                const scientificName = await verifySpecies(sp);
                if (scientificName) valid.push(scientificName);
                else invalid.push(sp);
            } catch (err) {
                invalid.push(sp);
            }

            if ((i + 1) % 3 === 0 && i !== newSpecies.length - 1) {
                await pause(1000);
            }
        }

        const uniqueValid = Array.from(new Set(valid));
        setSelectedSpecies((prev) => {
            const existing = new Map(prev.map((species) => [species.toLowerCase(), species]));
            uniqueValid.forEach((species) => existing.set(species.toLowerCase(), species));
            return Array.from(existing.values());
        });

        if (uniqueValid.length > 0) {
            setStatusMessage(`Added ${uniqueValid.length} species: ${uniqueValid.join(", ")}`);
        }
        if (invalid.length > 0) {
            setErrorMessage(`These species could not be verified: ${invalid.join(", ")}`);
        }

        setSpeciesQuery("");
        setQueuedSpecies([]);
        setIsVerifyingSpecies(false);
    }, [speciesQuery, queuedSpecies, selectedSpecies, normalizeSpeciesList, verifySpecies]);

    // Handle suggestion click
    const handleSuggestionSelect = useCallback((scientificName) => {
        setQueuedSpecies((current) => current.some((name) => name === scientificName)
            ? current.filter((name) => name !== scientificName)
            : [...current, scientificName]);
    }, []);

    // Handle removing species
    const handleRemoveSpecies = useCallback((speciesName) => {
        setSelectedSpecies((prev) => prev.filter((s) => s !== speciesName));
    }, []);

    // Enable "Start Analysis" button
    const canStartGenomeAnalysis = useMemo(() => {
        return selectedSpecies.length >= MIN_SPECIES_COUNT
            && selectedSpecies.length <= MAX_SPECIES_COUNT
            && galaxyKeyValidation === "valid";
    }, [selectedSpecies, galaxyKeyValidation]);

    const handleValidateGalaxyKey = useCallback(async () => {
        const key = galaxyApiKey.trim();
        if (!key) {
            setGalaxyKeyValidation("invalid");
            setErrorMessage("Enter your Galaxy Europe API key before starting the analysis.");
            return false;
        }
        setGalaxyKeyValidation("checking");
        setErrorMessage(null);
        try {
            await validateGalaxyApiKey(key);
            setGalaxyKeyValidation("valid");
            return true;
        } catch (error) {
            setGalaxyKeyValidation("invalid");
            setErrorMessage(error?.message || "Galaxy rejected this API key.");
            return false;
        }
    }, [galaxyApiKey]);

   
    // Define before first use to avoid Temporal Dead Zone errors
    const loadOrthologousGroups = useCallback(async (historyId, orthoFinderJobId, options = {}) => {
        const quiet = options.quiet === true;
        setIsLoadingGroups(true);
        if (!quiet) setStatusMessage("Loading orthologous groups...");
        if (quiet) setOrthoBrowseError(null);
        try {
            const sessionGalaxyApiKey = galaxyApiKey.trim() || resumeGalaxyApiKey.trim() || undefined;
            // Prefer filtered single-copy output from OrthoFinder
            const outputs = await getOrthoFinderOutputs(historyId, orthoFinderJobId, sessionGalaxyApiKey);
            if (outputs) {
                setOrthoOutputs(outputs);
                setOutputsFetched(true);
            }
            let groups = [];
            if (outputs?.singleCopyDatasetId) {
                const content = await downloadOrthoFinderOutput(outputs.singleCopyDatasetId, {
                    galaxyApiKey: galaxyApiKey.trim() || resumeGalaxyApiKey.trim() || undefined
                });
                groups = parseFilteredSingleCopy(content);
            }

            // Fallback to API groups if parsing failed
            if (!groups || groups.length === 0) {
                groups = await getOrthologousGroups(historyId, orthoFinderJobId, sessionGalaxyApiKey);
            }

            setOrthologousGroups(groups || []);
            if (!quiet) {
                setStatusMessage(`Found ${(groups || []).length} orthologous groups. Select groups to analyze.`);
                setStatusProgress(100);
            }
            return outputs;
        } catch (e) {
            const msg = e?.message || String(e);
            if (quiet) setOrthoBrowseError(`Unable to load OrthoFinder groups: ${msg}`);
            else {
                setErrorMessage(`Error loading orthologous groups: ${msg}`);
                setStatusMessage(`Error loading groups: ${msg}`);
            }
            return null;
        } finally {
            setIsLoadingGroups(false);
        }
    }, [galaxyApiKey, resumeGalaxyApiKey]);

    const fetchOrthoFinderOutputs = useCallback(async (historyId, orthoFinderJobId, options = {}) => {
        try {
            const data = options.data || await getOrthoFinderOutputs(
                    historyId,
                    orthoFinderJobId,
                    galaxyApiKey.trim() || resumeGalaxyApiKey.trim() || undefined
                );
            setOrthoOutputs(data);
            setOutputsFetched(true);

            if (data?.speciesTreeNewick) {
                setPhylotreeFailed(false);
                setSpeciesTreeNewick(data.speciesTreeNewick);
                const parsed = parseNewick(data.speciesTreeNewick);
                if (parsed) {
                    setSpeciesTreeData(layoutTree(parsed));
                    setSpeciesTreeTimeData(null);

                    if (analysisSpecies.length >= 2) {
                        try {
                            const reference = await getReferenceSpeciesTree(
                                analysisSpecies,
                                ncbiApiKey.trim() || undefined
                            );
                            const taxIdsByName = new Map(
                                Object.entries(reference?.leafTaxIds || {}).map(([name, taxId]) => [normalizeTreeTaxon(name), taxId])
                            );
                            const orthoLeafTaxIds = Object.fromEntries(
                                collectLeafNames(parsed)
                                    .map((leafName) => [leafName, taxIdsByName.get(normalizeTreeTaxon(leafName))])
                                    .filter(([, taxId]) => Boolean(taxId))
                            );
                            if (Object.keys(orthoLeafTaxIds).length >= 2) {
                                const divergenceTimes = await getSpeciesTreeTimes(data.speciesTreeNewick, orthoLeafTaxIds);
                                const dated = parseNewick(data.speciesTreeNewick);
                                annotateDivergenceTimes(dated, divergenceTimes);
                                setSpeciesTreeData(layoutTree(dated));
                                setSpeciesTreeTimeData(layoutTreeByTime(dated));
                            }
                        } catch (timeError) {
                            console.warn("TimeTree estimates unavailable for the OrthoFinder species tree.", timeError);
                        }
                    }
                }
            } else {
                setSpeciesTreeNewick(null);
                setSpeciesTreeData(null);
                setSpeciesTreeTimeData(null);
                setSpeciesTreeScaleMode("distance");
            }
        } catch (e) {
            const msg = e?.message || String(e);
            if (options.quiet) setOrthoBrowseError(`Unable to load OrthoFinder outputs: ${msg}`);
            else setErrorMessage(`Unable to load OrthoFinder outputs: ${msg}`);
        }
    }, [galaxyApiKey, resumeGalaxyApiKey, analysisSpecies, ncbiApiKey]);

    const refreshHyphyOutputs = useCallback(async (historyId) => {
        try {
            const sessionGalaxyApiKey = galaxyApiKey.trim() || resumeGalaxyApiKey.trim() || undefined;
            const resp = await getHyphyOutputs(historyId, sessionGalaxyApiKey);
            if (resp?.hyphyMarkdownOutputs || resp?.hyphyJsonOutputs) {
                setCurrentJobInfo((prev) => ({
                    historyId: historyId,
                    ...(prev || {}),
                    galaxyApiKey: sessionGalaxyApiKey || prev?.galaxyApiKey,
                    ...(resp.hyphyMarkdownOutputs ? { hyphyMarkdownOutputs: resp.hyphyMarkdownOutputs } : {}),
                    ...(resp.hyphyJsonOutputs ? { hyphyJsonOutputs: resp.hyphyJsonOutputs } : {}),
                    hyphyGeneCount: Number.isFinite(resp.geneCount) ? resp.geneCount : prev?.hyphyGeneCount,
                    hyphyMethodCounts: resp.methodCounts || prev?.hyphyMethodCounts,
                    hyphyCollectionCountsMatch: resp.collectionCountsMatch ?? prev?.hyphyCollectionCountsMatch
                }));
            }
        } catch (err) {
            console.warn("Unable to refresh HYPHY outputs", err);
        }
    }, [galaxyApiKey, resumeGalaxyApiKey]);
    const handleStartGenomeAnalysis = useCallback(async () => {
        setErrorMessage(null);
        if (galaxyKeyValidation !== "valid" || !galaxyApiKey.trim()) {
            const valid = await handleValidateGalaxyKey();
            if (!valid) return;
        }
        if (selectedSpecies.length < MIN_SPECIES_COUNT) {
            setErrorMessage(`Select at least ${MIN_SPECIES_COUNT} species.`);
            return;
        }
        if (selectedSpecies.length > MAX_SPECIES_COUNT) {
            setErrorMessage(`Select no more than ${MAX_SPECIES_COUNT} species.`);
            return;
        }

        stopPolling?.();
        setStopPolling(null);
        setCurrentJobInfo(null);
        setActiveHistoryId(null);
        setOrthoOutputs(null);
        setSpeciesTreeData(null);
        setSpeciesTreeTimeData(null);
        setSpeciesTreeScaleMode("distance");
        setSpeciesTreeNewick(null);
        setOutputsFetched(false);
        setIsStartingGenomeAnalysis(true);
        setStatusProgress(0);
        setStatusMessage("Initializing genome analysis...");
        setCurrentStep(WORKFLOW_STEPS.GENOME_ANALYSIS);

        try {
            const sessionGalaxyApiKey = galaxyApiKey.trim() || undefined;
            const resp = await startGenomeAnalysis({
                species: selectedSpecies,
                ncbiApiKey: ncbiApiKey.trim() || undefined,
                galaxyApiKey: sessionGalaxyApiKey
            });
            
            const { sessionId } = resp;
            if (!sessionId) {
                throw new Error("Invalid API response: missing session ID.");
            }
            saveSession({ sessionId, species: selectedSpecies });

            // Subscribe to progress updates via SSE
            const unsubscribe = subscribeToGenomeAnalysisProgress(sessionId, {
                onProgress: (update) => {
                    console.log("Progress update received:", update);
                    const { step, message, percentage, details } = update;
                    
                    if (details?.historyId) {
                        setActiveHistoryId(details.historyId);
                        saveSession({ historyId: details.historyId });
                    }
                    
                    // Use functional updates to ensure state is set correctly
                    setStatusMessage(prev => {
                        const newMsg = message || step;
                        console.log("Updating status message from", prev, "to", newMsg);
                        return newMsg;
                    });
                    
                    if (percentage !== undefined && percentage !== null) {
                        setStatusProgress(prev => {
                            console.log("Updating progress from", prev, "to", percentage);
                            return percentage;
                        });
                    }

                    // When analysis is started, we'll have the final data
                    if (step === "Analysis Started" && update.details) {
                        const { historyId, orthoFinderJobId, genomes } = update.details;
                        if (historyId && orthoFinderJobId && genomes) {
                            // Close the SSE stream now that we have the job info to avoid a reconnection error when the server completes the setup.
                            try { unsubscribe?.(); } catch (e) { console.warn("Failed to unsubscribe SSE", e); }
                            setGenomeAnalysisData({ historyId, genomes, orthoFinderJobId });
                            saveSession({ historyId, orthoFinderJobId, genomes });
                            setCurrentJobInfo({ jobId: orthoFinderJobId, state: "running", galaxyApiKey: sessionGalaxyApiKey });
                            
                            // Start polling the OrthoFinder job
                            const pollCancel = pollJob(orthoFinderJobId, {
                                intervalMs: 8000,
                                galaxyApiKey: sessionGalaxyApiKey,
                                onTick: (job) => {
                                    setCurrentJobInfo((prev) => ({ ...(prev || {}), ...job, galaxyApiKey: sessionGalaxyApiKey }));
                                    const state = job?.state || "unknown";
                                    
                                    if (state === "ok") {
                    setStatusMessage("OrthoFinder complete. Launching Galaxy workflow for orthogroup multifastas...");
                    setStatusProgress(100);
                    setIsStartingGenomeAnalysis(false);
                    setCurrentStep(WORKFLOW_STEPS.RUNNING_ANALYSIS);
                    if (!outputsFetched) {
                        fetchOrthoFinderOutputs(historyId, orthoFinderJobId);
                    }
                    // Trigger downstream workflow via backend orthogroup load
                    loadOrthologousGroups(historyId, orthoFinderJobId);
                } else if (state === "error" || state === "failed") {
                                        setStatusMessage(`OrthoFinder failed: ${state}`);
                                        setErrorMessage(`OrthoFinder analysis failed with state: ${state}`);
                                        setIsStartingGenomeAnalysis(false);
                                    } else {
                                        setStatusMessage(`OrthoFinder: ${state}`);
                                    }
                                },
                                onError: (err) => {
                                    const msg = err?.message || String(err);
                                    setErrorMessage(msg);
                                    setStatusMessage(`Error polling job: ${msg}`);
                                    setIsStartingGenomeAnalysis(false);
                                },
                            });
                            setStopPolling(() => pollCancel);
                        }
                    }
                },
                onError: (error) => {
                    console.error("SSE error:", error);
                    const msg = error?.message || "Connection error";
                    setErrorMessage(`Progress stream error: ${msg}`);
                    setStatusMessage(`Error: ${msg}`);
                    setIsStartingGenomeAnalysis(false);
                }
            });

            // Store the unsubscribe function to clean up on unmount
            setStopPolling(() => unsubscribe);

        } catch (e) {
            const msg = e?.message || String(e);
            setErrorMessage(msg);
            setStatusMessage(`Error starting analysis: ${msg}`);
            setStatusProgress(0);
            setCurrentStep(WORKFLOW_STEPS.SETUP);
            setIsStartingGenomeAnalysis(false);
        }
    }, [selectedSpecies, stopPolling, loadOrthologousGroups, fetchOrthoFinderOutputs, outputsFetched, galaxyApiKey, ncbiApiKey, galaxyKeyValidation, handleValidateGalaxyKey]);

    const startOrthoFinderPolling = useCallback((historyId, jobId) => {
        stopPolling?.();
        setStopPolling(null);

        const sessionGalaxyApiKey = galaxyApiKey.trim() || resumeGalaxyApiKey.trim() || undefined;
        setCurrentJobInfo({ jobId, state: "running", galaxyApiKey: sessionGalaxyApiKey });
        setStatusMessage("OrthoFinder is running. This may take several minutes...");
        setStatusProgress((prev) => (prev < 30 ? 30 : prev));
        setCurrentStep(WORKFLOW_STEPS.GENOME_ANALYSIS);

        const pollCancel = pollJob(jobId, {
            intervalMs: 8000,
            galaxyApiKey: sessionGalaxyApiKey,
            onTick: (job) => {
                setCurrentJobInfo((prev) => ({ ...(prev || {}), ...job, galaxyApiKey: sessionGalaxyApiKey }));
                const state = job?.state || "unknown";

                if (state === "ok") {
                    setStatusMessage("OrthoFinder complete. Loading orthologous groups...");
                    setStatusProgress(100);
                    setCurrentStep(WORKFLOW_STEPS.GROUP_SELECTION);
                    setIsStartingGenomeAnalysis(false);
                    if (!outputsFetched) {
                        fetchOrthoFinderOutputs(historyId, jobId);
                    }
                    loadOrthologousGroups(historyId, jobId);
                } else if (state === "error" || state === "failed") {
                    setStatusMessage(`OrthoFinder failed: ${state}`);
                    setErrorMessage(`OrthoFinder analysis failed with state: ${state}`);
                    setIsStartingGenomeAnalysis(false);
                } else {
                    setStatusMessage(`OrthoFinder running: ${state}`);
                    setStatusProgress((prev) => (prev < 70 ? 70 : prev));
                }
            },
            onError: (err) => {
                const msg = err?.message || String(err);
                setErrorMessage(msg);
                setStatusMessage(`Error polling job: ${msg}`);
                setIsStartingGenomeAnalysis(false);
            },
        });
        setStopPolling(() => pollCancel);
    }, [loadOrthologousGroups, stopPolling, fetchOrthoFinderOutputs, outputsFetched, galaxyApiKey, resumeGalaxyApiKey]);

    const handleContinuePreparedAnalysis = useCallback(async () => {
        const historyId = extractGalaxyHistoryId(activeHistoryId || resumeHistoryId);
        const sessionGalaxyApiKey = resumeGalaxyApiKey.trim() || galaxyApiKey.trim();
        if (!historyId || !sessionGalaxyApiKey || isContinuingPreparation) return;

        setIsContinuingPreparation(true);
        setErrorMessage(null);
        setStatusMessage("Recovering prepared FASTA files from Galaxy...");
        try {
            const response = await continuePreparedGenomeAnalysis({
                historyId,
                galaxyApiKey: sessionGalaxyApiKey
            });
            let unsubscribe;
            unsubscribe = subscribeToGenomeAnalysisProgress(response.sessionId, {
                onProgress: (update) => {
                    setStatusMessage(update.message || update.step || "Continuing genome preparation...");
                    if (Number.isFinite(update.percentage)) setStatusProgress(update.percentage);
                    if (update.step === "Error") {
                        setErrorMessage(update.message || "Unable to continue to OrthoFinder.");
                        setIsContinuingPreparation(false);
                    }
                    if (update.step === "Analysis Started" && update.details?.orthoFinderJobId) {
                        const genomes = update.details.genomes || [];
                        setGenomeAnalysisData({ historyId, genomes, orthoFinderJobId: update.details.orthoFinderJobId });
                        saveSession({ historyId, genomes, orthoFinderJobId: update.details.orthoFinderJobId });
                        setIsContinuingPreparation(false);
                        try { unsubscribe?.(); } catch { /* stream already closed */ }
                        startOrthoFinderPolling(historyId, update.details.orthoFinderJobId);
                    }
                },
                onError: (error) => {
                    setErrorMessage(error?.message || "The recovery progress stream disconnected.");
                    setIsContinuingPreparation(false);
                }
            });
            setStopPolling(() => unsubscribe);
        } catch (error) {
            setErrorMessage(error?.message || String(error));
            setIsContinuingPreparation(false);
        }
    }, [activeHistoryId, resumeHistoryId, resumeGalaxyApiKey, galaxyApiKey, isContinuingPreparation, startOrthoFinderPolling]);

    const refreshGalaxyStatus = useCallback(async (historyIdOverride, apiKeyOverride) => {
        const historyId = extractGalaxyHistoryId(historyIdOverride || activeHistoryId || resumeHistoryId);
        const apiKey = apiKeyOverride || resumeGalaxyApiKey.trim() || galaxyApiKey.trim();
        if (!historyId || !apiKey) return null;
        if (galaxyStatusRefreshInFlightRef.current || resumeInFlightRef.current) return null;

        galaxyStatusRefreshInFlightRef.current = true;
        setIsRefreshingGalaxyStatus(true);
        try {
            const status = normalizeGalaxyStatus(await inspectGenomeAnalysis({ historyId, galaxyApiKey: apiKey }));
            setGalaxyStatusWarning(null);
            setErrorMessage(null);
            setLiveGalaxyStatus(status);
            setActiveHistoryId(historyId);
            setCurrentJobInfo((previous) => ({ ...(previous || {}), state: status.stage, galaxyApiKey: apiKey }));
            setStatusMessage(status.detail || "Galaxy history inspected.");
            const stageIndex = getDetectedPipelineStage(status.stage, currentStep);
            setStatusProgress([10, 35, 60, 75, 90, 100][stageIndex]);
            if (status.stage === "workflow_complete" || status.stage === "workflow_failed") {
                // The status endpoint intentionally returns only lightweight history
                // evidence. Hydrate completed collection elements separately.
                if (!hyphyResultsHydratedRef.current) {
                    const completed = await getHyphyOutputs(historyId, apiKey);
                    setCurrentJobInfo((previous) => ({
                        ...(previous || {}),
                        state: status.stage,
                        galaxyApiKey: apiKey,
                        hyphyMarkdownOutputs: Array.isArray(completed?.hyphyMarkdownOutputs) ? completed.hyphyMarkdownOutputs : [],
                        hyphyJsonOutputs: Array.isArray(completed?.hyphyJsonOutputs) ? completed.hyphyJsonOutputs : [],
                        hyphyGeneCount: Number.isFinite(completed?.geneCount) ? completed.geneCount : undefined,
                        hyphyMethodCounts: completed?.methodCounts || undefined,
                        hyphyCollectionCountsMatch: completed?.collectionCountsMatch
                    }));
                    hyphyResultsHydratedRef.current = true;
                }
                setCurrentStep(WORKFLOW_STEPS.COMPLETED);
                setIsAnalyzing(false);
                analysisSubmissionInFlightRef.current = false;
            }
            else if (["workflow_running", "workflow_preparing", "hyphy_ready", "post_mafft_ready"].includes(status.stage)) {
                setCurrentStep(WORKFLOW_STEPS.RUNNING_ANALYSIS);
                setIsAnalyzing(["workflow_running", "workflow_preparing"].includes(status.stage));
            }
            else if (status.stage === "workflow_pending") {
                // A prepared unzip collection is reusable input, not proof that an
                // alignment workflow has been submitted. Keep submission enabled.
                setCurrentStep(WORKFLOW_STEPS.GROUP_SELECTION);
                setIsAnalyzing(false);
                analysisSubmissionInFlightRef.current = false;
            }
            else if (["orthofinder_complete", "orthogroups_ready"].includes(status.stage)) setCurrentStep(WORKFLOW_STEPS.GROUP_SELECTION);
            else setCurrentStep(WORKFLOW_STEPS.GENOME_ANALYSIS);
            return status;
        } catch (error) {
            const message = error?.message || String(error);
            if (error?.transient) {
                setErrorMessage(null);
                setGalaxyStatusWarning(
                    "Galaxy did not answer the latest status check. The analysis has not been marked as failed; phyloDIVaS will retry automatically."
                );
            } else {
                setErrorMessage(message);
            }
            return null;
        } finally {
            galaxyStatusRefreshInFlightRef.current = false;
            setIsRefreshingGalaxyStatus(false);
        }
    }, [activeHistoryId, resumeHistoryId, resumeGalaxyApiKey, galaxyApiKey, currentStep]);

    useEffect(() => {
        if (!autoRefreshGalaxyStatus || !activeHistoryId) return undefined;
        refreshGalaxyStatus(activeHistoryId);
        const timer = window.setInterval(() => refreshGalaxyStatus(activeHistoryId), 60000);
        return () => window.clearInterval(timer);
    }, [autoRefreshGalaxyStatus, activeHistoryId, refreshGalaxyStatus]);

    const handleResumeExistingHistory = useCallback(async () => {
        if (resumeInFlightRef.current) return;
        resumeInFlightRef.current = true;
        if (resumeRetryTimerRef.current) {
            window.clearTimeout(resumeRetryTimerRef.current);
            resumeRetryTimerRef.current = null;
        }
        setErrorMessage(null);
        const historyIdInput = extractGalaxyHistoryId(resumeHistoryId);
        if (!historyIdInput) {
            setErrorMessage("Enter a Galaxy history ID to resume.");
            resumeInFlightRef.current = false;
            return;
        }

        stopPolling?.();
        setStopPolling(null);
        setCurrentJobInfo(null);
        hyphyResultsHydratedRef.current = false;
        setGenomeAnalysisData(null);
        setOrthologousGroups([]);
        setSelectedGroups([]);
        setOrthoOutputs(null);
        setSpeciesTreeData(null);
        setSpeciesTreeTimeData(null);
        setSpeciesTreeScaleMode("distance");
        setOutputsFetched(false);
        setIsStartingGenomeAnalysis(true);
        setStatusProgress(0);
        setStatusMessage("Connecting to existing Galaxy history...");
        setCurrentStep(WORKFLOW_STEPS.GENOME_ANALYSIS);

        const sessionGalaxyApiKey = resumeGalaxyApiKey.trim() || galaxyApiKey.trim();
        if (!sessionGalaxyApiKey) {
            setErrorMessage("Enter the Galaxy API key for the account that owns this history.");
            setIsStartingGenomeAnalysis(false);
            resumeInFlightRef.current = false;
            return;
        }

        // The attach endpoint validates access and reconstructs the complete UI state.
        // A lightweight status refresh is useful telemetry, but must never gate resume.
        const processResponse = async () => {
            const resp = await resumeGenomeAnalysis({
                historyId: historyIdInput,
                orthoFinderJobId: undefined,
                galaxyApiKey: resumeGalaxyApiKey.trim() || galaxyApiKey.trim() || undefined
            });
            const sessionGalaxyApiKey = resumeGalaxyApiKey.trim() || galaxyApiKey.trim() || undefined;

            const {
                historyId,
                orthoFinderJobId,
                genomes = [],
                status,
                statusDetail,
                statusProgress: serverStatusProgress,
                workflowInvocationId,
                workflowStatus,
                workflowStatusDetail,
                hyphyMarkdownOutputs,
                hyphyJsonOutputs
            } = resp;

            if (!historyId) {
                throw new Error("Missing Galaxy history ID when attaching to existing history.");
            }

            setActiveHistoryId(historyId);
            setSelectedSpecies(genomes.map((g) => g.species).filter(Boolean));
            setGenomeAnalysisData({ historyId, genomes, orthoFinderJobId });
            saveSession({ historyId, orthoFinderJobId, genomes });

            const normalizedStatus = workflowInvocationId
                ? (workflowStatus || "workflow_running")
                : (status || (orthoFinderJobId
                    ? "orthofinder"
                    : (genomes.length === 0 ? "initializing" : "uploading")));

            const messageByStatus = statusDetail ?? workflowStatusDetail ?? (
                normalizedStatus === "initializing"
                    ? "Galaxy is preparing the history and downloading genomes..."
                    : normalizedStatus === "uploading"
                        ? `Galaxy has downloaded ${genomes.length} genome(s) and is preparing FASTA files...`
                        : normalizedStatus === "workflow_complete"
                            ? "Galaxy workflow completed. Loading outputs..."
                            : normalizedStatus === "workflow_failed"
                                ? "Galaxy workflow failed. Review the failed jobs in Galaxy history."
                            : workflowInvocationId
                                ? "Galaxy workflow is running for orthogroup analysis..."
                                : "Connected to Galaxy history. Monitoring OrthoFinder job..."
            );

            const progressByStatus = Number.isFinite(serverStatusProgress)
                ? serverStatusProgress
                : normalizedStatus === "initializing"
                    ? 10
                    : normalizedStatus === "uploading"
                        ? 30
                        : normalizedStatus === "workflow_complete" || normalizedStatus === "workflow_failed"
                            ? 100
                            : normalizedStatus === "orthogroups_ready"
                                ? 80
                                : workflowInvocationId
                                    ? 90
                                    : 70;

            setStatusMessage(messageByStatus);
            setStatusProgress(progressByStatus);
            setGalaxyStatusWarning(null);
            setLiveGalaxyStatus((previous) => ({
                ...(previous || {}),
                historyId,
                stage: normalizedStatus,
                detail: messageByStatus,
                checkedAt: new Date().toISOString(),
                jobs: previous?.jobs || { running: 0, queued: 0, failed: 0 }
            }));
            setCurrentJobInfo({
                historyId,
                jobId: orthoFinderJobId || null,
                state: normalizedStatus,
                galaxyApiKey: sessionGalaxyApiKey,
                genomes,
                ...(workflowInvocationId ? { workflowInvocationId } : {}),
                ...(Array.isArray(hyphyMarkdownOutputs) && hyphyMarkdownOutputs.length ? { hyphyMarkdownOutputs } : {}),
                ...(Array.isArray(hyphyJsonOutputs) && hyphyJsonOutputs.length ? { hyphyJsonOutputs } : {})
            });

            if (orthoFinderJobId && !outputsFetched) {
                fetchOrthoFinderOutputs(historyId, orthoFinderJobId);
            }

            // If the downstream workflow was already invoked, skip group selection and show analysis progress/results
            if (workflowInvocationId) {
                setCurrentStep(
                    normalizedStatus === "workflow_complete"
                    || normalizedStatus === "workflow_failed"
                        ? WORKFLOW_STEPS.COMPLETED
                        : WORKFLOW_STEPS.RUNNING_ANALYSIS
                );
                setIsAnalyzing(normalizedStatus !== "workflow_complete" && normalizedStatus !== "workflow_failed");
                setStatusMessage(messageByStatus);
                setStatusProgress(progressByStatus);
                setIsStartingGenomeAnalysis(false);
                setStopPolling(null);
                return;
            }

            if (["workflow_complete", "workflow_failed"].includes(normalizedStatus)) {
                setCurrentStep(WORKFLOW_STEPS.COMPLETED);
                setIsAnalyzing(false);
                setIsStartingGenomeAnalysis(false);
                setStopPolling(null);
                return;
            }

            if (["workflow_running", "workflow_preparing"].includes(normalizedStatus)) {
                setCurrentStep(WORKFLOW_STEPS.RUNNING_ANALYSIS);
                setIsAnalyzing(true);
                setIsStartingGenomeAnalysis(false);
                setStopPolling(null);
                return;
            }

            if (["orthofinder_complete", "orthogroups_ready", "workflow_pending"].includes(normalizedStatus)) {
                    setIsLoadingGroups(true);
                    const outputLookupId = orthoFinderJobId || "history-scan";
                    setGenomeAnalysisData({ historyId, genomes, orthoFinderJobId: outputLookupId });
                    setCurrentStep(WORKFLOW_STEPS.GROUP_SELECTION);
                    setStatusMessage(messageByStatus);
                    setStatusProgress(progressByStatus);
                    if (!outputsFetched) {
                        fetchOrthoFinderOutputs(historyId, outputLookupId);
                    }
                    loadOrthologousGroups(historyId, outputLookupId);
                    setStopPolling(null);
                    setIsStartingGenomeAnalysis(false);
                    return;
            }

            if (!orthoFinderJobId) {
                // Attaching is a mutating, expensive recovery operation. Do it once;
                // subsequent auto-refreshes use the read-only status endpoint.
                setStatusMessage(messageByStatus);
                setIsStartingGenomeAnalysis(false);
                setStopPolling(null);
                return;
            }

            setGenomeAnalysisData({ historyId, genomes, orthoFinderJobId });
            startOrthoFinderPolling(historyId, orthoFinderJobId);
        };

        try {
            await processResponse();
            resumeRetryCountRef.current = 0;
            void refreshGalaxyStatus(historyIdInput, sessionGalaxyApiKey);
        } catch (e) {
            const msg = e?.message || String(e);
            if (e?.transient) {
                setGalaxyStatusWarning(
                    "Galaxy is temporarily unavailable. The history remains selected and phyloDIVaS will retry status checks automatically."
                );
                setStatusMessage("Connected history saved; waiting for Galaxy to answer the resume request...");
                setActiveHistoryId(historyIdInput);
                if (resumeRetryCountRef.current < 3) {
                    resumeRetryCountRef.current += 1;
                    const retryDelayMs = resumeRetryCountRef.current * 15000;
                    resumeRetryTimerRef.current = window.setTimeout(() => {
                        resumeRetryTimerRef.current = null;
                        handleResumeExistingHistory();
                    }, retryDelayMs);
                }
            } else {
                setErrorMessage(msg);
                setStatusMessage(`Error loading history: ${msg}`);
                setCurrentStep(WORKFLOW_STEPS.SETUP);
            }
            setIsLoadingGroups(false);
        } finally {
            // Even if we're waiting on OrthoFinder to start, stop the "connecting" spinner
            resumeInFlightRef.current = false;
            setIsStartingGenomeAnalysis(false);
        }
    }, [resumeHistoryId, resumeGalaxyApiKey, galaxyApiKey, stopPolling, startOrthoFinderPolling, fetchOrthoFinderOutputs, loadOrthologousGroups, outputsFetched, refreshGalaxyStatus]);


    // Auto-resume if a historyId is provided in the route
    useEffect(() => {
        if (!routeHistoryId || autoResumeAttempted) return;
        setAutoResumeAttempted(true);
        setWorkflowMode(WORKFLOW_MODES.RESUME);
        setResumeHistoryId(routeHistoryId);
        handleResumeExistingHistory();
    }, [routeHistoryId, autoResumeAttempted, handleResumeExistingHistory]);

    // A normal /Analysis visit should start a new analysis. Preserve a previous
    // history ID as a convenience, but only reconnect after the user chooses Resume.
    useEffect(() => {
        if (autoResumeAttempted || routeHistoryId) return;
        try {
            const stored = localStorage.getItem(SESSION_KEY);
            if (!stored) return;
            const data = JSON.parse(stored);
            if (data?.historyId) {
                setAutoResumeAttempted(true);
                setResumeHistoryId(data.historyId);
            }
        } catch {
            /* ignore */
        }
    }, [autoResumeAttempted, routeHistoryId]);

    // If workflow is complete but HyPhy outputs are empty, attempt to refresh them once
    useEffect(() => {
        if (currentStep !== WORKFLOW_STEPS.COMPLETED) return;
        if (currentJobInfo?.hyphyMarkdownOutputs?.length || currentJobInfo?.hyphyJsonOutputs?.length) return;
        if (hyphyResultsHydratedRef.current) return;

        const historyId = genomeAnalysisData?.historyId || activeHistoryId;
        if (!historyId) return;
        hyphyResultsHydratedRef.current = true;
        refreshHyphyOutputs(historyId);
    }, [currentStep, currentJobInfo, genomeAnalysisData, activeHistoryId, refreshHyphyOutputs]);



    const handleAnalyzeSelected = useCallback(async () => {
        if (!genomeAnalysisData || selectedGroups.length === 0 || analysisSubmissionInFlightRef.current) return;

        analysisSubmissionInFlightRef.current = true;
        setErrorMessage(null);
        setIsAnalyzing(true);
        setStatusMessage(`Validating ${selectedGroups.length} selected groups before Galaxy submission...`);
        setStatusProgress(10);

        try {
            const selectedGroupData = orthologousGroups.filter(group =>
                selectedGroups.includes(group.groupId)
            );
            const resp = await analyzeMultipleGroups({
                historyId: genomeAnalysisData.historyId,
                orthoGroups: selectedGroupData,
                genomes: genomeAnalysisData.genomes,
                galaxyApiKey: galaxyApiKey.trim() || resumeGalaxyApiKey.trim() || undefined,
                ncbiApiKey: ncbiApiKey.trim() || undefined,
                alignment
            });

            setSubmittedGroups((previous) => {
                const byId = new Map(previous.map((group) => [group.groupId, group]));
                selectedGroupData.forEach((group) => byId.set(group.groupId, group));
                return Array.from(byId.values());
            });
            setCurrentStep(WORKFLOW_STEPS.RUNNING_ANALYSIS);

            const results = Array.isArray(resp?.results) ? resp.results : [];
            const jobIds = results.map((r) => r?.jobId).filter(Boolean);
            const workflowInvocationId = extractWorkflowInvocationId(results);
            const hasGalaxyWork = jobIds.length > 0 || !!workflowInvocationId;

            if (!hasGalaxyWork) throw new Error("Galaxy did not return a workflow invocation ID.");

            // Galaxy owns the complete dependency graph after invocation. Clear the
            // request-scoped credential; reconnecting later requires entering it again.
            analysisSubmissionInFlightRef.current = false;
            setIsAnalyzing(false);
            setGalaxyApiKey("");
            setResumeGalaxyApiKey("");
            setGalaxyKeyValidation("idle");
            setAutoRefreshGalaxyStatus(false);
            setStatusMessage("Complete Galaxy workflow submitted. Galaxy will continue MAFFT, back-translation, IQ-TREE and all HyPhy tests even if this page or localhost closes.");
            setStatusProgress(50);
            setCurrentJobInfo({
                ...(jobIds.length ? { jobIds } : {}),
                ...(workflowInvocationId ? { workflowInvocationId } : {}),
                state: "workflow_running"
            });
        } catch (e) {
            const msg = e?.message || String(e);
            setErrorMessage(msg);
            setStatusMessage(`Submission was not accepted: ${msg}`);
            setCurrentStep(WORKFLOW_STEPS.GROUP_SELECTION);
            setStatusProgress(0);
            setIsAnalyzing(false);
            analysisSubmissionInFlightRef.current = false;
        }
    }, [genomeAnalysisData, selectedGroups, orthologousGroups, alignment, galaxyApiKey, resumeGalaxyApiKey, ncbiApiKey]);

    const handleCancelAnalysis = useCallback(async () => {
        const historyId = genomeAnalysisData?.historyId || activeHistoryId;
        const key = galaxyApiKey.trim() || resumeGalaxyApiKey.trim();
        if (!historyId || !key || isCancelingAnalysis) return;
        setIsCancelingAnalysis(true);
        setStatusMessage("Canceling active Galaxy jobs...");
        try {
            const result = await cancelGenomeAnalysis({ historyId, galaxyApiKey: key });
            stopPolling?.();
            analysisSubmissionInFlightRef.current = false;
            setIsAnalyzing(false);
            setCurrentStep(WORKFLOW_STEPS.GROUP_SELECTION);
            setStatusProgress(0);
            setStatusMessage(`Analysis canceled. ${result?.canceled?.length || 0} active Galaxy jobs were stopped.`);
        } catch (error) {
            setErrorMessage(error?.message || "Galaxy could not cancel the analysis.");
        } finally {
            setIsCancelingAnalysis(false);
        }
    }, [genomeAnalysisData, activeHistoryId, galaxyApiKey, resumeGalaxyApiKey, isCancelingAnalysis, stopPolling]);

    const handleStartNewAnalysis = useCallback(() => {
        stopPolling?.();
        analysisSubmissionInFlightRef.current = false;
        setSubmittedGroups([]);
        setSelectedGroups([]);
        setGenomeAnalysisData(null);
        setActiveHistoryId(null);
        setCurrentJobInfo(null);
        setCurrentStep(WORKFLOW_STEPS.SETUP);
        setStatusProgress(0);
        setStatusMessage("Add at least 3 species to begin analysis.");
        setErrorMessage(null);
    }, [stopPolling]);

    const handleRetryHyphy = useCallback(async () => {
        const historyId = genomeAnalysisData?.historyId || activeHistoryId;
        const key = resumeGalaxyApiKey.trim() || galaxyApiKey.trim();
        const resumesAfterMafft = [liveGalaxyStatus?.stage, currentJobInfo?.state].includes("post_mafft_ready");
        if (!historyId) {
            setErrorMessage("Load the Galaxy history before restarting HyPhy.");
            return;
        }
        if (!key) {
            setErrorMessage("Re-enter the Galaxy API key for this history. phyloDIVaS does not retain API keys.");
            return;
        }
        if (isRetryingHyphy) return;

        setIsRetryingHyphy(true);
        setErrorMessage(null);
        setStatusMessage(resumesAfterMafft
            ? "Checking completed MAFFT alignments and filtered CDS collections..."
            : "Checking completed codon alignments and IQ-TREE gene trees...");
        try {
            const result = await retryHyphyFromCompletedAlignments({ historyId, galaxyApiKey: key });
            setCurrentJobInfo((previous) => ({
                ...(previous || {}),
                state: "workflow_running",
                workflowInvocationId: result.invocationId
            }));
            setLiveGalaxyStatus((previous) => ({
                ...(previous || {}),
                historyId,
                stage: "workflow_running",
                detail: resumesAfterMafft
                    ? "Post-MAFFT workflow submitted. Galaxy is running back-translation, IQ-TREE, MEME, BUSTED, and aBSREL."
                    : "HyPhy-only retry submitted using the completed alignment and gene-tree collections.",
                checkedAt: new Date().toISOString(),
                jobs: previous?.jobs || { running: 0, queued: 0, failed: 0 }
            }));
            setCurrentStep(WORKFLOW_STEPS.RUNNING_ANALYSIS);
            setIsAnalyzing(true);
            setGalaxyApiKey("");
            setResumeGalaxyApiKey("");
            setGalaxyKeyValidation("idle");
            setAutoRefreshGalaxyStatus(false);
            setStatusMessage(`${resumesAfterMafft ? "Post-MAFFT recovery" : "HyPhy-only retry"} submitted. Galaxy now owns the jobs and will continue without this page or localhost. Re-enter the key only when you want to refresh private-history status.`);
            setStatusProgress(90);
        } catch (error) {
            if (error?.status === 409) {
                setErrorMessage(null);
                setStatusMessage("A recovery submission is already being processed by phyloDIVaS. HyPhy jobs are not confirmed until Galaxy returns a workflow invocation. Do not submit again while preparation is in progress.");
                return;
            }
            setErrorMessage(error?.message || "Galaxy could not restart HyPhy from the completed alignments.");
            setStatusMessage("HyPhy was not restarted.");
        } finally {
            setIsRetryingHyphy(false);
        }
    }, [genomeAnalysisData, activeHistoryId, resumeGalaxyApiKey, galaxyApiKey, isRetryingHyphy, liveGalaxyStatus?.stage, currentJobInfo?.state]);

    const automaticRecoveryAttempts = useRef(new Set());
    useEffect(() => {
        const stage = liveGalaxyStatus?.stage;
        const historyId = activeHistoryId || genomeAnalysisData?.historyId;
        if (!historyId || !["hyphy_ready", "post_mafft_ready"].includes(stage)) return;
        if (isRetryingHyphy || !(resumeGalaxyApiKey.trim() || galaxyApiKey.trim())) return;
        const jobs = liveGalaxyStatus?.jobs;
        if (!jobs || jobs.running > 0 || jobs.queued > 0 || jobs.failed > 0) return;
        const attempt = `${historyId}:${stage}`;
        if (automaticRecoveryAttempts.current.has(attempt)) return;
        automaticRecoveryAttempts.current.add(attempt);
        void handleRetryHyphy();
    }, [liveGalaxyStatus, activeHistoryId, genomeAnalysisData?.historyId, isRetryingHyphy, resumeGalaxyApiKey, galaxyApiKey, handleRetryHyphy]);

    const handleGroupSelectionChange = useCallback((newSelection) => {
        setSelectedGroups(newSelection);
    }, []);

    const handleSelectAll = useCallback((allGroupIds) => {
        setSelectedGroups(allGroupIds);
    }, []);

    const handleSelectNone = useCallback(() => {
        setSelectedGroups([]);
    }, []);

    const handleGroupsAnnotated = useCallback((updates) => {
        setOrthologousGroups((current) => current.map((group) => {
            const annotation = updates[group.groupId];
            if (!annotation || group.groupName === annotation.name) return group;
            return { ...group, groupName: annotation.name, annotation };
        }));
    }, []);

    const handleAnalyzeAll = useCallback(async () => {
        if (!genomeAnalysisData || analysisSubmissionInFlightRef.current) return;

        const groupCount = orthologousGroups.length;
        const confirmed = window.confirm(
            `Analyze all ${groupCount.toLocaleString()} strict 1:1 N0 orthogroups?\n\n` +
            "This reuses the current species set and OrthoFinder results, but Galaxy will run alignment, " +
            "back-translation, gene-tree inference, BUSTED, aBSREL, and MEME for every group. " +
            "A genome-wide run can take many hours or days and use substantial Galaxy storage."
        );
        if (!confirmed) return;

        analysisSubmissionInFlightRef.current = true;
        setErrorMessage(null);
        setIsAnalyzing(true);
        setCurrentStep(WORKFLOW_STEPS.RUNNING_ANALYSIS);
        setStatusMessage(`Running Galaxy workflow for orthogroup analysis...`);
        setStatusProgress(10);

        try {
            const resp = await analyzeAllGroups({
                historyId: genomeAnalysisData.historyId,
                orthoFinderJobId: genomeAnalysisData.orthoFinderJobId,
                genomes: genomeAnalysisData.genomes,
                galaxyApiKey: galaxyApiKey.trim() || resumeGalaxyApiKey.trim() || undefined,
                ncbiApiKey: ncbiApiKey.trim() || undefined,
                alignment
            });

            const results = Array.isArray(resp?.results) ? resp.results : [];
            const workflowInvocationId = extractWorkflowInvocationId(results);
            const jobIds = results.map(r => r?.jobId).filter(Boolean);
            const hasGalaxyWork = jobIds.length > 0 || !!workflowInvocationId;

            setSubmittedGroups(orthologousGroups);
            setSelectedGroups(orthologousGroups.map((group) => group.groupId));

            if (!hasGalaxyWork) throw new Error("Galaxy did not return a workflow invocation ID.");

            analysisSubmissionInFlightRef.current = false;
            setIsAnalyzing(false);
            setGalaxyApiKey("");
            setResumeGalaxyApiKey("");
            setGalaxyKeyValidation("idle");
            setAutoRefreshGalaxyStatus(false);
            setStatusMessage(`Complete Galaxy workflow launched for all ${groupCount.toLocaleString()} orthogroups. Galaxy will continue through HyPhy without this page or localhost.`);
            setStatusProgress(50);
            setCurrentJobInfo({
                ...(jobIds.length ? { jobId: jobIds[0], jobIds } : {}),
                ...(workflowInvocationId ? { workflowInvocationId } : {}),
                state: "workflow_running"
            });

        } catch (e) {
            const msg = e?.message || String(e);
            setErrorMessage(msg);
            setStatusMessage(`Error starting analysis: ${msg}`);
            setCurrentStep(WORKFLOW_STEPS.RUNNING_ANALYSIS);
            setIsAnalyzing(false);
            analysisSubmissionInFlightRef.current = false;
        }
    }, [genomeAnalysisData, orthologousGroups, alignment, galaxyApiKey, resumeGalaxyApiKey, ncbiApiKey]);

    const handleAnalyzeAnotherGeneSet = useCallback(() => {
        stopPolling?.();
        analysisSubmissionInFlightRef.current = false;
        setStopPolling(null);
        setSelectedGroups([]);
        setSubmittedGroups([]);
        setIsAnalyzing(false);
        setErrorMessage(null);
        setCurrentJobInfo((previous) => ({
            ...(previous || {}),
            state: "orthogroups_ready"
        }));
        setCurrentStep(WORKFLOW_STEPS.GROUP_SELECTION);
        setStatusProgress(100);
        setStatusMessage(
            `Reusing this species set and ${orthologousGroups.length.toLocaleString()} strict 1:1 N0 orthogroups. ` +
            "Choose a new gene set; genome retrieval and OrthoFinder will not run again."
        );
    }, [orthologousGroups.length, stopPolling]);

    const handleBackToSpeciesSelection = useCallback(() => {
        setCurrentStep(WORKFLOW_STEPS.SETUP);
        setGenomeAnalysisData(null);
        setOrthologousGroups([]);
        setSelectedGroups([]);
        setCurrentJobInfo(null);
        setStatusProgress(0);
        setStatusMessage(
            workflowMode === WORKFLOW_MODES.RESUME
                ? "Enter an existing Galaxy history ID to resume analysis."
                : "Add at least 3 species to begin analysis."
        );
        stopPolling?.();
        setStopPolling(null);
        setActiveHistoryId(null);
    }, [stopPolling, workflowMode]);

    const handleModeChange = useCallback(
        (mode) => {
            if (workflowMode === mode) return;
            stopPolling?.();
            setWorkflowMode(mode);
            setCurrentStep(WORKFLOW_STEPS.SETUP);
            setErrorMessage(null);
            setCurrentJobInfo(null);
            setStatusProgress(0);
            setStatusMessage(
                mode === WORKFLOW_MODES.RESUME
                    ? "Enter an existing Galaxy history ID to resume analysis."
                    : "Add at least 3 species to begin analysis."
            );
        },
        [workflowMode, stopPolling]
    );

    const renderJobMeta = () => {
        const workflowInvocationId = currentJobInfo?.workflowInvocationId;
        const hasJobInfo = !!currentJobInfo && (currentJobInfo.state || currentJobInfo.jobId || (currentJobInfo.jobIds && currentJobInfo.jobIds.length) || workflowInvocationId);
        if (!hasJobInfo && !activeHistoryId) return null;

        const state = currentJobInfo?.state || (Array.isArray(currentJobInfo?.jobIds) ? "running" : null);
        const jobIds = Array.isArray(currentJobInfo?.jobIds) ? currentJobInfo.jobIds.join(", ") : currentJobInfo?.jobId;
        const hideJobIds = state === "workflow_complete" || jobIds === "history-scan";

        if (state === "workflow_complete") {
            return (
                <div className={resultsStyles.jobMeta}>
                    <span className={`${resultsStyles.jobTag} ${resultsStyles.jobState}`}>Workflow complete</span>
                </div>
            );
        }

        if (state === "workflow_failed") {
            return (
                <div className={resultsStyles.jobMeta}>
                    <span className={`${resultsStyles.jobTag} ${resultsStyles.jobState}`}>Workflow failed</span>
                </div>
            );
        }

        return (
            <div className={resultsStyles.jobMeta}>
                {activeHistoryId && (
                    <span className={resultsStyles.historyBadge}>
                        History <code>{activeHistoryId}</code>
                    </span>
                )}
                {!hideJobIds && jobIds && (
                    <span className={resultsStyles.jobTag}>
                        Job{Array.isArray(currentJobInfo?.jobIds) && currentJobInfo.jobIds.length > 1 ? "s" : ""}:{" "}
                        <code>{jobIds}</code>
                    </span>
                )}
            </div>
        );
    };

    const orthoFinderJobId = genomeAnalysisData?.orthoFinderJobId || (typeof currentJobInfo?.jobId === "string" ? currentJobInfo.jobId : null);
    const advancedOptionsOpen = showAdvancedOptions || !!ncbiApiKey || alignment !== "mafft";
    const workflowStage = currentStep === WORKFLOW_STEPS.SETUP ? 0
        : currentStep === WORKFLOW_STEPS.GENOME_ANALYSIS ? 1
        : currentStep === WORKFLOW_STEPS.GROUP_SELECTION ? 2
        : currentStep === WORKFLOW_STEPS.RUNNING_ANALYSIS ? 3
        : 4;
    const detectedPipelineStage = getDetectedPipelineStage(currentJobInfo?.state, currentStep);
    const workflowFailed = [currentJobInfo?.state, liveGalaxyStatus?.stage]
        .some((state) => String(state || "").toLowerCase().endsWith("_failed"));
    const workflowSucceeded = currentJobInfo?.state === "workflow_complete" || liveGalaxyStatus?.stage === "workflow_complete";
    const pipelineTimeEstimate = getPipelineTimeEstimate({
        stage: liveGalaxyStatus?.stage || currentJobInfo?.state,
        currentStep,
        speciesCount: genomeAnalysisData?.genomes?.length || selectedSpecies.length,
        groupCount: submittedGroups.length || selectedGroups.length || liveGalaxyStatus?.selectedOrthogroupCount || 0,
        jobs: liveGalaxyStatus?.jobs
    });
    const strictOrthologCount = liveGalaxyStatus?.strictOrthogroupCount || orthologousGroups.length;
    const analysisSpeciesCount = genomeAnalysisData?.genomes?.length || selectedSpecies.length;
    const handleToggleRunningOrthoResults = async () => {
        const nextOpen = !showRunningOrthoResults;
        setShowRunningOrthoResults(nextOpen);
        if (!nextOpen) return;

        const historyId = genomeAnalysisData?.historyId || activeHistoryId;
        if (!historyId) {
            setOrthoBrowseError("The Galaxy history is not connected, so OrthoFinder results cannot be loaded.");
            return;
        }

        const outputLookupId = orthoFinderJobId || "history-scan";
        if (orthologousGroups.length === 0) {
            const outputs = await loadOrthologousGroups(historyId, outputLookupId, { quiet: true });
            if (outputs) await fetchOrthoFinderOutputs(historyId, outputLookupId, { quiet: true, data: outputs });
        } else if (!orthoOutputs) {
            await fetchOrthoFinderOutputs(historyId, outputLookupId, { quiet: true });
        }
    };

    return (
        <div className={analysisPageStyles.body}>
            <div className={analysisPageStyles.searchSection}>
                <div className={analysisPageStyles.searchContainer}>
                    <p className={analysisPageStyles.eyebrow}>Comparative genomics workflow</p>
                    <h1 className={`${analysisPageStyles.title} mb-2`}>dN/dS selection pressure analysis</h1>
                    <small>Application revision: {import.meta.env.VITE_APP_REVISION || "development"}</small>
                    <p className={analysisPageStyles.subtitle}>
                        Select annotated RefSeq genomes, identify orthologous genes with OrthoFinder, and test selection pressure with HyPhy BUSTED.
                    </p>
                    <ol className={analysisPageStyles.workflowSteps} aria-label="Analysis workflow">
                        {["Select species", "Find orthologs", "Choose groups", "Run tests", "Review results"].map((label, index) => (
                            <li key={label} className={index < workflowStage ? analysisPageStyles.completeStep : index === workflowStage ? analysisPageStyles.currentStep : ""} aria-current={index === workflowStage ? "step" : undefined}>
                                <span>{index + 1}</span>{label}
                            </li>
                        ))}
                    </ol>
                    <p className={analysisPageStyles.provenance}><strong>Data flow:</strong> NCBI RefSeq provides annotated genomes; phyloDIVaS submits processing jobs to Galaxy; results and restart metadata remain in your browser and Galaxy history.</p>

            {/* Progress indicator */}
            {currentStep !== WORKFLOW_STEPS.SETUP && (
                <div className={`${selectorStyles.container} mb-4`}>
                    <div className={resultsStyles.statusBlock}>
                        {(statusMessage || errorMessage) && (
                            <Alert
                                variant={errorMessage ? "danger" : "info"}
                                className={resultsStyles.statusAlert}
                            >
                                {errorMessage || statusMessage}
                            </Alert>
                        )}
                        <div className={analysisPageStyles.pipelineTracker} aria-label="Detected Galaxy pipeline stage">
                            <div className={analysisPageStyles.pipelineTrackerHeader}>
                                <strong>Detected pipeline stage</strong>
                                <span>Based on the jobs and datasets currently present in this Galaxy history</span>
                            </div>
                            <ol>
                                {PIPELINE_STAGES.map((label, index) => (
                                    <li
                                        key={label}
                                        className={index < detectedPipelineStage
                                            ? analysisPageStyles.pipelineStageComplete
                                            : index === detectedPipelineStage
                                                ? workflowFailed
                                                    ? analysisPageStyles.pipelineStageFailed
                                                    : analysisPageStyles.pipelineStageCurrent
                                                : ""}
                                        aria-current={index === detectedPipelineStage ? "step" : undefined}
                                    >
                                        <span aria-hidden="true">{index < detectedPipelineStage ? "✓" : index === detectedPipelineStage && workflowFailed ? "!" : index + 1}</span>
                                        {label}
                                    </li>
                                ))}
                            </ol>
                            <div className={analysisPageStyles.timeEstimate} role="status" aria-live="polite">
                                <div>
                                    <span>Estimated total time remaining</span>
                                    <strong>{pipelineTimeEstimate.value}</strong>
                                </div>
                                <p>{pipelineTimeEstimate.detail}</p>
                            </div>
                            {liveGalaxyStatus && (
                                <div className={analysisPageStyles.liveStatusPanel}>
                                    {galaxyStatusWarning && (
                                        <Alert variant="warning" className="mb-2 py-2">
                                            {galaxyStatusWarning}
                                        </Alert>
                                    )}
                                    <div className={analysisPageStyles.liveStatusSummary}>
                                        <span><strong>{liveGalaxyStatus.historyName || "Galaxy history"}</strong></span>
                                        <span>Dataset/collection records: {liveGalaxyStatus.jobs?.running || 0} running</span>
                                        <span>{liveGalaxyStatus.jobs?.queued || 0} queued</span>
                                        <span>{liveGalaxyStatus.jobs?.failed || 0} failed</span>
                                        <span>Checked {new Date(liveGalaxyStatus.checkedAt).toLocaleTimeString()}</span>
                                    </div>
                                    {Array.isArray(liveGalaxyStatus.evidence) && liveGalaxyStatus.evidence.length > 0 && (
                                        <details className={analysisPageStyles.liveStatusEvidence}>
                                            <summary>Galaxy items used to determine this stage</summary>
                                            <ul>
                                                {liveGalaxyStatus.evidence.slice(0, 50).map((item) => (
                                                    <li key={`${item.id || item.hid}-${item.name}`}>
                                                        <span>{item.name}</span>
                                                        <strong>{item.state || "unknown"}</strong>
                                                    </li>
                                                ))}
                                            </ul>
                                        </details>
                                    )}
                                    <div className={analysisPageStyles.liveStatusActions}>
                                        {liveGalaxyStatus.stage === "unzipped" && (
                                            <Button
                                                type="button"
                                                variant="primary"
                                                size="sm"
                                                disabled={isContinuingPreparation}
                                                onClick={handleContinuePreparedAnalysis}
                                            >
                                                {isContinuingPreparation ? "Recovering preparation..." : "Recover interrupted preparation"}
                                            </Button>
                                        )}
                                        <Form.Check
                                            type="switch"
                                            id="auto-refresh-galaxy-status"
                                            label="Auto-refresh"
                                            checked={autoRefreshGalaxyStatus}
                                            onChange={(event) => setAutoRefreshGalaxyStatus(event.target.checked)}
                                        />
                                        <Button
                                            type="button"
                                            variant="outline-secondary"
                                            size="sm"
                                            disabled={isRefreshingGalaxyStatus}
                                            onClick={() => {
                                                hyphyResultsHydratedRef.current = false;
                                                refreshGalaxyStatus(activeHistoryId);
                                            }}
                                        >
                                            {isRefreshingGalaxyStatus ? "Refreshing..." : "Refresh now"}
                                        </Button>
                                    </div>
                                </div>
                            )}
                        </div>
                    </div>
                    {renderJobMeta()}
                </div>
            )}

            {/* OrthoFinder outputs and species tree */}
            {orthoOutputs && currentStep !== WORKFLOW_STEPS.GENOME_ANALYSIS && (
                <div className={`${selectorStyles.container} mb-4`}>
                    <div className={analysisPageStyles.outputsHeader}>
                        <div>
                            <h5 className="mb-1">OrthoFinder Outputs</h5>
                            <p className="text-muted mb-0">Download the generated files and preview the inferred species tree.</p>
                        </div>
                        <div className={analysisPageStyles.outputsMeta}>
                            {strictOrthologCount > 0 && (
                                <span className={analysisPageStyles.outputBadge}>
                                    {strictOrthologCount.toLocaleString()} selectable strict 1:1 orthologs
                                </span>
                            )}
                            {orthoOutputs.orthogroupsDatasetId && (
                                <span className={analysisPageStyles.outputBadge}>Orthogroups ready</span>
                            )}
                            {orthoOutputs.singleCopyDatasetId && (
                                <span className={analysisPageStyles.outputBadge}>Single-copy set</span>
                            )}
                        </div>
                    </div>

                    <div className={analysisPageStyles.outputGrid}>
                        {(orthoOutputs.outputs || []).map((raw) => {
                            const o = normalizeOutputItem(raw);
                            if (!o) return null;
                            const safeLabel = o.label || "orthogroups";
                            const suggestedName = o.suggestedExtension
                                ? `${safeLabel.replace(/\s+/g, "_").toLowerCase()}.${o.suggestedExtension}`
                                : safeLabel.replace(/\s+/g, "_");
                            return (
                                <div key={o.datasetId} className={analysisPageStyles.outputCard}>
                                    <div className={analysisPageStyles.outputTitle}>{safeLabel}</div>
                                    <p className={analysisPageStyles.outputDescription}>{o.description}</p>
                                    <Button
                                        variant="outline-primary"
                                        size="sm"
                                        onClick={() => downloadOrthoFinderOutputFile(o.datasetId, suggestedName)}
                                    >
                                        Download
                                    </Button>
                                </div>
                            );
                        })}
                    </div>

                    {speciesTreeData && (
                        <div className={analysisPageStyles.treeCard}>
                            <div className={analysisPageStyles.treeHeader}>
                                <div>
                                    <h6 className="mb-0">Species Tree</h6>
                                    <small className="text-muted">
                                        Branch labels are OrthoFinder substitutions/site. Node ages are TimeTree median estimates.
                                    </small>
                                </div>
                                <div className={analysisPageStyles.treeScaleSwitcher} role="group" aria-label="Tree horizontal scale">
                                    <button
                                        type="button"
                                        className={speciesTreeScaleMode === "distance" ? analysisPageStyles.treeScaleActive : ""}
                                        aria-pressed={speciesTreeScaleMode === "distance"}
                                        onClick={() => setSpeciesTreeScaleMode("distance")}
                                    >
                                        Evolutionary distance
                                    </button>
                                    <button
                                        type="button"
                                        className={speciesTreeScaleMode === "time" ? analysisPageStyles.treeScaleActive : ""}
                                        aria-pressed={speciesTreeScaleMode === "time"}
                                        onClick={() => setSpeciesTreeScaleMode("time")}
                                        disabled={!speciesTreeTimeData}
                                        title={speciesTreeTimeData ? "Scale horizontal positions by TimeTree divergence ages" : "TimeTree estimates are unavailable for this tree"}
                                    >
                                        Divergence time
                                    </button>
                                </div>
                            </div>
                            <div className={analysisPageStyles.treeCanvas} ref={treeContainerRef}>
                                <Form.Check type="switch" id="orthofinder-species-icons" label="Species silhouettes" checked={showSpeciesIcons} onChange={(event) => setShowSpeciesIcons(event.target.checked)} />
                                {renderSpeciesTree()}
                                {showSpeciesIcons && <details><summary>Species image credits</summary><ul>
                                    {collectLeafNames(speciesTreeData).map((name) => name.replace(/_protein\.faa$/i, "").replace(/_/g, " ")).map((name) => {
                                        const icon = speciesIcons[name];
                                        return <li key={name}>{name}: {icon ? <><a href={icon.source} target="_blank" rel="noopener noreferrer">{icon.credit}</a> (<a href={icon.license} target="_blank" rel="noopener noreferrer">license</a>){icon.fallback ? `; genus silhouette: ${icon.taxon}` : `; ${icon.taxon}`}</> : "No silhouette loaded"}</li>;
                                    })}
                                </ul></details>}
                            </div>
                        </div>
                    )}
                </div>
            )}

            {currentStep !== WORKFLOW_STEPS.SETUP && (
                <div className="d-flex justify-content-end mb-3">
                    <Button as="a" href="/Analysis?mode=resume" variant="outline-primary" onClick={() => clearSession()}>
                        View Another Analysis
                    </Button>
                </div>
            )}

            {/* Step 1: Species Selection */}
            {currentStep === WORKFLOW_STEPS.SETUP && (
                <div className={`${selectorStyles.container} mb-4`}>
                    <div className={analysisPageStyles.modeSwitcher}>
                        <Button
                            variant={workflowMode === WORKFLOW_MODES.NEW ? "success" : "outline-secondary"}
                            onClick={() => handleModeChange(WORKFLOW_MODES.NEW)}
                            className={analysisPageStyles.modeButton}
                            disabled={isStartingGenomeAnalysis || currentStep !== WORKFLOW_STEPS.SETUP}
                        >
                            Start New Analysis
                        </Button>
                        <Button
                            variant={workflowMode === WORKFLOW_MODES.RESUME ? "success" : "outline-secondary"}
                            onClick={() => handleModeChange(WORKFLOW_MODES.RESUME)}
                            className={analysisPageStyles.modeButton}
                            disabled={isStartingGenomeAnalysis || currentStep !== WORKFLOW_STEPS.SETUP}
                        >
                            Resume Existing
                        </Button>
                    </div>

                    <h4 className="text-center mb-3">
                        {workflowMode === WORKFLOW_MODES.RESUME
                            ? "Step 1: Resume a Previous Analysis"
                            : "Step 1: Select Species for Analysis"}
                    </h4>
                    <p className="text-muted text-center mb-4">
                        {workflowMode === WORKFLOW_MODES.RESUME
                            ? "Reconnect to an existing Galaxy history to pick up where you left off."
                            : `Choose ${MIN_SPECIES_COUNT}-${MAX_SPECIES_COUNT} species to compare. We'll download their complete genomes and find orthologous genes.`}
                    </p>
                    {workflowMode === WORKFLOW_MODES.NEW && (
                    <Form onSubmit={handleAddSpeciesSubmit}>
                        <div className={`${parametersStyles.searchAlignment} d-flex flex-column flex-lg-row gap-3`}>
                            <div className={parametersStyles.searchBar}>
                                <div className={parametersStyles.searchInputs}>
                                    <Form.Control
                                        as="textarea"
                                        rows={2}
                                        placeholder="Enter species (single or list). Separate with commas, semicolons, or new lines."
                                        value={speciesQuery}
                                        onChange={(e) => {
                                            setSpeciesQuery(e.target.value);
                                            setSpeciesSuggestionLimit(25);
                                        }}
                                        className={parametersStyles.speciesSearch}
                                        disabled={selectedSpecies.length >= MAX_SPECIES_COUNT}
                                    />
                                </div>
                            </div>
                            <Button
                                variant="primary"
                                size="lg"
                                className={parametersStyles.searchButton}
                                type="submit"
                                disabled={isVerifyingSpecies || selectedSpecies.length >= MAX_SPECIES_COUNT || (!speciesQuery.trim() && queuedSpecies.length === 0)}
                            >
                                {isVerifyingSpecies ? (
                                    <Spinner animation="border" role="status" size="sm">
                                        <span className="visually-hidden">Verifying…</span>
                                    </Spinner>
                                ) : (
                                    "Add Species"
                                )}
                            </Button>
                        </div>

                        {isLoadingSpeciesSuggestions && speciesQuery && speciesQuery.split(/[\n,;]+/).length === 1 && (
                                <div className="text-muted mt-2">Loading matching RefSeq annotated species...</div>
                        )}

                        {queuedSpecies.length > 0 && (
                            <div className={parametersStyles.queuedSpecies} aria-label="Genomes queued to add">
                                <span><strong>{queuedSpecies.length}</strong> queued</span>
                                {queuedSpecies.map((species) => <button type="button" key={species} onClick={() => handleSuggestionSelect(species)} title={`Remove ${species}`}>{species}<span aria-hidden="true">×</span></button>)}
                            </div>
                        )}

                        {speciesSuggestions.length > 0 && speciesQuery && speciesQuery.split(/[\n,;]+/).length === 1 && (
                            <>
                            <div className={parametersStyles.suggestionToolbar}>
                                <span>{speciesSuggestions.length} matching genomes</span>
                                <label>
                                    <span className="visually-hidden">Sort genome suggestions by</span>
                                    <select value={speciesSort} onChange={handleSpeciesSortChange} aria-label="Sort genome suggestions by">
                                        <option value="relevance">Search relevance</option>
                                        <option value="quality">Genome quality</option>
                                        <option value="alphabetical">Species name</option>
                                        <option value="publication">Publication date</option>
                                        <option value="assembly">Assembly completeness</option>
                                    </select>
                                </label>
                                <button
                                    type="button"
                                    className={parametersStyles.sortDirection}
                                    onClick={() => setSpeciesSortDirection((direction) => direction === "asc" ? "desc" : "asc")}
                                    aria-label={speciesSortDirection === "asc" ? "Sort descending" : "Sort ascending"}
                                    title={speciesSortDirection === "asc" ? "Currently ascending; click for descending" : "Currently descending; click for ascending"}
                                >
                                    <span className={speciesSortDirection === "asc" ? parametersStyles.triangleUp : parametersStyles.triangleDown} aria-hidden="true" />
                                </button>
                            </div>
                            <div className={parametersStyles.suggestionTray}>
                                {sortedSpeciesSuggestions.map((suggestion) => {
                                    const isQueued = queuedSpecies.includes(suggestion.scientific);
                                    return (
                                    <button
                                        key={suggestion.scientific}
                                        type="button"
                                        className={`${parametersStyles.suggestionChip} ${isQueued ? parametersStyles.suggestionSelected : ""}`}
                                        onClick={() => handleSuggestionSelect(suggestion.scientific)}
                                        disabled={selectedSpecies.length + queuedSpecies.length >= MAX_SPECIES_COUNT && !isQueued}
                                        aria-pressed={isQueued}
                                    >
                                        <span className={parametersStyles.suggestionScientific}>{suggestion.scientific}</span>
                                        <span className={parametersStyles.suggestionCommon}>{suggestion.common || "No common name listed"}</span>
                                        <span className={parametersStyles.suggestionMeta}><strong>Genome:</strong> {suggestion.accession}</span>
                                        <span className={parametersStyles.suggestionMeta}><strong>Assembly:</strong> {suggestion.assemblyLevel || "Not reported"}</span>
                                        <span className={parametersStyles.suggestionMeta}>
                                            <strong>Quality:</strong> {suggestion.scaffoldN50
                                                ? `Scaffold N50 ${Number(suggestion.scaffoldN50).toLocaleString()} bp`
                                                : suggestion.contigN50
                                                    ? `Contig N50 ${Number(suggestion.contigN50).toLocaleString()} bp`
                                                    : "N50 not reported"}
                                        </span>
                                        <span className={parametersStyles.suggestionMeta}>
                                            <strong>Annotation:</strong> {suggestion.annotationName || "NCBI RefSeq annotation"}
                                            {suggestion.buscoComplete != null ? ` · BUSCO ${(Number(suggestion.buscoComplete) * 100).toFixed(1)}%` : ""}
                                        </span>
                                        <span className={parametersStyles.suggestionMeta}><strong>Released:</strong> {suggestion.releaseDate || "Not reported"}</span>
                                        <span className={parametersStyles.selectionState}>{isQueued ? "Selected" : "Select genome"}</span>
                                    </button>
                                )})}
                            </div>
                            {speciesSuggestions.length >= speciesSuggestionLimit && speciesSuggestionLimit < 250 && (
                                <button
                                    type="button"
                                    className={parametersStyles.showMoreSuggestions}
                                    onClick={() => setSpeciesSuggestionLimit((current) => Math.min(current + 25, 250))}
                                    disabled={isLoadingSpeciesSuggestions}
                                >
                                    {isLoadingSpeciesSuggestions ? "Loading more..." : "Show 25 more genomes"}
                                </button>
                            )}
                            </>
                        )}

                        <div className={`${parametersStyles.alignment} mt-4`}>
                            <Form.Group controlId="galaxyApiKeyInput">
                                <div className="d-flex justify-content-between align-items-center">
                                    <Form.Label className="mb-1 fw-semibold">Galaxy Europe API Key <span aria-hidden="true">*</span></Form.Label>
                                    <Button
                                        variant="link"
                                        size="sm"
                                        type="button"
                                        aria-pressed={showGalaxyApiKey}
                                        onClick={() => setShowGalaxyApiKey((prev) => !prev)}
                                        className="p-0"
                                    >
                                        {showGalaxyApiKey ? "Hide" : "Show"}
                                    </Button>
                                </div>
                                <div className="d-flex flex-column flex-md-row gap-2">
                                    <Form.Control
                                        type={showGalaxyApiKey ? "text" : "password"}
                                        placeholder="Required: paste your API key from usegalaxy.eu"
                                        value={galaxyApiKey}
                                        onChange={(e) => {
                                            setGalaxyApiKey(e.target.value);
                                            setGalaxyKeyValidation("idle");
                                        }}
                                        autoComplete="new-password"
                                        required
                                        aria-invalid={galaxyKeyValidation === "invalid"}
                                    />
                                    <Button
                                        type="button"
                                        variant={galaxyKeyValidation === "valid" ? "success" : "outline-primary"}
                                        onClick={handleValidateGalaxyKey}
                                        disabled={!galaxyApiKey.trim() || galaxyKeyValidation === "checking"}
                                    >
                                        {galaxyKeyValidation === "checking" ? "Checking..." : galaxyKeyValidation === "valid" ? "Validated" : "Validate key"}
                                    </Button>
                                </div>
                                <Form.Text className={galaxyKeyValidation === "invalid" ? "text-danger" : "text-muted"}>
                                    {galaxyKeyValidation === "valid"
                                        ? "Key accepted. It remains only in this page's temporary memory and is never saved."
                                        : "Required to create and run jobs in your Galaxy account. Closing or refreshing this page clears the key."}
                                </Form.Text>
                            </Form.Group>
                        </div>

                        <div className="d-flex align-items-center justify-content-between mt-3 mb-2">
                            <span className="text-muted">Optional settings</span>
                            <Button
                                variant="outline-secondary"
                                size="sm"
                                type="button"
                                aria-expanded={advancedOptionsOpen}
                                onClick={() => setShowAdvancedOptions((prev) => !prev)}
                            >
                                {advancedOptionsOpen ? "Hide options" : "More options"}
                            </Button>
                        </div>

                        <Collapse in={advancedOptionsOpen}>
                            <div>
                                <div className={`${parametersStyles.alignment} mt-2`}>
                                    <Form.Group controlId="alignmentSelect">
                                        <Form.Label>Protein alignment tool</Form.Label>
                                        <Form.Select
                                            value={alignment}
                                            className={parametersStyles.alignmentSelect}
                                            disabled
                                            aria-describedby="alignmentToolHelp"
                                        >
                                            <option value="mafft">MAFFT</option>
                                        </Form.Select>
                                        <Form.Text id="alignmentToolHelp" className="text-muted">
                                            The validated downstream Galaxy workflow uses MAFFT before codon back-translation.
                                        </Form.Text>
                                    </Form.Group>
                                </div>

                                <div className={`${parametersStyles.alignment} mt-3`}>
                                    <Form.Group controlId="ncbiApiKeyInput">
                                        <div className="d-flex justify-content-between align-items-center">
                                            <Form.Label className="mb-1">NCBI API Key (optional)</Form.Label>
                                            <Button
                                                variant="link"
                                                size="sm"
                                                type="button"
                                                aria-pressed={showNcbiApiKey}
                                                onClick={() => setShowNcbiApiKey((prev) => !prev)}
                                                className="p-0"
                                            >
                                                {showNcbiApiKey ? "Hide" : "Show"}
                                            </Button>
                                        </div>
                                        <Form.Control
                                            type={showNcbiApiKey ? "text" : "password"}
                                            placeholder="Paste your NCBI key for higher request limits"
                                            value={ncbiApiKey}
                                            onChange={(e) => setNcbiApiKey(e.target.value)}
                                            autoComplete="off"
                                        />
                                        <Form.Text className="text-muted">
                                            Used for genome lookup and NCBI Datasets requests during this session.
                                        </Form.Text>
                                    </Form.Group>
                                </div>

                            </div>
                        </Collapse>

                        <div className={parametersStyles.feedbackBlock}>
                            {statusMessage && <div className={parametersStyles.statusText}>{statusMessage}</div>}
                            {errorMessage && <div className={parametersStyles.errorText}>{errorMessage}</div>}
                        </div>

                        <div className={`${parametersStyles.alignment} mt-4`}>
                            <div className={parametersStyles.selectedSpeciesHeader}>
                                <h5>Selected Species ({selectedSpecies.length}/{MAX_SPECIES_COUNT})</h5>
                                <div className={parametersStyles.viewSwitcher} role="group" aria-label="Selected species view">
                                    <button type="button" className={speciesDisplayMode === "list" ? parametersStyles.activeView : ""} aria-pressed={speciesDisplayMode === "list"} onClick={() => setSpeciesDisplayMode("list")}>List</button>
                                    <button type="button" className={speciesDisplayMode === "tree" ? parametersStyles.activeView : ""} aria-pressed={speciesDisplayMode === "tree"} onClick={() => setSpeciesDisplayMode("tree")} disabled={selectedSpecies.length < 2}>Phylogeny</button>
                                </div>
                            </div>
                            {selectedSpecies.length === 0 && (
                                <p className={parametersStyles.placeholder}>Add between three and ten species to begin.</p>
                            )}
                            {speciesDisplayMode === "list" && <ul className={parametersStyles.speciesList}>
                                {selectedSpecies.map((species) => (
                                    <li key={species} className={parametersStyles.speciesItem}>
                                        <div className={parametersStyles.speciesSummary}>
                                            <span className={parametersStyles.speciesName}>{species}</span>
                                        </div>
                                        <Button
                                            variant="outline-danger"
                                            size="sm"
                                            className={parametersStyles.removeButton}
                                            type="button"
                                            onClick={() => handleRemoveSpecies(species)}
                                        >
                                            Remove
                                        </Button>
                                    </li>
                                ))}
                            </ul>}
                            {speciesDisplayMode === "tree" && selectedSpecies.length >= 2 && <div className={parametersStyles.inputTreePanel}>
                                {inputTreeLoading && <div className={parametersStyles.treeStatus}><Spinner animation="border" size="sm" /> Extracting reference subtree...</div>}
                                {!inputTreeLoading && inputTreeError && <div className={parametersStyles.treeError} role="alert">{inputTreeError}</div>}
                                {!inputTreeLoading && inputTree && <>
                                    {renderInputTree()}
                                    <div className={parametersStyles.treeActions}>
                                        <Button
                                            type="button"
                                            variant="outline-primary"
                                            onClick={handleOpenTreeOfLifeComparison}
                                            disabled
                                        >
                                            {contextTreeLoading
                                                ? <><Spinner animation="border" size="sm" className="me-2" />Building context tree...</>
                                                : "External tree context unavailable in lab beta"}
                                        </Button>
                                    </div>
                                    {contextTreeError && <div className={parametersStyles.treeError} role="alert">{contextTreeError}</div>}
                                    <p className={parametersStyles.treeProvenance}>TimeTree divergence estimates are mapped onto an NCBI Taxonomy topology. Branches are scaled in millions of years.</p>
                                    <p className={parametersStyles.treeContextNote}>The external Big Tree Viewer overlay is disabled pending validation of reference coverage and taxon matching. The reference subtree above remains available; it is not an OrthoFinder-inferred species tree.</p>
                                    {inputTreeMaxDivergenceMa > dsSaturationWarningMa && <div className={parametersStyles.saturationWarning} role="alert">
                                        <strong>Divergence screening flag:</strong> this species set spans approximately {inputTreeMaxDivergenceMa.toFixed(1)} Ma, above the configured {dsSaturationWarningMa.toFixed(0)} Ma screening threshold. Divergence time alone does not establish synonymous-site saturation or invalidate an analysis. Check gene-specific dS estimates, alignment quality, lineage, and substitution-model fit.
                                    </div>}
                                    {inputTree.missingSpecies?.length > 0 && <p className={parametersStyles.treeWarning}>Not resolved: {inputTree.missingSpecies.join(", ")}</p>}
                                </>}
                            </div>}
                        </div>

                        <div className={parametersStyles.actionsRow}>
                            <Button
                                variant="success"
                                size="lg"
                                type="button"
                                onClick={handleStartGenomeAnalysis}
                                disabled={!canStartGenomeAnalysis}
                            >
                                {isStartingGenomeAnalysis ? (
                                    <Spinner animation="border" role="status" size="sm">
                                        <span className="visually-hidden">Starting…</span>
                                    </Spinner>
                                ) : (
                                    "Start Analysis"
                                )}
                            </Button>
                        </div>
                    </Form>
                    )}

                    {workflowMode === WORKFLOW_MODES.RESUME && (
                        <div className={analysisPageStyles.resumeCard}>
                            <div className={analysisPageStyles.resumeHeader}>
                                <h5 className="mb-0">Resume Existing Galaxy History</h5>
                                <Button
                                    variant="link"
                                    type="button"
                                    onClick={() => setShowGalaxyCredentialHelp((open) => !open)}
                                    aria-expanded={showGalaxyCredentialHelp}
                                    aria-controls="galaxy-credential-help"
                                    aria-label={showGalaxyCredentialHelp ? "Hide Galaxy setup help" : "Show Galaxy setup help"}
                                    title={showGalaxyCredentialHelp ? "Hide Galaxy setup help" : "Galaxy history and API key help"}
                                    className={analysisPageStyles.infoButton}
                                >
                                    <FontAwesomeIcon icon={faCircleInfo} aria-hidden="true" />
                                </Button>
                            </div>
                            <p className="text-muted">
                                Paste a Galaxy history ID to reuse a previous run without redownloading genomes. The workflow will locate the OrthoFinder job automatically.
                            </p>
                            <Collapse in={showGalaxyCredentialHelp}>
                                <div id="galaxy-credential-help" className={analysisPageStyles.galaxyHelp}>
                                    <section>
                                        <h6>Galaxy History ID</h6>
                                        <ol>
                                            <li>Open <a href="https://usegalaxy.eu" target="_blank" rel="noopener noreferrer">Galaxy Europe</a> and sign in to the account used for the analysis.</li>
                                            <li>Open <strong>Histories</strong> from the activity bar, then select the history you want to resume.</li>
                                            <li>Open that history in its full-page view. In the browser address, copy the value after <code>id=</code>. You may also paste the full URL here.</li>
                                        </ol>
                                    </section>
                                    <section>
                                        <h6>Galaxy API Key</h6>
                                        <ol>
                                            <li>In Galaxy, open <strong>User</strong>, then <strong>Preferences</strong>.</li>
                                            <li>Select <strong>Manage API key</strong>.</li>
                                            <li>Create a key if none exists, then copy it into the field below.</li>
                                        </ol>
                                        <a href="https://training.galaxyproject.org/training-material/faqs/galaxy/preferences_admin_api_key.html" target="_blank" rel="noopener noreferrer">View Galaxy's illustrated API-key guide</a>
                                    </section>
                                    <p><strong>Important:</strong> The key must come from the same Galaxy server and account that owns the history. Treat it like a password and do not share it.</p>
                                </div>
                            </Collapse>
                            <Form className="mb-3">
                                <div className="mb-2">
                                    <Form.Label className="fw-semibold">Galaxy History ID</Form.Label>
                                </div>
                                <div className="d-flex flex-column flex-lg-row align-items-stretch gap-3">
                                    <Form.Control
                                        type="text"
                                        placeholder="Enter a history ID or Galaxy history URL"
                                        value={resumeHistoryId}
                                        onChange={(e) => setResumeHistoryId(e.target.value)}
                                        className="flex-grow-1"
                                    />
                                    <div className="d-flex align-items-end w-100" style={{ maxWidth: "280px" }}>
                                        <Button
                                            variant="success"
                                            size="lg"
                                            type="button"
                                            onClick={handleResumeExistingHistory}
                                            disabled={isStartingGenomeAnalysis || !resumeGalaxyApiKey.trim()}
                                            className={`${analysisPageStyles.modeAction} w-100`}
                                        >
                                            {isStartingGenomeAnalysis ? (
                                                <Spinner animation="border" role="status" size="sm">
                                                    <span className="visually-hidden">Loading…</span>
                                                </Spinner>
                                            ) : (
                                                "Load Existing History"
                                            )}
                                        </Button>
                                    </div>
                                </div>

                                <div className="d-flex align-items-center justify-content-between mt-3">
                                    <div className="d-flex align-items-center gap-2">
                                        <Form.Label className="mb-0 fw-semibold">Galaxy API Key</Form.Label>
                                        <span className="text-danger small">Required</span>
                                    </div>
                                    <Button
                                        variant="link"
                                        size="sm"
                                        type="button"
                                        onClick={() => setShowResumeGalaxyApiKey((prev) => !prev)}
                                        className="p-0"
                                        aria-pressed={showResumeGalaxyApiKey}
                                    >
                                        {showResumeGalaxyApiKey ? "Hide" : "Show"}
                                    </Button>
                                </div>
                                <div className="mt-2">
                                    <Form.Control
                                        type={showResumeGalaxyApiKey ? "text" : "password"}
                                        placeholder="Paste the key that created this history"
                                        value={resumeGalaxyApiKey}
                                        onChange={(e) => setResumeGalaxyApiKey(e.target.value)}
                                        autoComplete="new-password"
                                        required
                                    />
                                    <Form.Text className="text-muted">
                                        Required to access the private history. It is kept only in this page's temporary memory and is never saved.
                                    </Form.Text>
                                </div>
                            </Form>

                            <div className={parametersStyles.feedbackBlock}>
                                {statusMessage && <div className={parametersStyles.statusText}>{statusMessage}</div>}
                                {errorMessage && <div className={parametersStyles.errorText}>{errorMessage}</div>}
                            </div>
                        </div>
                    )}
                </div>
            )}

            {/* Steps 2-3: Genome Analysis & Group Selection */}
            {(currentStep === WORKFLOW_STEPS.GENOME_ANALYSIS || 
              currentStep === WORKFLOW_STEPS.GROUP_SELECTION) && (
                <div className={`${selectorStyles.container} mb-4`}>
                    <div className="d-flex justify-content-between align-items-center mb-3">
                        <h4 className="mb-0">
                            {currentStep === WORKFLOW_STEPS.GENOME_ANALYSIS 
                                ? "Step 2: Downloading Genomes & Finding Orthologous Genes" 
                                : "Step 3: Select Orthologous Gene Families"}
                        </h4>
                        <Button
                            variant="outline-secondary"
                            onClick={handleBackToSpeciesSelection}
                            disabled={isStartingGenomeAnalysis || isAnalyzing}
                        >
                            Back to Species Selection
                        </Button>
                    </div>
                    
                    <OrthologousGroupSelector
                        groups={orthologousGroups}
                        selectedGroups={selectedGroups}
                        onGroupSelectionChange={handleGroupSelectionChange}
                        onSelectAll={handleSelectAll}
                        onSelectNone={handleSelectNone}
                        onAnalyzeSelected={handleAnalyzeSelected}
                        onAnalyzeAll={handleAnalyzeAll}
                        isLoading={isLoadingGroups}
                        errorMessage={errorMessage}
                        canAnalyze={!!genomeAnalysisData && orthologousGroups.length > 0}
                        isAnalyzing={isAnalyzing}
                        ncbiApiKey={ncbiApiKey}
                        onGroupsAnnotated={handleGroupsAnnotated}
                        genomes={genomeAnalysisData?.genomes?.length ? genomeAnalysisData.genomes : selectedSpecies}
                    />
                </div>
            )}

            {/* Step 4: Running Analysis */}
            {currentStep === WORKFLOW_STEPS.RUNNING_ANALYSIS && (
                <div className={`${selectorStyles.container} mb-4`}>
                    <div className="d-flex flex-wrap justify-content-between align-items-start gap-3 mb-3">
                        <div>
                            <h4 className="mb-1">Step 4: Running Selection Analysis</h4>
                            <p className="text-muted mb-0">Galaxy is processing the submitted orthogroups in collection batches.</p>
                        </div>
                        <div className="d-flex flex-wrap gap-2">
                            <Button variant="outline-danger" onClick={handleCancelAnalysis} disabled={isCancelingAnalysis}>
                                {isCancelingAnalysis ? <><Spinner animation="border" size="sm" className="me-2" />Canceling...</> : "Cancel analysis"}
                            </Button>
                            <Button variant="outline-secondary" onClick={handleStartNewAnalysis} disabled={isCancelingAnalysis || isAnalyzing}>
                                Start new analysis
                            </Button>
                        </div>
                    </div>

                    <Alert variant="info" className="d-flex align-items-center mb-3">
                        <Spinner animation="border" size="sm" className="me-3 flex-shrink-0" />
                        <div><strong>Analysis submitted to Galaxy.</strong><div>{statusMessage || "Waiting for Galaxy job progress..."}</div></div>
                    </Alert>

                    {([liveGalaxyStatus?.stage, currentJobInfo?.state].includes("hyphy_ready") ||
                      [liveGalaxyStatus?.stage, currentJobInfo?.state].includes("post_mafft_ready")) && (
                        <Alert variant="warning" className="mb-3">
                            <Alert.Heading as="h5">
                                {[liveGalaxyStatus?.stage, currentJobInfo?.state].includes("post_mafft_ready")
                                    ? "MAFFT is complete; resume the downstream workflow"
                                    : "Completed alignments are ready for HyPhy"}
                            </Alert.Heading>
                            <p>{[liveGalaxyStatus?.stage, currentJobInfo?.state].includes("post_mafft_ready")
                                ? "phyloDIVaS will reuse the completed MAFFT and filtered CDS collections, then submit back-translation, IQ-TREE, MEME, BUSTED, and aBSREL as one Galaxy workflow."
                                : "phyloDIVaS automatically resumes the missing HyPhy stage when this history is connected with a valid key. If recovery fails, review the reported error before retrying."} After Galaxy accepts the workflow, the key is cleared and localhost can close.</p>
                            {!resumeGalaxyApiKey.trim() && !galaxyApiKey.trim() && (
                                <Form.Group controlId="legacyHyphyGalaxyKey" className="mb-3">
                                    <Form.Label className="fw-semibold">Galaxy Europe API Key</Form.Label>
                                    <Form.Control
                                        type="password"
                                        value={resumeGalaxyApiKey}
                                        onChange={(event) => setResumeGalaxyApiKey(event.target.value)}
                                        autoComplete="new-password"
                                        placeholder="Enter the key for this Galaxy history"
                                    />
                                    <Form.Text>The key is used for this submission only and is not saved.</Form.Text>
                                </Form.Group>
                            )}
                            <Button
                                type="button"
                                variant="primary"
                                onClick={handleRetryHyphy}
                                disabled={isRetryingHyphy || (!resumeGalaxyApiKey.trim() && !galaxyApiKey.trim())}
                            >
                                {isRetryingHyphy
                                    ? <><Spinner animation="border" size="sm" className="me-2" />Submitting downstream workflow...</>
                                    : [liveGalaxyStatus?.stage, currentJobInfo?.state].includes("post_mafft_ready")
                                        ? "Resume after MAFFT"
                                        : "Submit HyPhy to Galaxy"}
                            </Button>
                        </Alert>
                    )}

                    <section className={analysisPageStyles.runningOrthoResults} aria-labelledby="running-ortho-results-title">
                        <div className={analysisPageStyles.runningOrthoHeader}>
                            <div>
                                <h5 id="running-ortho-results-title" className="mb-1">Explore completed OrthoFinder results</h5>
                                <p className="mb-0">
                                    Browse the species set and strict 1:1 orthogroups while Galaxy continues the downstream analysis.
                                </p>
                            </div>
                            <Button
                                type="button"
                                variant="outline-primary"
                                onClick={handleToggleRunningOrthoResults}
                                aria-expanded={showRunningOrthoResults}
                                aria-controls="running-ortho-results-content"
                            >
                                {showRunningOrthoResults ? "Hide results" : "Browse results"}
                            </Button>
                        </div>
                        <div className={analysisPageStyles.runningOrthoSummary} aria-live="polite">
                            <strong>{strictOrthologCount > 0 ? strictOrthologCount.toLocaleString() : "Ready"}</strong>
                            <span>{strictOrthologCount > 0 ? "strict 1:1 orthogroups available" : "OrthoFinder results available to load"}</span>
                            <span>Downstream Galaxy jobs continue independently.</span>
                        </div>
                        {showRunningOrthoResults && (
                            <div id="running-ortho-results-content" className={analysisPageStyles.runningOrthoContent}>
                                {orthoBrowseError && <Alert variant="warning">{orthoBrowseError}</Alert>}
                                <OrthologousGroupSelector
                                    groups={orthologousGroups}
                                    selectedGroups={selectedGroups}
                                    onGroupSelectionChange={handleGroupSelectionChange}
                                    onSelectAll={handleSelectAll}
                                    onSelectNone={handleSelectNone}
                                    onAnalyzeSelected={handleAnalyzeSelected}
                                    onAnalyzeAll={handleAnalyzeAll}
                                    isLoading={isLoadingGroups}
                                    errorMessage=""
                                    canAnalyze={false}
                                    isAnalyzing
                                    ncbiApiKey={ncbiApiKey}
                                    onGroupsAnnotated={handleGroupsAnnotated}
                                    genomes={genomeAnalysisData?.genomes?.length ? genomeAnalysisData.genomes : selectedSpecies}
                                    browseOnly
                                />
                            </div>
                        )}
                    </section>
                    <h5>Submitted orthogroups ({submittedGroups.length.toLocaleString()})</h5>
                    <div className={selectorStyles.submittedGroups}>
                        {submittedGroups.slice(0, 24).map((group) => (
                            <section key={group.groupId} className={selectorStyles.submittedGroup}>
                                <strong>{group.groupId}</strong>
                                {group.groupName && group.groupName !== group.groupId && <span>{group.groupName}</span>}
                                <dl>
                                    {Object.entries(group.speciesGenes || {}).map(([species, genes]) => (
                                        <div key={species}>
                                            <dt>{species.replaceAll('_', ' ')}</dt>
                                            <dd>{genes || "No gene ID"}</dd>
                                        </div>
                                    ))}
                                </dl>
                            </section>
                        ))}
                    </div>
                    {submittedGroups.length > 24 && (
                        <p className={selectorStyles.submittedOverflow}>
                            Showing 24 of {submittedGroups.length.toLocaleString()} submitted orthogroups. The complete batch is tracked in Galaxy.
                        </p>
                    )}
                    <Alert variant="secondary" className="mt-4 mb-0">
                        Another gene set can be selected after this Galaxy batch completes. This prevents overlapping collection workflows and duplicate submissions.
                    </Alert>
                </div>
            )}

            {/* Step 5: Results */}
            {currentStep === WORKFLOW_STEPS.COMPLETED && (
                <div className={`${selectorStyles.container} mb-4`}>
                    <h4 className="text-center mb-3">Galaxy Results</h4>
                    <Button className="mb-3" onClick={handleToggleRunningOrthoResults} disabled={isLoadingGroups}>Load OrthoFinder results and membership tables</Button>
                    {orthoBrowseError && <Alert variant="warning">{orthoBrowseError}</Alert>}
                    <Alert variant={workflowFailed ? "warning" : "info"} className="text-center">
                        <Alert.Heading>{workflowFailed ? "Galaxy reported a downstream failure" : "Galaxy outputs detected"}</Alert.Heading>
                        <p className="mb-0">{workflowFailed
                            ? statusMessage || "A downstream Galaxy job failed. No selection results are available for this run."
                            : "Review the method-specific results below. Workflow scheduling or available outputs alone do not establish that every gene completed successfully."}</p>
                    </Alert>

                    {workflowFailed && (
                        <div className={analysisPageStyles.retryPanel}>
                            <div>
                                <h5>Restart selection tests only</h5>
                                <p>
                                    Reuse the completed codon-alignment and IQ-TREE collections in this history, then run only
                                    MEME, BUSTED and aBSREL. Genome retrieval, OrthoFinder, filtering and alignment will not be repeated.
                                </p>
                            </div>
                            <Button
                                type="button"
                                variant="primary"
                                onClick={handleRetryHyphy}
                                disabled={isRetryingHyphy}
                            >
                                {isRetryingHyphy ? (
                                    <><Spinner animation="border" size="sm" className="me-2" />Checking Galaxy...</>
                                ) : "Retry HyPhy from completed alignments"}
                            </Button>
                        </div>
                    )}

                    {strictOrthologCount > 0 && (
                        <div className={analysisPageStyles.reusePanel}>
                            <div>
                                <p className={analysisPageStyles.reuseEyebrow}>Reuse OrthoFinder results</p>
                                <h5>Analyze a different gene set</h5>
                                <p>
                                    Choose from the same {strictOrthologCount.toLocaleString()} strict 1:1 N0 orthogroups across {analysisSpeciesCount} species.
                                    Genome retrieval, isoform filtering, and OrthoFinder will be skipped; only the newly selected genes enter the downstream Galaxy workflow.
                                </p>
                            </div>
                            <Button type="button" variant="primary" onClick={handleAnalyzeAnotherGeneSet}>
                                Choose another gene set
                            </Button>
                        </div>
                    )}

                    {(workflowSucceeded || workflowFailed) && (
                        <div className="mt-4">
                            <AnalysisResults
                                jobInfo={currentJobInfo}
                                statusMessage={statusMessage}
                                errorMessage=""
                                orthologousGroups={orthologousGroups}
                                analysisGroups={submittedGroups}
                                genomes={genomeAnalysisData?.genomes || selectedSpecies}
                                historyId={genomeAnalysisData?.historyId || activeHistoryId}
                                requestedGeneCount={currentJobInfo?.hyphyGeneCount || liveGalaxyStatus?.selectedOrthogroupCount || submittedGroups.length}
                            />
                        </div>
                    )}
                </div>
            )}

                    <div className={analysisPageStyles.citationsLink}>
                        <Link to="/Citations" className={analysisPageStyles.citationsText}>
                            View Research Tool Citations
                        </Link>
                    </div>

                </div>
            </div>
        </div>
    );
};

export default GenomeAnalysisPage;
