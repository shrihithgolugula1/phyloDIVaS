import React, { useState, useEffect } from "react";
import ResultsTable from "../../Table/ResultsTable/ResultsTable";
import SearchBar from "../../SearchBar/SearchBar";
import ExpandedGenome from "./ExpandedInfo/ExpandedGenome";
import styles from "./GenomeSearchPage.module.css";
import { Link } from "react-router-dom";
import {saveSearch} from "../../../api/searchHistoryApiCalls.js";
import {getGenomeFasta} from "../../../api/downloadGenomeAPICall.js";
import { suggestSpecies } from "../../../api/dndsApi.js";

const GenomeSearchPage = () => {
    // Table headers and subheaders
    const HEADERS = [
        { id: 1, label: "Organism" },
        { id: 2, label: "RefSeq accession" },
        { id: 3, label: "Assembly" },
        { id: 4, label: "Quality" },
    ];
    const COLUMNS = [
        { key: "organismLabel", render: (row) => <div><strong>{row.organism.name}</strong><span className={styles.secondary}>{row.organism.commonName}</span></div> },
        { key: "accession" },
        { key: "assemblyLabel", render: (row) => <div>{row.assembly.name}<span className={styles.secondary}>{row.assembly.level}</span></div> },
        { key: "quality", render: (row) => <div><strong>{row.quality.n50}</strong><span className={styles.secondary}>{row.quality.busco}</span></div> },
    ];

    function getLastSearch(){
        var lastSearch = sessionStorage.getItem('lastSearch');
        console.log("getLastSearch triggered: " + lastSearch)
        
        if (lastSearch !== null){
            sessionStorage.removeItem('lastSearch')
            return lastSearch;
        }else{
            return "";
        }
    }

    // State for search and results
    const [searchQuery, setSearchQuery] = useState(getLastSearch);
    const [results, setResults] = useState([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);
    const [downloadError, setDownloadError] = useState(null);


    // Fetch results when search query changes
    useEffect(() => {
        let active = true;
        setDownloadError(null);
        const fetchResults = async () => {
            if (!searchQuery.trim()) {
                setResults([]);
                setError(null);
                setLoading(false);
                return;
            }

            setLoading(true);
            setError(null);
            
            try {
                const suggestions = await suggestSpecies({ query: searchQuery, limit: 25 });
                if (!active) return;

                    const formattedResults = suggestions.map(item => ({
                        accession: item.accession || "N/A",
                        organism: {
                            name: item.scientific || "N/A",
                            commonName: item.common || "No common name listed"
                        },
                        assembly: {
                            name: item.assemblyName || "N/A",
                            level: item.assemblyLevel || "N/A"
                        },
                        quality: {
                            n50: item.scaffoldN50 ? `N50 ${Number(item.scaffoldN50).toLocaleString()} bp` : "N50 unavailable",
                            busco: item.buscoComplete != null ? `BUSCO ${(Number(item.buscoComplete) * 100).toFixed(1)}% complete` : "BUSCO unavailable"
                        },
                        // Store the full item for expanded view
                        _fullData: {
                            ...item,
                            scientificName: item.scientific,
                            commonName: item.common,
                            assemblyStatus: item.isReference ? "RefSeq reference genome" : "RefSeq annotated assembly"
                        },
                        _taxId: item.taxId || -1,
                        _geneId: -1,
                        _assemblyId: item.accession || "-1",

                        _saveAction: async (row = {}) => {
                            console.log("=== SAVE ACTION TRIGGERED ===");
                            try {
                                const result = await saveSearch(
                                    sessionStorage.getItem('idToken'),
                                    "genome",
                                    searchQuery,
                                    row._taxId || -1,
                                    row._geneId || -1,
                                    row._assemblyId || "-1"
                                );
                                console.log("Search saved successfully for:", searchQuery);
                                
                            } catch (err) {
                                console.error("Error saving search:", err);
                            }
                        },

                        _downloadAction: async (row = {}) => {
                            console.log("=== Download ACTION TRIGGERED ===: " + row.accession);
                            setDownloadError(null);
                            try {
                                await getGenomeFasta(row.accession)
                                //call download api method here 
                                console.log("Download successful");
                            } catch (err) {
                                if (active) setDownloadError(`Failed to download sequence: ${err.message || "Please try again."}`);
                            }
                        },

                        _geneSearchAction: async (row = {}) => {
                            console.log("=== GENE SEARCH ACTION TRIGGERED ===: " + row.organism.name);

                          
                            try {
                                window.location.href = `/GeneSearch?taxon=${encodeURIComponent(row.organism.name)}`
                                console.log("successful");
                            } catch (err) {
                                console.error("Error ", err);
                            }
                        }


                    }));
                    
                    setResults(formattedResults);
            } catch (err) {
                if (!active) return;
                setError(`Failed to fetch results: ${err.message}`);
                setResults([]);
            } finally {
                if (active) setLoading(false);
            }
        };

        fetchResults();
        return () => { active = false; };
    }, [searchQuery]);

    return (
        <div className={styles.body}>
            <main className={styles.searchSection}>
                <div className={styles.searchContainer}>
                    <header className={styles.pageHeader}>
                        <p className={styles.eyebrow}>NCBI RefSeq data explorer</p>
                        <h1>Search genomes</h1>
                        <p>Find annotated reference assemblies by common name, scientific name, clade, taxonomy ID, or RefSeq accession.</p>
                    </header>
                    <nav className={styles.searchTabs} aria-label="Data search type">
                        <Link className={styles.activeTab} to="/Search">Genomes</Link>
                        <Link to="/GeneSearch">Genes</Link>
                    </nav>

                    <SearchBar
                        onSearch={setSearchQuery}
                        placeholder="Common name, scientific name, clade, tax ID, or RefSeq accession"
                        name="genomeSearchBar"
                        filter="GENOME"
                        initialValue={searchQuery}
                    />

                    <div className={styles.sourceNote}><strong>Source:</strong> NCBI Taxonomy, Datasets, RefSeq, and BUSCO assembly metadata. Results are limited to annotated RefSeq assemblies.</div>

                    {downloadError && <div className={styles.error} role="alert">{downloadError}</div>}

                    {error && <div className={styles.error} role="alert"><strong>Search unavailable.</strong> {error}</div>}

                    {loading && <div className={styles.loading} role="status"><span className={styles.spinner} />Searching NCBI and ranking assemblies...</div>}

                    {!loading && results.length > 0 && (
                        <section aria-labelledby="genome-results">
                        <div className={styles.resultSummary}><h2 id="genome-results">Genome results</h2><span>{results.length} annotated {results.length === 1 ? "assembly" : "assemblies"}</span></div>
                        <ResultsTable
                            rows={results}
                            headers={HEADERS}
                            includeSubHeader={false}
                            columns={COLUMNS}
                            expandedContent={(rowData) => <ExpandedGenome data={rowData._fullData} />}
                        />
                        </section>
                    )}

                    {!loading && results.length === 0 && searchQuery && !error && (
                        <div className={styles.noResults}>No results found. Try a different query.</div>
                    )}
                </div>
            </main>

        </div>
    );
};

export default GenomeSearchPage;
