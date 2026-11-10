const METHODS = ["BUSTED", "aBSREL", "MEME"];

export function reconcileHyphyCollections(outputs = []) {
    const methodIds = Object.fromEntries(METHODS.map((method) => [method, new Set()]));
    for (const output of outputs) {
        const method = METHODS.find((value) => value.toLowerCase() === String(output.method || "").toLowerCase());
        const match = String(output.elementIdentifier || "").match(/\bN\d+\.HOG\d+\b/i);
        if (method && match) methodIds[method].add(match[0].toUpperCase());
    }
    const geneIds = [...new Set(Object.values(methodIds).flatMap((ids) => [...ids]))].sort();
    return {
        geneIds,
        counts: Object.fromEntries(METHODS.map((method) => [method, methodIds[method].size])),
        consistent: geneIds.length > 0 && METHODS.every((method) => geneIds.every((id) => methodIds[method].has(id)))
    };
}

export function calculateAnalysisCompletion(genes = [], requestedCount = 0) {
    const numericRequested = Number(requestedCount);
    const safeRequestedCount = Number.isFinite(numericRequested)
        ? Math.max(0, Math.floor(numericRequested))
        : 0;
    const requested = Math.max(safeRequestedCount, genes.length);
    const complete = Math.min(
        requested,
        genes.filter((gene) => gene?.analysisStatus === "complete").length
    );
    return {
        requested,
        complete,
        incomplete: Math.max(0, requested - complete),
        fraction: requested > 0 ? complete / requested : 0
    };
}

export const DEFAULT_THRESHOLDS = Object.freeze({
    bustedP: 0.05,
    bustedQ: 0.05,
    absrelP: 0.05,
    memeP: 0.1
});

export const normalizeIdentifier = (value = "") => String(value)
    .trim()
    .toLowerCase()
    .replace(/\.(json|md|markdown|fasta|fa|fas|fna|nwk|newick)$/i, "")
    .replace(/^(busted|absrel|meme)\s*[-_:]\s*/i, "")
    .replace(/[^a-z0-9]+/g, "_")
    .replace(/^_+|_+$/g, "");

