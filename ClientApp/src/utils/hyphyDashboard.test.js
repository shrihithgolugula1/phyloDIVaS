import { describe, expect, it } from "vitest";
describe("partial retrieval and statistical family", () => {
    it("does not attach membership from a substring orthogroup match", () => {
        const genes = buildNormalizedGeneResults([
            {elementIdentifier: "N0.HOG1", method: "BUSTED"}
        ], [{groupId: "N0.HOG10", speciesGenes: {Human: "XP_123"}}]);
        expect(genes[0].speciesGenes).toEqual({});
    });
    it("keeps unloaded methods pending and withholds interim BH conclusions", () => {
        const genes = buildNormalizedGeneResults([], [], { requestedGroupIds: ["N0.HOG0000001"], loading: true });
        expect(genes[0].methodStatus.BUSTED).toBe("pending");
        expect(genes[0].bustedQValue).toBeNull();
    });
    it("distinguishes retrieval failure from a Galaxy job error", () => {
        const genes = buildNormalizedGeneResults([
            { elementIdentifier: "N0.HOG0000001", method: "BUSTED", error: new Error("504") },
            { elementIdentifier: "N0.HOG0000001", method: "MEME", json: {state: "error"} }
        ]);
        expect(genes[0].methodStatus.BUSTED).toBe("unreadable");
        expect(genes[0].methodStatus.MEME).toBe("failed");
    });
    it("preserves zero and independently known BH adjustments while excluding null", () => {
        const q = benjaminiHochberg([{id:"a",pValue:0}, {id:"b",pValue:0.02}, {id:"c",pValue:0.04}, {id:"missing",pValue:null}]);
        expect(q.get("a")).toBe(0);
        expect(q.get("b")).toBeCloseTo(0.03);
        expect(q.get("c")).toBeCloseTo(0.04);
        expect(q.has("missing")).toBe(false);
    });
});
describe("Galaxy collection gene inventory", () => {
    const ids = ["N0.HOG0005248", "N0.HOG0007914"];
    const outputs = ["BUSTED", "aBSREL", "MEME"].flatMap(method => ids.map(elementIdentifier => ({ method, elementIdentifier })));
    it("counts two genes across six results and ignores duplicates and tool labels", () => {
        const inventory = reconcileHyphyCollections([...outputs, outputs[0], { method: "BUSTED", elementIdentifier: "HyPhy-BUSTED" }]);
        expect(inventory.geneIds).toEqual(ids);
        expect(inventory.counts).toEqual({ BUSTED: 2, aBSREL: 2, MEME: 2 });
        expect(inventory.consistent).toBe(true);
    });
    it("detects different gene sets even when counts agree", () => {
        expect(reconcileHyphyCollections(outputs.map((item, i) => i === 5 ? { ...item, elementIdentifier: "N0.HOG0009999" } : item)).consistent).toBe(false);
    });
    it("requires all three tools, and handles empty collections", () => {
        expect(reconcileHyphyCollections(outputs.filter(item => item.method !== "MEME")).consistent).toBe(false);
        expect(reconcileHyphyCollections([])).toEqual({ geneIds: [], counts: { BUSTED: 0, aBSREL: 0, MEME: 0 }, consistent: false });
    });
});
import {
    benjaminiHochberg,
    buildLineageSelectionMatrix,
    buildNormalizedGeneResults,
    calculateAnalysisCompletion,
    reconcileHyphyCollections,
    calculateGoEnrichment,
    DEFAULT_THRESHOLDS,
    evaluateGeneEvidence,
    interpretGene,
    overlapCounts,
    rowsToDelimited
} from "./hyphyDashboard";

const card = (method, group, summary) => ({
    method,
    elementIdentifier: group,
    summary,
    json: { id: `${method}-${group}` }
});

const completeCards = (group, bustedP = 0.01) => [
    card("BUSTED", group, { resultAvailable: true, pValue: bustedP, sequences: 8, sites: 300 }),
    card("aBSREL", group, {
        resultAvailable: true,
        sequences: 8,
        sites: 300,
        absrelBranchResults: [
            { branch: "XP_1.2", correctedPValue: 0.01, tested: true },
            { branch: "Node4", correctedPValue: 0.02, tested: true }
        ]
    }),
    card("MEME", group, {
        resultAvailable: true,
        sequences: 8,
        sites: 300,
        memeSiteResults: [
            { site: 12, pValue: 0.04 },
            { site: 44, pValue: 0.4 }
        ]
    })
];

describe("benjaminiHochberg", () => {
    it("produces monotonic q-values in the original identity space", () => {
        const q = benjaminiHochberg([
            { id: "a", pValue: 0.01 },
            { id: "b", pValue: 0.04 },
            { id: "c", pValue: 0.03 },
            { id: "missing", pValue: null }
        ]);
        expect(q.get("a")).toBeCloseTo(0.03);
        expect(q.get("c")).toBeCloseTo(0.04);
        expect(q.get("b")).toBeCloseTo(0.04);
        expect(q.has("missing")).toBe(false);
    });
});

