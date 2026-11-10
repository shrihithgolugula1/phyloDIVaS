import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import AnalysisResults from "./AnalysisResults";

describe("results loading boundary", () => {
    it("shows only the accessible loading screen before result retrieval, even for a completed Galaxy workflow", () => {
        const html = renderToStaticMarkup(<AnalysisResults
            historyId="test-history"
            requestedGeneCount={9978}
            jobInfo={{state: "workflow_complete", hyphyJsonOutputs: [
                {id: "dataset-1", method: "BUSTED", elementIdentifier: "N0.HOG0000001"}
            ]}}
        />);
        expect(html).toContain("Preparing your Galaxy results");
        expect(html).toContain('aria-busy="true"');
        expect(html).toContain('aria-label="Result files processed"');
        expect(html).not.toContain("Incomplete / skipped");
        expect(html).not.toContain("Analysis overview");
        expect(html).not.toContain("BUSTED p-value distribution");
        expect(html).not.toContain("Gene-Specific Analysis");
    });
});
