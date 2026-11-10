import AnalysisParameters from "../../AnalysisParameters/AnalysisParameters";
import AnalysisResults from "../../AnalysisResults/AnalysisResults";
import styles from './AnalysisPage.module.css';
import { getCandidates, pollJob, startDnDs } from "../../api/dndsApi";
import { filterSpecies } from "../../api/speciesCatalog";

const MIN_SPECIES_COUNT = 3;

const AnalysisPage = () => {
    const [gene, setGene] = useState("");
    const [speciesQuery, setSpeciesQuery] = useState("");
    const [selectedSpecies, setSelectedSpecies] = useState([]);
    const [candidates, setCandidates] = useState({});
    const [selectedIds, setSelectedIds] = useState({});
    const [alignment, setAlignment] = useState("mafft");
    const [statusMessage, setStatusMessage] = useState("Add a gene and at least 3 species to begin.");
    const [statusProgress, setStatusProgress] = useState(0);
    const [jobInfo, setJobInfo] = useState(null);
    const [isLoadingCandidates, setIsLoadingCandidates] = useState(false);
    const [isVerifyingSpecies, setIsVerifyingSpecies] = useState(false);
    const [isStartingAnalysis, setIsStartingAnalysis] = useState(false);
    const [analysisError, setAnalysisError] = useState(null);
    const [stopPolling, setStopPolling] = useState(null);

    useEffect(() => {
        return () => {
            stopPolling?.();
        };
    }, [stopPolling]);

    const speciesSuggestions = useMemo(() => {
        return filterSpecies(speciesQuery).filter((s) => !selectedSpecies.includes(s.scientific));
    }, [speciesQuery, selectedSpecies]);

    const jobActive = useMemo(() => {
        const state = jobInfo?.state;
        if (!state) return false;
        return !["ok", "error", "failed"].includes(state);
    }, [jobInfo]);

    const verifySpecies = useCallback(async (speciesName) => {
        const base = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esearch.fcgi";
        const params = new URLSearchParams({
            db: "nuccore",
            retmode: "json",
            retmax: "1",
            term: `${speciesName}[Organism] AND (cds)`
        });

        const url = `${base}?${params.toString()}`;
        console.log("Verifying species via:", url);
        const resp = await fetch(url, { cache: "no-store" });
        if (!resp.ok) return false;

        const data = await resp.json();
        const count = parseInt(data?.esearchresult?.count || "0", 10);
        return count > 0;
    }, []);

    const handleAddSpecies = useCallback(async (raw) => {
        const speciesName = (raw ?? speciesQuery).trim();
        if (!speciesName) {
            setAnalysisError("Enter a species name to add.");
            return false;
        }
        if (selectedSpecies.includes(speciesName)) {
            setAnalysisError(`${speciesName} is already selected.`);
            return false;
        }

        setAnalysisError(null);
        setIsVerifyingSpecies(true);
        setStatusMessage(`Verifying ${speciesName}…`);
        try {
            const exists = await verifySpecies(speciesName);
            if (!exists) {
                setStatusMessage(`Species not found in NCBI: ${speciesName}`);
                setAnalysisError(`Species not found in NCBI: ${speciesName}`);
                return false;
            }
            setSelectedSpecies((prev) => [...prev, speciesName]);
            setStatusMessage(`Added ${speciesName}. Select a CDS once candidates load.`);
            setSpeciesQuery("");
            return true;
        } catch (e) {
            const msg = e?.message || String(e);
            setStatusMessage(`Error verifying species: ${msg}`);
            setAnalysisError(msg);
            return false;
        } finally {
            setIsVerifyingSpecies(false);
        }
    }, [speciesQuery, selectedSpecies, verifySpecies]);

    const handleRemoveSpecies = useCallback((speciesName) => {
        setSelectedSpecies((prev) => prev.filter((s) => s !== speciesName));
        setCandidates((prev) => {
            const next = { ...prev };
            delete next[speciesName];
            return next;
        });
        setSelectedIds((prev) => {
            const next = { ...prev };
            delete next[speciesName];
            return next;
        });
    }, []);

    useEffect(() => {
        if (!gene.trim() || selectedSpecies.length === 0) {
            setCandidates({});
            setIsLoadingCandidates(false);
            if (selectedSpecies.length > 0) {
                setStatusMessage("Enter a gene to load CDS candidates.");
            }
            return;
        }

        let cancelled = false;
        setIsLoadingCandidates(true);
        const debounce = setTimeout(async () => {
            try {
                const result = await getCandidates({ gene: gene.trim(), species: selectedSpecies });
                if (!cancelled) {
                    setCandidates(result);
                    setStatusMessage("Select a CDS for each species.");
                }
            } catch (e) {
                if (!cancelled) {
                    const msg = e?.message || String(e);
                    setAnalysisError(msg);
                    setStatusMessage(`Error loading CDS candidates: ${msg}`);
                }
            } finally {
                if (!cancelled) {
                    setIsLoadingCandidates(false);
                }
            }
        }, 450);

        return () => {
            cancelled = true;
            clearTimeout(debounce);
        };
    }, [gene, selectedSpecies]);

    useEffect(() => {
        setSelectedIds((prev) => {
            const next = { ...prev };
            let mutated = false;

            selectedSpecies.forEach((speciesName) => {
                const list = candidates?.[speciesName] || [];
                if (list.length === 0) {
                    if (next[speciesName]) {
                        delete next[speciesName];
                        mutated = true;
                    }
                    return;
                }
                if (!next[speciesName] || !list.some((cand) => cand.uid === next[speciesName])) {
                    next[speciesName] = list[0].uid;
                    mutated = true;
                }
            });

            Object.keys(next).forEach((speciesName) => {
                if (!selectedSpecies.includes(speciesName)) {
                    delete next[speciesName];
                    mutated = true;
                }
            });

            return mutated ? next : prev;
        });
    }, [candidates, selectedSpecies]);

    const handleSelectCandidate = useCallback((speciesName, candidateId) => {
        setSelectedIds((prev) => {
            if (prev[speciesName] === candidateId) return prev;
            return { ...prev, [speciesName]: candidateId };
        });
    }, []);

    const handleSuggestionSelect = useCallback((scientificName) => {
        setSpeciesQuery(scientificName);
    }, []);

    const canStartAnalysis = useMemo(() => {
        const trimmedGene = gene.trim();
        if (!trimmedGene) return false;
        if (selectedSpecies.length < MIN_SPECIES_COUNT) return false;
        return selectedSpecies.every((speciesName) => selectedIds[speciesName]);
    }, [gene, selectedSpecies, selectedIds]);

    const handleStartAnalysis = useCallback(async () => {
        setAnalysisError(null);
        const trimmedGene = gene.trim();
        if (!trimmedGene) {
            setAnalysisError("Enter a gene to analyze.");
            return;
        }
        if (selectedSpecies.length < MIN_SPECIES_COUNT) {
            setAnalysisError(`Select at least ${MIN_SPECIES_COUNT} species.`);
            return;
        }
        const filteredIds = {};
        selectedSpecies.forEach((speciesName) => {
            const id = selectedIds[speciesName];
            if (id) filteredIds[speciesName] = id;
        });
        if (Object.keys(filteredIds).length !== selectedSpecies.length) {
            setAnalysisError("Select a CDS for every species.");
            return;
        }

    stopPolling?.();
    setStopPolling(null);
    setJobInfo(null);
        setIsStartingAnalysis(true);
        setStatusProgress(10);
        setStatusMessage("Starting dN/dS pipeline…");

        try {
            const payload = {
                gene: trimmedGene,
                species: selectedSpecies,
                alignment,
                selectedIds: filteredIds,
            };

            const resp = await startDnDs(payload);
            const jobId = resp?.jobId ?? resp?.id ?? resp?.JobId ?? resp?.jobID;
            if (!jobId) {
                throw new Error("Invalid API response: missing jobId.");
            }

            setStatusMessage("BUSTED submitted. Polling…");
            setStatusProgress(20);
            setJobInfo({ jobId, state: "submitted" });

            const pollCancel = pollJob(jobId, {
                intervalMs: 6000,
                onTick: (job) => {
                    setJobInfo(job);
                    const state = job?.state || "unknown";
                    let pct = 30;
                    if (state === "new" || state === "queued") pct = 25;
                    else if (state === "running") pct = 60;
                    else if (state === "ok") pct = 100;
                    else if (state === "error" || state === "failed") pct = 100;
                    if (state === "ok") setStatusMessage("Galaxy job complete.");
                    else if (state === "error" || state === "failed") setStatusMessage(`Galaxy job: ${state}`);
                    else setStatusMessage(`Galaxy job: ${state}`);
                    setStatusProgress(pct);
                    if (["ok", "error", "failed"].includes(state)) {
                        setStopPolling(null);
                        setIsStartingAnalysis(false);
                    }
                },
                onError: (err) => {
                    const msg = err?.message || String(err);
                    setAnalysisError(msg);
                    setStatusMessage(`Error polling job: ${msg}`);
                    setStatusProgress(100);
                    setIsStartingAnalysis(false);
                },
            });
            setStopPolling(() => pollCancel);
        } catch (e) {
            const msg = e?.message || String(e);
            setAnalysisError(msg);
            setStatusMessage(`Error starting pipeline: ${msg}`);
            setStatusProgress(0);
            setStopPolling(null);
        } finally {
            setIsStartingAnalysis(false);
        }
    }, [alignment, gene, selectedIds, selectedSpecies, stopPolling]);

    return (
        <div>
            <h2 className={styles.title}>dN/dS Analysis</h2>
            <br />
            <AnalysisParameters
                gene={gene}
                onGeneChange={setGene}
                speciesQuery={speciesQuery}
                onSpeciesQueryChange={setSpeciesQuery}
                speciesSuggestions={speciesSuggestions}
                onSpeciesSuggestionSelect={handleSuggestionSelect}
                onAddSpecies={handleAddSpecies}
                onRemoveSpecies={handleRemoveSpecies}
                selectedSpecies={selectedSpecies}
                candidates={candidates}
                selectedIds={selectedIds}
                onSelectCandidate={handleSelectCandidate}
                alignment={alignment}
                onAlignmentChange={setAlignment}
                onStartAnalysis={handleStartAnalysis}
                canStartAnalysis={canStartAnalysis}
                isVerifyingSpecies={isVerifyingSpecies}
                isLoadingCandidates={isLoadingCandidates}
                isStartingAnalysis={isStartingAnalysis}
                jobActive={jobActive}
                statusMessage={statusMessage}
                errorMessage={analysisError || ""}
            />
            <br />
            <AnalysisResults
                jobInfo={jobInfo}
                statusMessage={statusMessage}
                statusProgress={statusProgress}
                errorMessage={analysisError || ""}
            />
        </div>
    );
};

export default AnalysisPage;