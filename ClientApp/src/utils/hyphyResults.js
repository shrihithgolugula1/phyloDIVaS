const headerText = (header) => {
    const value = Array.isArray(header) ? header[0] : header;
    return String(value ?? "")
        .replace(/&alpha;/gi, "alpha")
        .replace(/&beta;/gi, "beta")
        .replace(/<[^>]+>/g, "")
        .replace(/&[^;]+;/g, "")
        .replace(/[^a-z0-9+-]+/gi, " ")
        .trim()
        .toLowerCase();
};

const numericValue = (value) => {
    if (value === null || value === undefined || typeof value === "boolean" || (typeof value === "string" && !value.trim())) return null;
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
};
const probability = value => {
    const parsed = numericValue(value);
    return parsed !== null && parsed >= 0 && parsed <= 1 ? parsed : null;
};

const normalizedKey = (value) => String(value ?? "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "");

const objectValue = (object, ...keys) => {
    if (!object || typeof object !== "object") return undefined;
    const wanted = new Set(keys.map(normalizedKey));
    const entry = Object.entries(object).find(([key]) => wanted.has(normalizedKey(key)));
    return entry?.[1];
};

const normalizedMethod = (method) => {
    const value = String(method ?? "").toLowerCase();
    if (value.includes("absrel")) return "aBSREL";
    if (value.includes("meme")) return "MEME";
    return "BUSTED";
};

const isBranchAttribute = (value) => {
    if (!value || typeof value !== "object" || Array.isArray(value)) return false;
    return Object.keys(value).some((key) => [
        "correctedpvalue",
        "uncorrectedpvalue",
        "lrt",
        "rateclasses",
        "baselinemg94xrevomegaratio"
    ].includes(normalizedKey(key)));
};

const branchWasTested = (label, correctedPValue) => {
    if (correctedPValue !== null) return true;
    if (label === undefined || label === null) return false;
    const normalized = String(label).trim().toLowerCase();
    return normalized === "test" || normalized === "foreground" || normalized === "tested" || normalized === "1";
};

const partitionEntries = (value) => {
    if (!value || typeof value !== "object" || Array.isArray(value)) return [];
    const entries = Object.entries(value);
    const partitions = entries.filter(([key, branches]) =>
        normalizedKey(key) !== "attributes" &&
        branches &&
        typeof branches === "object" &&
        !Array.isArray(branches) &&
        Object.values(branches).some(isBranchAttribute));
    if (partitions.length > 0) return partitions;
    if (entries.some(([, branch]) => isBranchAttribute(branch))) {
        return [["0", value]];
    }
    return entries.filter(([, branches]) => branches && typeof branches === "object" && !Array.isArray(branches));
};

const extractOmegaClasses = (parsed) => {
    const fits = parsed?.fits || {};
    const unconstrained =
        fits["Unconstrained model"] ||
        fits.Unconstrained ||
        fits.UnconstrainedModel;
    const testDistribution = unconstrained?.["Rate Distributions"]?.Test;
    if (!testDistribution || typeof testDistribution !== "object") return [];

    return Object.values(testDistribution).flatMap((entry) => {
        const omega = numericValue(Array.isArray(entry) ? entry[0] : objectValue(entry, "omega", "w"));
        const proportion = numericValue(Array.isArray(entry) ? entry[1] : objectValue(entry, "proportion", "weight"));
        if (omega === null || proportion === null) return [];
        return [{
            omega,
            proportion,
            category: omega > 1 ? "positive" : omega === 1 ? "neutral" : "purifying"
        }];
    });
};

const modelFitValue = (fit, ...keys) => numericValue(objectValue(fit, ...keys));

const compactLocationList = (label, values, limit = 8) => {
    const visible = values.slice(0, limit);
    const remainder = values.length - visible.length;
    return `${label}: ${visible.join(", ")}${remainder > 0 ? `, and ${remainder} more` : ""}`;
};

export function extractMemeSiteResults(parsed, significanceThreshold = 0.1) {
    const mle = parsed?.MLE || parsed?.mle;
    const headers = Array.isArray(mle?.headers) ? mle.headers.map(headerText) : [];
    const content = mle?.content;
    if (!headers.length || !content || typeof content !== "object") return [];

    const findColumn = (...patterns) => headers.findIndex((header) =>
        patterns.some((pattern) => pattern.test(header)));
    const pValueColumn = findColumn(/^p[- ]?value$/, /p[- ]?value/);
    const lrtColumn = findColumn(/^lrt$/, /likelihood ratio/);
    const betaPlusColumn = findColumn(/beta\s*\+/, /positive.*rate/);

    return Object.entries(content).flatMap(([partition, rows]) => {
        if (!Array.isArray(rows)) return [];
        return rows.map((row, index) => {
            const values = Array.isArray(row) ? row : [];
            const pValue = pValueColumn >= 0 ? probability(values[pValueColumn]) : null;
            const lrt = lrtColumn >= 0 ? numericValue(values[lrtColumn]) : null;
            const betaPlus = betaPlusColumn >= 0 ? numericValue(values[betaPlusColumn]) : null;
            return {
                partition,
                site: index + 1,
                pValue,
                lrt,
                betaPlus,
                significant: pValue !== null && pValue <= significanceThreshold
            };
        });
    });
}

export function extractAbsrelBranchResults(parsed, significanceThreshold = 0.05) {
    const branchAttributes = parsed?.["branch attributes"] || parsed?.branch_attributes;
    const tested = parsed?.tested || {};

    return partitionEntries(branchAttributes).flatMap(([partition, branches]) =>
        Object.entries(branches).flatMap(([branchKey, attributes]) => {
            if (!attributes || typeof attributes !== "object" || Array.isArray(attributes)) return [];

            const correctedPValue = probability(objectValue(
                attributes,
                "Corrected P-value",
                "corrected p value",
                "corrected_p_value"
            ));
            const uncorrectedPValue = numericValue(objectValue(
                attributes,
                "Uncorrected P-value",
                "uncorrected p value",
                "uncorrected_p_value"
            ));
            const lrt = numericValue(objectValue(attributes, "LRT", "likelihood ratio test"));
            const rateClasses = numericValue(objectValue(attributes, "Rate classes", "rate_classes"));
            const baselineOmega = numericValue(objectValue(
                attributes,
                "Baseline MG94xREV omega ratio",
                "baseline omega ratio"
            ));
            const originalName = objectValue(attributes, "original name", "original_name");
            const testedLabel = tested?.[partition]?.[branchKey] ?? tested?.[branchKey];
            const wasTested = branchWasTested(testedLabel, correctedPValue);

            return [{
                partition,
                branch: String(originalName || branchKey),
                correctedPValue,
                uncorrectedPValue,
                lrt,
                rateClasses,
                baselineOmega,
                tested: wasTested,
                significant: wasTested && correctedPValue !== null && correctedPValue <= significanceThreshold
            }];
        })
    );
}

export function summarizeHyphyResult(parsed, method) {
    if (!parsed || typeof parsed !== "object") return null;

    const normalized = normalizedMethod(method);
    const testResults = parsed["test results"] || parsed.test_results || {};
    const input = parsed.input || {};
    const sequences = numericValue(objectValue(input, "number of sequences", "number_of_sequences"));
    const sites = numericValue(objectValue(input, "number of sites", "number_of_sites"));
    const omegaClasses = extractOmegaClasses(parsed);
    const omegaWeight = omegaClasses.reduce((sum, item) => sum + item.proportion, 0);
    const meanOmega = omegaWeight > 0
        ? omegaClasses.reduce((sum, item) => sum + (item.omega * item.proportion), 0) / omegaWeight
        : null;
    const omegaLT1 = omegaClasses.filter((item) => item.omega < 1).reduce((sum, item) => sum + item.proportion, 0);
    const omegaEQ1 = omegaClasses.filter((item) => item.omega === 1).reduce((sum, item) => sum + item.proportion, 0);
    const omegaGT1 = omegaClasses.filter((item) => item.omega > 1).reduce((sum, item) => sum + item.proportion, 0);
    const fits = parsed.fits || {};
    const unconstrained = fits["Unconstrained model"] || fits.Unconstrained || fits.UnconstrainedModel;
    const nullFit = fits["Null model"] || fits.Null || fits.NullModel;

    const base = {
        method: normalized,
        sequences,
        sites,
        omegaClasses,
        omegaLT1,
        omegaEQ1,
        omegaGT1,
        meanOmega,
        unconstrainedLogL: modelFitValue(unconstrained, "Log Likelihood", "log_likelihood", "log-likelihood"),
        nullLogL: modelFitValue(nullFit, "Log Likelihood", "log_likelihood", "log-likelihood"),
        selectedSites: 0,
        selectedBranches: 0,
        memeSiteResults: [],
        absrelBranchResults: [],
        threshold: normalized === "MEME" ? 0.1 : 0.05
    };

    if (normalized === "MEME") {
        const memeSiteResults = extractMemeSiteResults(parsed, base.threshold);
        const significantSites = memeSiteResults.filter((site) => site.significant);
        const pValues = memeSiteResults.map((site) => site.pValue).filter((value) => value !== null);
        const lrts = memeSiteResults.map((site) => site.lrt).filter((value) => value !== null);
        const resultAvailable = pValues.length > 0;

        return {
            ...base,
            resultAvailable,
            significant: resultAvailable && significantSites.length > 0,
            pValue: pValues.length ? Math.min(...pValues) : null,
            testStatistic: lrts.length ? Math.max(...lrts) : null,
            selectedSites: significantSites.length,
            memeSiteResults,
            callLabel: !resultAvailable
                ? "Result unavailable"
                : significantSites.length > 0
                    ? "Site-level selection detected"
                    : "No site-level selection detected",
            conclusion: !resultAvailable
                ? "The native MEME result does not contain a readable site table."
                : significantSites.length > 0
                    ? `MEME found evidence of episodic positive selection at ${significantSites.length} codon site${significantSites.length === 1 ? "" : "s"} (p <= 0.10).`
                    : "MEME found no codon sites with evidence of episodic positive selection at p <= 0.10.",
            location: significantSites.length > 0
                ? compactLocationList(
                    `Codon site${significantSites.length === 1 ? "" : "s"}`,
                    significantSites.map((site) => site.site)
                )
                : "No codon sites passed the MEME threshold."
        };
    }

    if (normalized === "aBSREL") {
        const absrelBranchResults = extractAbsrelBranchResults(parsed, base.threshold);
        const testedBranches = absrelBranchResults.filter((branch) => branch.tested);
        const significantBranches = testedBranches.filter((branch) => branch.significant);
        const correctedPValues = testedBranches.map((branch) => branch.correctedPValue).filter((value) => value !== null);
        const lrts = testedBranches.map((branch) => branch.lrt).filter((value) => value !== null);
        const resultAvailable = correctedPValues.length > 0;

        return {
            ...base,
            resultAvailable,
            significant: resultAvailable && significantBranches.length > 0,
            pValue: correctedPValues.length ? Math.min(...correctedPValues) : null,
            testStatistic: lrts.length ? Math.max(...lrts) : null,
            selectedBranches: significantBranches.length,
            testedBranches: testedBranches.length,
            absrelBranchResults,
            callLabel: !resultAvailable
                ? "Result unavailable"
                : significantBranches.length > 0
                    ? "Branch-level selection detected"
                    : "No branch-level selection detected",
            conclusion: !resultAvailable
                ? "The native aBSREL result does not contain readable corrected branch tests."
                : significantBranches.length > 0
                    ? `aBSREL found evidence of episodic positive selection on ${significantBranches.length} of ${testedBranches.length} tested branches (corrected p <= 0.05).`
                    : `aBSREL found no evidence of episodic positive selection among ${testedBranches.length} tested branches at corrected p <= 0.05.`,
            location: !resultAvailable
                ? "Branch test locations are unavailable."
                : significantBranches.length > 0
                    ? compactLocationList(
                        `Branch${significantBranches.length === 1 ? "" : "es"}`,
                        significantBranches.map((branch) => branch.branch)
                    )
                    : "No tested branches passed the corrected aBSREL threshold."
        };
    }

    const pValue = probability(objectValue(testResults, "p-value", "p_value", "p"));
    const testStatistic = numericValue(objectValue(testResults, "LRT", "test statistic", "test_statistic"));
    const resultAvailable = pValue !== null;
    const significant = resultAvailable && pValue <= base.threshold;

    return {
        ...base,
        resultAvailable,
        significant,
        pValue,
        testStatistic,
        callLabel: !resultAvailable
            ? "Result unavailable"
            : significant
                ? "Gene-wide selection detected"
                : "No gene-wide selection detected",
        conclusion: !resultAvailable
            ? "The native BUSTED result does not contain a readable gene-wide p-value."
            : significant
                ? "BUSTED found gene-wide evidence that at least one site on at least one tested branch experienced episodic positive selection (p <= 0.05)."
                : "BUSTED found no statistically significant gene-wide evidence of episodic positive selection at p <= 0.05.",
        location: significant
            ? "Gene-wide signal across the tested branches. BUSTED does not identify a specific branch or codon."
            : "No specific location is reported because the gene-wide test was not significant."
    };
}
