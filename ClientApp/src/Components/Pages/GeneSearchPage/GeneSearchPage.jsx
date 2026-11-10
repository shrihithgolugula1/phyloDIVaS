import React, { useState, useEffect } from "react";
import ResultsTable from "../../Table/ResultsTable/ResultsTable";
import SearchBar from "../../SearchBar/SearchBar";
import ExpandedGene from "./ExpandedInfo/ExpandedGene";
import { Link } from "react-router-dom";
import {getGeneFasta} from "../../../api/downloadGenomeAPICall.js";
import {saveSearch} from "../../../api/searchHistoryApiCalls.js";
import { API_BASES, apiUrl } from "../../../api/config.js";

import styles from './GeneSearchPage.module.css';
import { useNavigate } from "react-router-dom";

const GeneSearchPage = () => {
    const navigate = useNavigate();

    // these should not change 
    const HEADERS = [
        { id: 1, label: "Gene Name", colspan: 1 },
        { id: 2, label: "Gene ID", colspan: 1 },
        { id: 3, label: "Symbol", colspan: 1 },
        { id: 4, label: "Gene Type", colspan: 1 },
    ];

    const COLUMNS = HEADERS.map((header, index) => ({ key: ["geneName", "geneId", "symbol", "geneType"][index] }));

// this will change
//    const mockData = [
//        {
//            geneName: "tumor protein p53",
//            geneId: "7157",
//            symbol: "TP53",
//            geneType: "protein-coding",
//        },
//        {
//            geneName: "tumor protein p53",
//            geneId: "7157",
//            symbol: "TP53",
//            geneType: "protein-coding",
//       }
//    ];

    const params = new URLSearchParams(window.location.search);
    const taxon = params.get('taxon');

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
    const [searchQuery, setSearchQuery] = useState(getLastSearch());
    const [results, setResults] = useState([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);


    // Fetch results when search query changes
    useEffect(() => {
        const fetchResults = async () => {
            if (!searchQuery.trim()) {
                setResults([]);
                setError(null);
                return;
            }

            setLoading(true);
            setError(null);

            try {
                console.log("GeneSearchPage: Fetching results for query =", searchQuery);
                const response = await fetch(
                    apiUrl(API_BASES.search, `/api/Search/searchGene?query=${encodeURIComponent(searchQuery)}&taxon=${encodeURIComponent(taxon || "")}`)
                );

                if (!response.ok) {
                    const errorData = await response.text();
                    if (response.status === 404) {
                        setResults([]);
                        setError(errorData || "No results found for the query.");
                    } else {
                        throw new Error(errorData || `HTTP error! status: ${response.status}`);
                    }
                } else {
                    const data = await response.json();
                    console.log("GeneSearchPage: API response =", data);
                    //saves once good results are returned


                    // Transform API data to match ResultsTable expected format
                    const formattedResults = (data.data || []).map(item => ({
                        geneName: item.geneName || "N/A",
                        geneId: item.geneId || "N/A",
                        symbol: item.symbol || "N/A",
                        geneType: item.geneType || "N/A",
                        // Store the full item for expanded view
                        _fullData: item.fullGene,

                        _saveAction: async (row = {}) => {
                            console.log("=== SAVE ACTION TRIGGERED ===");
                            try {
                                const result = await saveSearch(
                                    sessionStorage.getItem('idToken'),
                                    "gene",
                                    searchQuery,
                                    row.taxId || -1,
                                    -1
                                    //row.geneId || -1, for now untill table gets fixed
                                    
                                );
                                console.log(result)
                                console.log("Search saved successfully for:", searchQuery);
                                
                            } catch (err) {
                                console.error("Error saving search:", err);
                            }
                        },

                        _downloadAction: async (row = {}) => {
                            console.log("=== Download ACTION TRIGGERED ===: " + row.geneId);
                            try {
                                await getGeneFasta(row.geneId)
                                //call download api method here 
                                console.log("Download successful");
                            } catch (err) {
                                console.error("Error saving search:", err);
                            }
                        }

                    }));

                    setResults(formattedResults);
                }
            } catch (err) {
                setError(`Failed to fetch results: ${err.message}`);
                setResults([]);
            } finally {
                setLoading(false);
            }
        };

        fetchResults();
    }, [searchQuery]);

    return(
        <div className={styles.body}>
            <main className={styles.searchSection}>
                <div className={styles.searchContainer}>
                    <header className={styles.pageHeader}>
                        <p className={styles.eyebrow}>NCBI Gene explorer</p>
                        <h1>{taxon ? `Genes in ${taxon}` : "Search genes"}</h1>
                        <p>{taxon ? "Search by gene symbol, full gene name, or NCBI Gene ID." : "Choose an organism first. Common names, scientific names, clades, taxonomy IDs, and RefSeq accessions are supported."}</p>
                    </header>
                    <nav className={styles.searchTabs} aria-label="Data search type">
                        <Link to="/Search">Genomes</Link>
                        <Link className={styles.activeTab} to="/GeneSearch">Genes</Link>
                    </nav>

                    {!taxon && (
                        <SearchBar
                            onSearch={(organism) => navigate(`/GeneSearch?taxon=${encodeURIComponent(organism)}`)}
                            placeholder="Common name, scientific name, clade, tax ID, or RefSeq accession"
                            name="geneOrganismSearchBar"
                            filter="GENOME"
                            initialValue=""
                        />
                    )}
                    <br />
                    {taxon && (
                        <div className={styles.selectedTaxon}><span>Organism</span><strong>{taxon}</strong><Link to="/GeneSearch">Change</Link></div>
                    )}
                    {taxon && (
                        <SearchBar
                            onSearch={setSearchQuery}
                            placeholder="Gene symbol, full name, or NCBI Gene ID"
                            name="geneSearchBar"
                            enableSpeciesSuggestion={false} 
                            initialValue={searchQuery}/>
                    )}

                    <br />
                    <div className={styles.sourceNote}><strong>Source:</strong> NCBI Taxonomy, Gene, and Datasets. Sequence downloads are provided directly by NCBI.</div>

                    {error && <div className={styles.error} role="alert"><strong>Search unavailable.</strong> {error}</div>}
                    {loading && <div className={styles.loading} role="status"><span className={styles.spinner} />Searching NCBI Gene...</div>}

                        {!loading && results.length > 0 && (
                            <section aria-labelledby="gene-results">
                            <div className={styles.resultSummary}><h2 id="gene-results">Gene results</h2><span>{results.length} {results.length === 1 ? "record" : "records"}</span></div>
                            <ResultsTable
                                headers={HEADERS}
                                rows={results}
                                includeSubHeader={false}
                                columns={COLUMNS}
                                expandedContent={(rowData) => <ExpandedGene data={rowData._fullData} />}
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
}

export default GeneSearchPage;