const normalizeAccession = (value = "") => String(value)
    .trim()
    .replace(/^['"]|['"]$/g, "")
    .replace(/\.\d+$/, "")
    .toLowerCase();

const asArray = (value) => {
    if (Array.isArray(value)) return value;
    if (value === null || value === undefined || value === "") return [];
    return String(value).split(/[,;|]/);
};

const uniqueStrings = (values) => [...new Set((Array.isArray(values) ? values : [values])
    .flatMap(asArray)
    .map((value) => String(value).trim())
    .filter(Boolean))];

const numeric = (value) => {
    if (value === null || value === undefined || typeof value === "boolean" || (typeof value === "string" && !value.trim())) return null;
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
};

const methodName = (card) => {
    const raw = String(card?.method || card?.name || "").toLowerCase();
    if (raw.includes("absrel")) return "aBSREL";
    if (raw.includes("meme")) return "MEME";
    return "BUSTED";
};

const groupAccessions = (group) => uniqueStrings([
    ...(Array.isArray(group?.geneIds) ? group.geneIds : []),
    ...Object.values(group?.speciesGenes || {})
]);

const annotationAliases = (group) => uniqueStrings([
    group?.aliases,
    group?.geneAliases,
    group?.annotation?.aliases,
    group?.annotation?.otherAliases,
    group?.annotation?.synonyms
]);

const geneMetadata = (group, orthogroupId) => {
    const annotation = group?.annotation || {};
    const symbol = String(
        annotation.symbol ||
        annotation.geneSymbol ||
        annotation.nomenclatureSymbol ||
        group?.geneSymbol ||
        group?.symbol ||
        ""
    ).trim();
    const fullName = String(
        annotation.fullName ||
        annotation.nomenclatureName ||
        annotation.name ||
        group?.groupName ||
        ""
    ).trim();
    const description = String(
        annotation.description ||
        annotation.summary ||
        fullName ||
        ""
    ).trim();
    const aliases = annotationAliases(group);
    const accessions = groupAccessions(group);
    return {
        symbol: symbol || null,
        fullName: fullName && normalizeIdentifier(fullName) !== normalizeIdentifier(orthogroupId)
            ? fullName
            : null,
        description: description || null,
        aliases,
        accessions,
        displayName: symbol || fullName || orthogroupId,
        annotationSourceUrl: annotation.sourceUrl || null,
        goTerms: Array.isArray(annotation.goTerms) ? annotation.goTerms : []
    };
};

export function benjaminiHochberg(entries) {
    const valid = entries
        .map((entry, index) => ({
            id: entry.id ?? index,
            pValue: numeric(entry.pValue)
        }))
        .filter((entry) => entry.pValue !== null && entry.pValue >= 0 && entry.pValue <= 1)
        .sort((left, right) => left.pValue - right.pValue);
    const adjusted = new Map();
    let runningMinimum = 1;
    for (let index = valid.length - 1; index >= 0; index -= 1) {
        const rank = index + 1;
        runningMinimum = Math.min(runningMinimum, (valid[index].pValue * valid.length) / rank);
        adjusted.set(valid[index].id, Math.min(1, runningMinimum));
    }
    return adjusted;
}

const buildGroupIndex = (groups) => {
    const index = new Map();
    groups.forEach((group) => {
        [group?.groupId, group?.groupName].filter(Boolean).forEach((key) => {
            index.set(String(key), group);
            index.set(normalizeIdentifier(key), group);
        });
    });
    return index;
};

const findGroup = (index, identifier) => {
    // Stable IDs are indexed once. Substring scans were quadratic for large
    // histories and could incorrectly join HOG1 to HOG10.
    return index.get(identifier) || index.get(normalizeIdentifier(identifier)) || null;
};

const methodStatus = (card) => {
    if (!card) return "missing";
    const state = card.json?.state;
    if (["error", "failed", "discarded"].includes(state)) return "failed";
    if (["deleted", "inaccessible", "paused"].includes(state)) return state;
    if (["new", "queued", "running", "waiting", "upload"].includes(state)) return "pending";
    if (card.pending) return "pending";
    if (card.error) return "unreadable";
    if (card.error || !card.summary?.resultAvailable) return "failed";
    return "complete";
};

export function buildNormalizedGeneResults(cards = [], groups = [], options = {}) {
    const expectedSpeciesCount = Number(options.expectedSpeciesCount) || null;
    const groupIndex = buildGroupIndex(groups);
    const cardsByGene = new Map();
    cards.forEach((card) => {
        const identifier = card?.elementIdentifier || card?.name;
        if (!identifier) return;
        const key = normalizeIdentifier(identifier);
        const current = cardsByGene.get(key) || { identifier, cards: {} };
        current.cards[methodName(card)] = card;
        cardsByGene.set(key, current);
    });

    const requestedIds = uniqueStrings(options.requestedGroupIds || []);
    requestedIds.forEach((identifier) => {
        const key = normalizeIdentifier(identifier);
        if (!cardsByGene.has(key)) cardsByGene.set(key, { identifier, cards: {} });
    });

    const genes = [...cardsByGene.entries()].map(([key, entry]) => {
        const group = findGroup(groupIndex, entry.identifier) ||
            Object.values(entry.cards).find((card) => card?.geneSet)?.geneSet ||
            null;
        const orthogroupId = group?.groupId || entry.identifier;
        const metadata = geneMetadata(group, orthogroupId);
        const statuses = Object.fromEntries(METHODS.map((method) => [method, !entry.cards[method] && options.loading ? "pending" : methodStatus(entry.cards[method])]));
        const completeMethodCount = METHODS.filter((method) => statuses[method] === "complete").length;
        const failedMethodCount = METHODS.filter((method) => statuses[method] === "failed").length;
        const alignmentLengths = METHODS
            .map((method) => numeric(entry.cards[method]?.summary?.sites))
            .filter((value) => value !== null);
        const sequenceCounts = METHODS
            .map((method) => numeric(entry.cards[method]?.summary?.sequences))
            .filter((value) => value !== null);
        const alignmentLength = alignmentLengths.length ? Math.max(...alignmentLengths) : null;
        const speciesCount = sequenceCounts.length ? Math.max(...sequenceCounts) : null;
        const warnings = [];
        const missingMethods = METHODS.filter((method) => statuses[method] === "missing");
        const failedMethods = METHODS.filter((method) => statuses[method] === "failed");
        if (missingMethods.length) warnings.push(`Missing ${missingMethods.join(", ")} result${missingMethods.length === 1 ? "" : "s"}.`);
        if (failedMethods.length) warnings.push(`Unreadable or failed ${failedMethods.join(", ")} result${failedMethods.length === 1 ? "" : "s"}.`);
        if (expectedSpeciesCount && speciesCount !== null && speciesCount < expectedSpeciesCount) {
            warnings.push(`${speciesCount} of ${expectedSpeciesCount} expected species are represented.`);
        }
        if (new Set(alignmentLengths).size > 1) warnings.push("HyPhy methods report different alignment lengths.");
        if (!metadata.symbol && !metadata.fullName) warnings.push("Reference gene annotation is unavailable; the orthogroup ID is shown.");

        const analysisStatus = completeMethodCount === METHODS.length
            ? "complete"
            : completeMethodCount > 0
                ? "partial"
                : failedMethodCount > 0
                    ? "failed"
                    : "missing";
        const speciesCoverage = expectedSpeciesCount && speciesCount !== null
            ? Math.min(1, speciesCount / expectedSpeciesCount)
            : null;
        const speciesGenes = group?.speciesGenes || {};

        return {
            id: key,
            orthogroupId,
            ...metadata,
            group,
            speciesGenes,
            methods: entry.cards,
            methodStatus: statuses,
            analysisStatus,
            completeMethodCount,
            alignmentLength,
            speciesCount,
            expectedSpeciesCount,
            speciesCoverage,
            warnings,
            searchText: [
                metadata.symbol,
                metadata.fullName,
                metadata.description,
                orthogroupId,
                ...metadata.aliases,
                ...metadata.accessions
            ].filter(Boolean).join(" ").toLowerCase()
        };
    });

    const bustedQValues = benjaminiHochberg(genes.map((gene) => ({
        id: gene.id,
        pValue: gene.methodStatus.BUSTED === "complete" ? gene.methods.BUSTED?.summary?.pValue : null
    })));

    return genes.map((gene) => ({
        ...gene,
        bustedPValue: numeric(gene.methods.BUSTED?.summary?.pValue),
        bustedQValue: options.loading ? null : bustedQValues.get(gene.id) ?? null
    })).sort((left, right) => left.displayName.localeCompare(right.displayName, undefined, {
        numeric: true,
        sensitivity: "base"
    }));
}

export function evaluateGeneEvidence(gene, thresholds = DEFAULT_THRESHOLDS) {
    const bustedP = numeric(gene?.bustedPValue);
    const bustedQ = numeric(gene?.bustedQValue);
    const memeSites = (gene?.methods?.MEME?.summary?.memeSiteResults || [])
        .filter((site) => numeric(site.pValue) !== null && Number(site.pValue) <= thresholds.memeP);
    const absrelBranches = (gene?.methods?.aBSREL?.summary?.absrelBranchResults || [])
        .filter((branch) => branch.tested && numeric(branch.correctedPValue) !== null && Number(branch.correctedPValue) <= thresholds.absrelP);
    return {
        bustedNominal: bustedP !== null && bustedP <= thresholds.bustedP,
        bustedFdr: bustedQ !== null && bustedQ <= thresholds.bustedQ,
        meme: memeSites.length > 0,
        absrel: absrelBranches.length > 0,
        memeSites,
        absrelBranches,
        selectedSites: memeSites.length,
        selectedBranches: absrelBranches.length,
        any: (bustedQ !== null && bustedQ <= thresholds.bustedQ) || memeSites.length > 0 || absrelBranches.length > 0
    };
}

export function interpretGene(gene, thresholds = DEFAULT_THRESHOLDS) {
    const evidence = evaluateGeneEvidence(gene, thresholds);
    const statements = [];
    if (gene.methodStatus.BUSTED === "complete") {
        if (evidence.bustedFdr) {
            statements.push(`BUSTED supports gene-wide episodic positive selection after across-gene BH correction (q <= ${thresholds.bustedQ}).`);
        } else if (gene.bustedQValue === null) {
            statements.push("BUSTED FDR assessment is pending retrieval of the full result family.");
        } else if (evidence.bustedNominal) {
            statements.push(`BUSTED is nominally significant at p <= ${thresholds.bustedP}, but does not pass the selected across-gene FDR threshold.`);
        } else {
            statements.push(`BUSTED does not detect gene-wide selection at p <= ${thresholds.bustedP}.`);
        }
    } else {
        statements.push("A readable BUSTED result is unavailable.");
    }
    if (gene.methodStatus.aBSREL === "complete") {
        statements.push(evidence.absrel
            ? `aBSREL identifies ${evidence.selectedBranches} branch${evidence.selectedBranches === 1 ? "" : "es"} after HyPhy's branch-level correction.`
            : `aBSREL identifies no branches at corrected p <= ${thresholds.absrelP}.`);
    } else {
        statements.push("A readable aBSREL result is unavailable.");
    }
    if (gene.methodStatus.MEME === "complete") {
        statements.push(evidence.meme
            ? `MEME identifies ${evidence.selectedSites} codon site${evidence.selectedSites === 1 ? "" : "s"} at raw p <= ${thresholds.memeP}.`
            : `MEME identifies no codon sites at raw p <= ${thresholds.memeP}.`);
    } else {
        statements.push("A readable MEME result is unavailable.");
    }
    return statements.join(" ");
}

export function overlapCounts(genes, thresholds = DEFAULT_THRESHOLDS) {
    const combinations = new Map();
    genes.forEach((gene) => {
        const evidence = evaluateGeneEvidence(gene, thresholds);
        const methods = [
            evidence.bustedFdr ? "BUSTED" : null,
            evidence.absrel ? "aBSREL" : null,
            evidence.meme ? "MEME" : null
        ].filter(Boolean);
        const comparable = METHODS.every(method => gene.methodStatus[method] === "complete") && gene.bustedQValue !== null;
        const key = !comparable ? "Insufficient results for cross-method comparison" : methods.length ? methods.join(" + ") : "No detected evidence";
        const current = combinations.get(key) || { key, methods, count: 0, genes: [] };
        current.count += 1;
        current.genes.push(gene);
        combinations.set(key, current);
    });
    return [...combinations.values()].sort((left, right) => right.count - left.count || left.key.localeCompare(right.key));
}

const branchSpecies = (gene, branchName) => {
    const branch = normalizeAccession(branchName);
    if (!branch || /^node\d*$/i.test(String(branchName).trim())) return null;
    for (const [species, identifiers] of Object.entries(gene?.speciesGenes || {})) {
        const matches = uniqueStrings(identifiers).map(normalizeAccession);
        if (matches.includes(branch)) return species.replaceAll("_", " ");
    }
    return null;
};

export function buildLineageSelectionMatrix(genes, thresholds = DEFAULT_THRESHOLDS) {
    const species = new Set();
    const rows = genes.flatMap((gene) => {
        const branches = gene?.methods?.aBSREL?.summary?.absrelBranchResults || [];
        const values = branches.flatMap((branch) => {
            const mappedSpecies = branchSpecies(gene, branch.branch);
            if (!mappedSpecies || !branch.tested || numeric(branch.correctedPValue) === null) return [];
            species.add(mappedSpecies);
            return [{
                geneId: gene.id,
                gene,
                species: mappedSpecies,
                pValue: Number(branch.correctedPValue),
                significant: Number(branch.correctedPValue) <= thresholds.absrelP
            }];
        });
        return values.length ? [{ gene, values }] : [];
    });
    return {
        species: [...species].sort((left, right) => left.localeCompare(right)),
        rows
    };
}

const normalizeGoTerm = (term) => {
    if (!term) return null;
    if (typeof term === "string") return { id: term, name: term, namespace: "Unspecified" };
    const id = term.id || term.goId || term.accession;
    if (!id) return null;
    return {
        id: String(id),
        name: String(term.name || term.term || id),
        namespace: String(term.namespace || term.aspect || "Unspecified")
    };
};

const logChoose = (n, k) => {
    if (k < 0 || k > n) return Number.NEGATIVE_INFINITY;
    const size = Math.min(k, n - k);
    let result = 0;
    for (let index = 1; index <= size; index += 1) {
        result += Math.log(n - size + index) - Math.log(index);
    }
    return result;
};

const hypergeometricTail = (population, populationSuccesses, draws, observed) => {
    const maximum = Math.min(populationSuccesses, draws);
    let probability = 0;
    for (let successes = observed; successes <= maximum; successes += 1) {
        const logProbability = logChoose(populationSuccesses, successes) +
            logChoose(population - populationSuccesses, draws - successes) -
            logChoose(population, draws);
        probability += Math.exp(logProbability);
    }
    return Math.min(1, probability);
};

export function calculateGoEnrichment(targetGenes, backgroundGenes) {
    const background = backgroundGenes.filter((gene) => gene.goTerms.map(normalizeGoTerm).filter(Boolean).length > 0);
    const backgroundIds = new Set(background.map((gene) => gene.id));
    const target = targetGenes.filter((gene) => backgroundIds.has(gene.id));
    if (!background.length || !target.length || target.length >= background.length) return [];

    const terms = new Map();
    background.forEach((gene) => {
        const seen = new Set();
        gene.goTerms.map(normalizeGoTerm).filter(Boolean).forEach((term) => {
            if (seen.has(term.id)) return;
            seen.add(term.id);
            const current = terms.get(term.id) || { ...term, backgroundGenes: new Set(), targetGenes: new Set() };
            current.backgroundGenes.add(gene.id);
            terms.set(term.id, current);
        });
    });
    target.forEach((gene) => {
        gene.goTerms.map(normalizeGoTerm).filter(Boolean).forEach((term) => terms.get(term.id)?.targetGenes.add(gene.id));
    });

    const results = [...terms.values()].flatMap((term) => {
        const backgroundCount = term.backgroundGenes.size;
        const geneCount = term.targetGenes.size;
        if (backgroundCount < 2 || geneCount < 1) return [];
        const pValue = hypergeometricTail(background.length, backgroundCount, target.length, geneCount);
        const expected = (target.length * backgroundCount) / background.length;
        return [{
            id: term.id,
            name: term.name,
            namespace: term.namespace,
            pValue,
            geneCount,
            backgroundCount,
            foldEnrichment: expected > 0 ? geneCount / expected : null,
            genes: [...term.targetGenes]
        }];
    });
    const qValues = benjaminiHochberg(results.map((result) => ({ id: result.id, pValue: result.pValue })));
    return results.map((result) => ({ ...result, qValue: qValues.get(result.id) ?? null }))
        .sort((left, right) => left.qValue - right.qValue || right.foldEnrichment - left.foldEnrichment);
}

const escapeDelimited = (value, delimiter) => {
    if (value === null || value === undefined) return "";
    const text = Array.isArray(value) ? value.join("; ") : String(value);
    if (text.includes(delimiter) || /["\r\n]/.test(text)) return `"${text.replaceAll('"', '""')}"`;
    return text;
};

export function rowsToDelimited(rows, columns, delimiter = ",") {
    const header = columns.map((column) => escapeDelimited(column.label, delimiter)).join(delimiter);
    const lines = rows.map((row) => columns
        .map((column) => escapeDelimited(typeof column.value === "function" ? column.value(row) : row[column.value], delimiter))
        .join(delimiter));
    return [header, ...lines].join("\n");
}

export function normalizedGeneRows(genes, thresholds = DEFAULT_THRESHOLDS) {
    return genes.map((gene) => {
        const evidence = evaluateGeneEvidence(gene, thresholds);
        return {
            gene: gene.displayName,
            symbol: gene.symbol,
            fullName: gene.fullName,
            orthogroupId: gene.orthogroupId,
            status: gene.analysisStatus,
            bustedP: gene.bustedPValue,
            bustedQ: gene.bustedQValue,
            bustedFdrSignificant: evidence.bustedFdr,
            absrelSignificantBranches: evidence.selectedBranches,
            memeSignificantSites: evidence.selectedSites,
            alignmentCodons: gene.alignmentLength,
            speciesRepresented: gene.speciesCount,
            expectedSpecies: gene.expectedSpeciesCount,
            warnings: gene.warnings
        };
    });
}

export const GENE_EXPORT_COLUMNS = [
    { label: "Gene", value: "gene" },
    { label: "Symbol", value: "symbol" },
    { label: "Full name", value: "fullName" },
    { label: "Orthogroup", value: "orthogroupId" },
    { label: "Analysis status", value: "status" },
    { label: "BUSTED p-value", value: "bustedP" },
    { label: "BUSTED BH q-value", value: "bustedQ" },
    { label: "BUSTED FDR significant", value: "bustedFdrSignificant" },
    { label: "Significant aBSREL branches", value: "absrelSignificantBranches" },
    { label: "Significant MEME sites", value: "memeSignificantSites" },
    { label: "Alignment length (codons)", value: "alignmentCodons" },
    { label: "Species represented", value: "speciesRepresented" },
    { label: "Expected species", value: "expectedSpecies" },
    { label: "Quality warnings", value: "warnings" }
];

export { METHODS };
