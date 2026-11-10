import React, { forwardRef, useEffect, useImperativeHandle, useRef, useState } from "react";
import { suggestSpecies } from "../../api/dndsApi";
import styles from "./SpeciesSuggestion.module.css";

const SpeciesSuggestions = forwardRef(({ query, setQuery }, ref) => {
    const [suggestions, setSuggestions] = useState([]);
    const [visible, setVisible] = useState(false);
    const [loading, setLoading] = useState(false);
    const containerRef = useRef(null);

    useImperativeHandle(ref, () => ({
        showSuggestions() {
            if (query.trim().length >= 2) setVisible(true);
        }
    }));

    useEffect(() => {
        const value = query.trim();
        if (value.length < 2) {
            setSuggestions([]);
            setVisible(false);
            return undefined;
        }

        let canceled = false;
        const timer = window.setTimeout(async () => {
            setLoading(true);
            try {
                const response = await suggestSpecies({ query: value, limit: 25 });
                if (!canceled) {
                    setSuggestions(response);
                    setVisible(true);
                }
            } catch {
                if (!canceled) setSuggestions([]);
            } finally {
                if (!canceled) setLoading(false);
            }
        }, 250);

        return () => {
            canceled = true;
            window.clearTimeout(timer);
        };
    }, [query]);

    useEffect(() => {
        const close = (event) => {
            if (containerRef.current && !containerRef.current.contains(event.target)) setVisible(false);
        };
        document.addEventListener("mousedown", close);
        return () => document.removeEventListener("mousedown", close);
    }, []);

    if (!visible) return null;

    return (
        <div className={styles.suggestions} ref={containerRef}>
            {loading && <div className={styles.noResults}>Searching NCBI taxonomy...</div>}
            {!loading && suggestions.length === 0 && <div className={styles.noResults}>No annotated RefSeq genomes found</div>}
            {!loading && suggestions.length > 0 && (
                <ul className={styles.unorderedList}>
                    {suggestions.map((item) => (
                        <li
                            key={`${item.scientific}-${item.accession}`}
                            className={styles.suggestionItem}
                            onMouseDown={() => {
                                setQuery(item.scientific);
                                setVisible(false);
                            }}
                        >
                            <span className={styles.commonName}>{item.common || item.scientific}</span>{" "}
                            <span className={styles.sciName}>{item.scientific}</span>
                            <span className={styles.taxId}>
                                {item.accession} · {item.assemblyLevel || "Assembly"}
                                {item.scaffoldN50 ? ` · N50 ${Number(item.scaffoldN50).toLocaleString()} bp` : ""}
                                {item.buscoComplete != null ? ` · BUSCO ${(Number(item.buscoComplete) * 100).toFixed(1)}%` : ""}
                            </span>
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
});

SpeciesSuggestions.displayName = "SpeciesSuggestions";
export default SpeciesSuggestions;
