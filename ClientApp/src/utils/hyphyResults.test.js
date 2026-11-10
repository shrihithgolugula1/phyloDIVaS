import { describe, expect, it } from "vitest";
describe("unavailable probability values", () => {
    it.each([null, "", " ", false, -0.1, 1.1])("does not interpret %s as selection", value => {
        const result = summarizeHyphyResult({"test results": {"p-value": value}}, "BUSTED");
        expect(result.resultAvailable).toBe(false);
        expect(result.pValue).toBeNull();
    });
    it("preserves a genuine zero p-value", () => {
        expect(summarizeHyphyResult({"test results": {"p-value": 0}}, "BUSTED").pValue).toBe(0);
    });
    it("requires readable MEME tests, not just site rows", () => {
        expect(summarizeHyphyResult({MLE: {headers: [["p-value"]], content: {0: [[null]]}}}, "MEME").resultAvailable).toBe(false);
    });
});
import {
    extractAbsrelBranchResults,
    extractMemeSiteResults,
    summarizeHyphyResult
} from "./hyphyResults";

describe("extractMemeSiteResults", () => {
    const memeResult = {
        input: { "number of sequences": 8, "number of sites": 2 },
        MLE: {
            headers: [
                ["&alpha;", "synonymous rate"],
                ["&beta;<sup>+</sup>", "positive rate"],
                ["LRT", "likelihood ratio test"],
                ["p-value", "site-level p-value"]
            ],
            content: {
                "0": [
                    [0.2, 4.1, 8.5, 0.012],
                    [0.3, 1.2, 0.4, 0.81]
                ]
            }
        }
    };

    it("parses standard HyPhy-MEME site p-values and LRT statistics", () => {
        expect(extractMemeSiteResults(memeResult)).toEqual([
            { partition: "0", site: 1, pValue: 0.012, lrt: 8.5, betaPlus: 4.1, significant: true },
            { partition: "0", site: 2, pValue: 0.81, lrt: 0.4, betaPlus: 1.2, significant: false }
        ]);
    });

    it("produces an explicit MEME site-level selection call", () => {
        const summary = summarizeHyphyResult(memeResult, "MEME");
        expect(summary.significant).toBe(true);
        expect(summary.selectedSites).toBe(1);
        expect(summary.location).toContain("Codon site: 1");
        expect(summary.sequences).toBe(8);
    });

    it("returns an empty list for non-MEME JSON", () => {
        expect(extractMemeSiteResults({ "test results": { "p-value": 0.01 } })).toEqual([]);
    });
});

describe("extractAbsrelBranchResults", () => {
    const absrelResult = {
        tested: {
            "0": { Human: "test", Chimpanzee: "test", Mouse: "background" }
        },
        "branch attributes": {
            "0": {
                Human: {
                    "Corrected P-value": 0.012,
                    "Uncorrected P-value": 0.004,
                    LRT: 8.4,
                    "Rate classes": 3,
                    "Baseline MG94xREV omega ratio": 0.4
                },
                Chimpanzee: {
                    "Corrected P-value": 0.64,
                    LRT: 0.2,
                    "Rate classes": 1
                },
                Mouse: {
                    LRT: 0,
                    "Rate classes": 1
                }
            },
            attributes: {
                "Corrected P-value": { "attribute type": "branch label", "display order": 6 },
                LRT: { "attribute type": "branch label", "display order": 4 }
            }
        }
    };

    it("uses corrected branch p-values and excludes untested branches", () => {
        const branches = extractAbsrelBranchResults(absrelResult);
        expect(branches).toHaveLength(3);
        expect(branches.find((branch) => branch.branch === "Human")?.significant).toBe(true);
        expect(branches.find((branch) => branch.branch === "Mouse")?.tested).toBe(false);
    });

    it("reports the selected branches in the aBSREL conclusion", () => {
        const summary = summarizeHyphyResult(absrelResult, "aBSREL");
        expect(summary.significant).toBe(true);
        expect(summary.selectedBranches).toBe(1);
        expect(summary.testedBranches).toBe(2);
        expect(summary.location).toBe("Branch: Human");
    });
});

describe("summarizeHyphyResult", () => {
    it("interprets a BUSTED p-value as a gene-wide result", () => {
        const summary = summarizeHyphyResult({
            input: { "number of sequences": 8, "number of sites": 450 },
            "test results": { LRT: 9.7, "p-value": 0.008 },
            fits: {
                "Unconstrained model": {
                    "Log Likelihood": -100,
                    "Rate Distributions": {
                        Test: {
                            "0": [0.2, 0.7],
                            "1": [1, 0.2],
                            "2": [3.5, 0.1]
                        }
                    }
                },
                "Null model": { "Log Likelihood": -108 }
            }
        }, "BUSTED");

        expect(summary.significant).toBe(true);
        expect(summary.callLabel).toBe("Gene-wide selection detected");
        expect(summary.omegaClasses).toHaveLength(3);
        expect(summary.omegaGT1).toBeCloseTo(0.1);
        expect(summary.location).toContain("does not identify a specific branch or codon");
    });

    it("does not call selection when a method-specific result is unreadable", () => {
        const summary = summarizeHyphyResult({}, "BUSTED");
        expect(summary.resultAvailable).toBe(false);
        expect(summary.significant).toBe(false);
        expect(summary.callLabel).toBe("Result unavailable");
    });
});