describe("buildNormalizedGeneResults", () => {
    it("combines three method files into one annotated gene record", () => {
        const groups = [{
            groupId: "N0.HOG0001",
            groupName: "schlafen family member 11",
            annotation: {
                symbol: "SLFN11",
                name: "schlafen family member 11",
                aliases: ["SLFN-11"]
            },
            speciesGenes: { Human: "XP_1.2", Chimp: "XP_2.1" }
        }];
        const genes = buildNormalizedGeneResults(completeCards("N0.HOG0001"), groups, {
            expectedSpeciesCount: 8,
            requestedGroupIds: ["N0.HOG0001"]
        });
        expect(genes).toHaveLength(1);
        expect(genes[0].displayName).toBe("SLFN11");
        expect(genes[0].analysisStatus).toBe("complete");
        expect(genes[0].bustedQValue).toBe(0.01);
        expect(genes[0].searchText).toContain("slfn-11");
    });

    it("keeps missing and failed methods distinct from nonsignificant results", () => {
        const cards = [
            card("BUSTED", "N0.HOG2", { resultAvailable: true, pValue: 0.8, sequences: 8, sites: 100 }),
            card("MEME", "N0.HOG2", { resultAvailable: false, memeSiteResults: [] })
        ];
        const [gene] = buildNormalizedGeneResults(cards, [], { requestedGroupIds: ["N0.HOG2"] });
        expect(gene.methodStatus.BUSTED).toBe("complete");
        expect(gene.methodStatus.MEME).toBe("failed");
        expect(gene.methodStatus.aBSREL).toBe("missing");
        expect(gene.analysisStatus).toBe("partial");
        expect(gene.warnings.join(" ")).toContain("Missing aBSREL");
    });

    it("creates a traceable missing record for a requested gene with no outputs", () => {
        const [gene] = buildNormalizedGeneResults([], [{ groupId: "N0.HOG3", groupName: "ABC1" }], {
            requestedGroupIds: ["N0.HOG3"]
        });
        expect(gene.analysisStatus).toBe("missing");
        expect(gene.orthogroupId).toBe("N0.HOG3");
    });
});

describe("calculateAnalysisCompletion", () => {
    it("reports two fully readable genes as 100 percent complete", () => {
        const result = calculateAnalysisCompletion([
            { analysisStatus: "complete" },
            { analysisStatus: "complete" }
        ], 2);
        expect(result).toEqual({ requested: 2, complete: 2, incomplete: 0, fraction: 1 });
    });

    it("preserves the Galaxy request count while result hydration is pending", () => {
        expect(calculateAnalysisCompletion([], 2)).toEqual({
            requested: 2,
            complete: 0,
            incomplete: 2,
            fraction: 0
        });
    });

    it("falls back to discovered genes and clamps invalid counts", () => {
        expect(calculateAnalysisCompletion([{ analysisStatus: "complete" }], -5).requested).toBe(1);
        expect(calculateAnalysisCompletion([], Number.NaN).requested).toBe(0);
    });
});

describe("selection evidence", () => {
    const [gene] = buildNormalizedGeneResults(completeCards("N0.HOG4"), [{
        groupId: "N0.HOG4",
        groupName: "Test gene",
        speciesGenes: { Species_A: "XP_1.2" }
    }], { expectedSpeciesCount: 8 });

    it("uses BUSTED FDR, corrected aBSREL p-values, and raw MEME site p-values", () => {
        const evidence = evaluateGeneEvidence(gene, DEFAULT_THRESHOLDS);
        expect(evidence.bustedFdr).toBe(true);
        expect(evidence.selectedBranches).toBe(2);
        expect(evidence.selectedSites).toBe(1);
    });

    it("generates a deterministic interpretation without a consensus score", () => {
        const interpretation = interpretGene(gene, DEFAULT_THRESHOLDS);
        expect(interpretation).toContain("BH correction");
        expect(interpretation).toContain("2 branches");
        expect(interpretation).toContain("1 codon site");
    });

    it("maps only accession-backed terminal branches across genes", () => {
        const matrix = buildLineageSelectionMatrix([gene], DEFAULT_THRESHOLDS);
        expect(matrix.species).toEqual(["Species A"]);
        expect(matrix.rows[0].values).toHaveLength(1);
        expect(matrix.rows[0].values[0].species).toBe("Species A");
    });

    it("reports cross-method intersections", () => {
        const combinations = overlapCounts([gene], DEFAULT_THRESHOLDS);
        expect(combinations[0].key).toBe("BUSTED + aBSREL + MEME");
        expect(combinations[0].count).toBe(1);
    });
});

describe("GO enrichment safeguards", () => {
    const term = { id: "GO:0001", name: "response to stimulus", namespace: "biological_process" };
    const background = Array.from({ length: 20 }, (_, index) => ({
        id: `g${index}`,
        goTerms: index < 5 ? [term] : []
    }));

    it("uses only real structured GO terms and the tested-gene background", () => {
        const target = [background[0], background[1], background[2], background[10]];
        const results = calculateGoEnrichment(target, background);
        expect(results).toHaveLength(1);
        expect(results[0].id).toBe("GO:0001");
        expect(results[0].geneCount).toBe(3);
        expect(results[0].backgroundCount).toBe(5);
        expect(results[0].qValue).toBeGreaterThanOrEqual(0);
    });

    it("does not invent enrichment when annotations are absent", () => {
        expect(calculateGoEnrichment([{ id: "a", goTerms: [] }], [{ id: "a", goTerms: [] }])).toEqual([]);
    });
});

describe("exports and scale", () => {
    it("escapes spreadsheet delimiters safely", () => {
        const csv = rowsToDelimited([{ gene: "A, B", note: "quoted \"text\"" }], [
            { label: "Gene", value: "gene" },
            { label: "Note", value: "note" }
        ]);
        expect(csv).toContain('"A, B"');
        expect(csv).toContain('"quoted ""text"""');
    });

    it("normalizes a representative large analysis without dropping genes", () => {
        const cards = Array.from({ length: 2000 }, (_, index) => completeCards(`N0.HOG${index}`)).flat();
        const genes = buildNormalizedGeneResults(cards, [], {});
        expect(genes).toHaveLength(2000);
        expect(genes.every((gene) => gene.analysisStatus === "complete")).toBe(true);
    });
});

