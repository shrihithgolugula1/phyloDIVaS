import { useCallback, useEffect, useMemo, useState } from 'react';
import PropTypes from 'prop-types';
import Form from 'react-bootstrap/Form';
import Button from 'react-bootstrap/Button';
import Spinner from 'react-bootstrap/Spinner';
import Alert from 'react-bootstrap/Alert';
import Table from 'react-bootstrap/Table';
import { getOrthogroupAnnotations } from '../../api/dndsApi';
import 'bootstrap/dist/css/bootstrap.min.css';
import styles from './OrthologousGroupSelector.module.css';

const PAGE_SIZE_OPTIONS = [25, 50, 100];
const ANNOTATION_BATCH_SIZE = 1000;
const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

const getGroupAccessions = (group) => {
    const explicit = Array.isArray(group.geneIds) ? group.geneIds : [];
    const values = explicit.length > 0 ? explicit : Object.values(group.speciesGenes || {});
    return values.flatMap((value) => String(value || '').split(',')).map((value) => value.trim()).filter(Boolean);
};

const findAnnotation = (annotations, accession) => annotations[accession] || annotations[accession?.split('.')[0]];

const OrthologousGroupSelector = ({
    groups, selectedGroups, onGroupSelectionChange, onSelectAll, onSelectNone,
    onAnalyzeSelected, onAnalyzeAll, isLoading, errorMessage, canAnalyze,
    isAnalyzing, ncbiApiKey, onGroupsAnnotated, genomes, browseOnly,
}) => {
    const [query, setQuery] = useState('');
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(50);
    const [sortBy, setSortBy] = useState('relevance');
    const [sortDirection, setSortDirection] = useState('asc');
    const [annotations, setAnnotations] = useState({});
    const [annotationProgress, setAnnotationProgress] = useState({ loaded: 0, total: 0, running: false });
    const normalizedQuery = query.trim().toLowerCase();
    const selectedSet = useMemo(() => new Set(selectedGroups), [selectedGroups]);
    const representativeEntries = useMemo(() => groups.map((group) => ({
        groupId: group.groupId,
        accession: getGroupAccessions(group)[0],
    })).filter((entry) => entry.accession), [groups]);
    const representativeKey = useMemo(() => representativeEntries
        .map((entry) => `${entry.groupId}:${entry.accession}`).join('|'), [representativeEntries]);

    useEffect(() => {
        let canceled = false;
        if (browseOnly) {
            setAnnotationProgress({ loaded: 0, total: 0, running: false });
            return () => { canceled = true; };
        }
        const representatives = [...new Set(representativeEntries.map((entry) => entry.accession))];
        setAnnotations({});
        setAnnotationProgress({ loaded: 0, total: representatives.length, running: representatives.length > 0 });
        if (representatives.length === 0) return () => { canceled = true; };

        const load = async () => {
            for (let start = 0; start < representatives.length && !canceled; start += ANNOTATION_BATCH_SIZE) {
                const batch = representatives.slice(start, start + ANNOTATION_BATCH_SIZE);
                try {
                    const next = await getOrthogroupAnnotations(batch, ncbiApiKey || undefined);
                    if (canceled) return;
                    setAnnotations((current) => ({ ...current, ...next }));
                    const groupUpdates = {};
                    representativeEntries.forEach((entry) => {
                        const annotation = findAnnotation(next, entry.accession);
                        if (annotation) groupUpdates[entry.groupId] = annotation;
                    });
                    if (Object.keys(groupUpdates).length > 0) onGroupsAnnotated(groupUpdates);
                } catch (error) {
                    console.warn('Unable to annotate an orthogroup batch from NCBI', error);
                }
                if (!canceled) setAnnotationProgress({
                    loaded: Math.min(start + batch.length, representatives.length),
                    total: representatives.length,
                    running: start + batch.length < representatives.length,
                });
            }
        };
        load();
        return () => { canceled = true; };
    // representativeKey intentionally prevents name-only parent updates from restarting the fetch loop.
    }, [representativeKey, ncbiApiKey, onGroupsAnnotated, browseOnly]);

    const indexedGroups = useMemo(() => groups.map((group) => {
        const ids = getGroupAccessions(group);
        const annotation = findAnnotation(annotations, ids[0]) || group.annotation;
        const familyName = annotation?.name || (group.groupName !== group.groupId ? group.groupName : '');
        return {
            ...group, ids, annotation, familyName,
            searchText: [group.groupId, group.groupName, familyName, annotation?.organism, ...ids]
                .filter(Boolean).join(' ').toLowerCase(),
        };
    }), [groups, annotations]);

    const filteredGroups = useMemo(() => {
        const ranked = indexedGroups.map((group) => {
            if (!normalizedQuery) return { group, relevance: 0 };
            const family = group.familyName.toLowerCase();
            const groupId = group.groupId.toLowerCase();
            const ids = group.ids.map((id) => id.toLowerCase());
            let relevance = Number.POSITIVE_INFINITY;
            if (family === normalizedQuery || groupId === normalizedQuery || ids.includes(normalizedQuery)) relevance = 0;
            else if (family.startsWith(normalizedQuery)) relevance = 1;
            else if (family.split(/\s+/).some((word) => word.startsWith(normalizedQuery))) relevance = 2;
            else if (groupId.startsWith(normalizedQuery) || ids.some((id) => id.startsWith(normalizedQuery))) relevance = 3;
            else if (family.includes(normalizedQuery)) relevance = 4;
            else if (group.searchText.includes(normalizedQuery)) relevance = 5;
            return { group, relevance };
        }).filter((entry) => Number.isFinite(entry.relevance));

        ranked.sort((left, right) => {
            let comparison = 0;
            if (sortBy === 'relevance') comparison = left.relevance - right.relevance;
            else if (sortBy === 'name') comparison = collator.compare(left.group.familyName || '\uffff', right.group.familyName || '\uffff');
            else if (sortBy === 'selected') comparison = Number(selectedSet.has(right.group.groupId)) - Number(selectedSet.has(left.group.groupId));
            else comparison = collator.compare(left.group.groupId, right.group.groupId);
            if (comparison === 0) comparison = collator.compare(left.group.familyName || left.group.groupId, right.group.familyName || right.group.groupId);
            return sortDirection === 'asc' ? comparison : -comparison;
        });
        return ranked.map((entry) => entry.group);
    }, [indexedGroups, normalizedQuery, selectedSet, sortBy, sortDirection]);
    const pageCount = Math.max(1, Math.ceil(filteredGroups.length / pageSize));
    const safePage = Math.min(page, pageCount);
    const visibleGroups = useMemo(() => {
        const start = (safePage - 1) * pageSize;
        return filteredGroups.slice(start, start + pageSize);
    }, [filteredGroups, safePage, pageSize]);

    const loadVisibleAnnotations = useCallback(async () => {
        const entries = visibleGroups
            .map((group) => ({ groupId: group.groupId, accession: group.ids[0] }))
            .filter((entry) => entry.accession && !findAnnotation(annotations, entry.accession));
        const accessions = [...new Set(entries.map((entry) => entry.accession))];
        if (accessions.length === 0) return;

        setAnnotationProgress({ loaded: 0, total: accessions.length, running: true });
        try {
            const next = await getOrthogroupAnnotations(accessions, ncbiApiKey || undefined);
            setAnnotations((current) => ({ ...current, ...next }));
            const groupUpdates = {};
            entries.forEach((entry) => {
                const annotation = findAnnotation(next, entry.accession);
                if (annotation) groupUpdates[entry.groupId] = annotation;
            });
            if (Object.keys(groupUpdates).length > 0) onGroupsAnnotated(groupUpdates);
            setAnnotationProgress({ loaded: accessions.length, total: accessions.length, running: false });
        } catch (error) {
            console.warn('Unable to annotate the visible orthogroups from NCBI', error);
            setAnnotationProgress({ loaded: 0, total: accessions.length, running: false });
        }
    }, [visibleGroups, annotations, ncbiApiKey, onGroupsAnnotated]);

    useEffect(() => setPage(1), [normalizedQuery, pageSize, sortBy, sortDirection]);
    const selectedGenomeNames = useMemo(() => genomes
        .map((genome) => typeof genome === 'string' ? genome : genome?.species)
        .filter(Boolean), [genomes]);
    const handleGroupToggle = useCallback((groupId) => {
        if (selectedSet.has(groupId)) onGroupSelectionChange(selectedGroups.filter((id) => id !== groupId));
        else onGroupSelectionChange([...selectedGroups, groupId]);
    }, [onGroupSelectionChange, selectedGroups, selectedSet]);

    if (isLoading) return <div className={styles.container}><div className={styles.loadingContainer}><Spinner animation="border" /><p>Loading orthologous groups...</p></div></div>;
    if (!groups || groups.length === 0) return <div className={styles.container}>{errorMessage
        ? <Alert variant="danger"><Alert.Heading>Unable to load groups</Alert.Heading><p>{errorMessage}</p></Alert>
        : <Alert variant="info"><p>No strict 1:1 orthologous groups are available yet.</p></Alert>}</div>;

    return (
        <div className={styles.container}>
            {errorMessage && <Alert variant="danger" dismissible={false}><Alert.Heading>Submission not accepted</Alert.Heading><p className="mb-0">{errorMessage}</p></Alert>}
            <div className={styles.header}>
                <div><h4>Orthologous Gene Families</h4><span className={styles.resultCount}>{filteredGroups.length.toLocaleString()} of {groups.length.toLocaleString()} groups</span></div>
                {!browseOnly && <div className={styles.bulkActions}>
                    <Button variant="outline-primary" size="sm" onClick={() => onSelectAll(filteredGroups.map((group) => group.groupId))} disabled={isAnalyzing || filteredGroups.length === 0}>Select filtered</Button>
                    <Button variant="outline-secondary" size="sm" onClick={onSelectNone} disabled={isAnalyzing || selectedGroups.length === 0}>Clear selection</Button>
                </div>}
            </div>

            <div className={styles.searchRow}>
                <Form.Group className={styles.searchField} controlId="orthogroup-search">
                    <Form.Label>Search gene families</Form.Label>
                    <Form.Control type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Family name, orthogroup ID, or RefSeq accession" />
                </Form.Group>
                <Form.Group className={styles.sortField} controlId="orthogroup-sort">
                    <Form.Label>Sort by</Form.Label>
                    <Form.Select value={sortBy} onChange={(event) => setSortBy(event.target.value)}>
                        <option value="relevance">Search relevance</option>
                        <option value="name">Family name</option>
                        <option value="id">Orthogroup ID</option>
                        {!browseOnly && <option value="selected">Selected first</option>}
                    </Form.Select>
                </Form.Group>
                <Form.Group className={styles.pageSizeField} controlId="orthogroup-page-size">
                    <Form.Label>Rows</Form.Label>
                    <Form.Select value={pageSize} onChange={(event) => setPageSize(Number(event.target.value))}>
                        {PAGE_SIZE_OPTIONS.map((size) => <option key={size} value={size}>{size}</option>)}
                    </Form.Select>
                </Form.Group>
                <Button
                    variant="outline-secondary"
                    className={styles.sortDirection}
                    onClick={() => setSortDirection((value) => value === 'asc' ? 'desc' : 'asc')}
                    aria-label={sortDirection === 'asc' ? 'Sort descending' : 'Sort ascending'}
                    title={sortDirection === 'asc' ? 'Ascending; click for descending' : 'Descending; click for ascending'}
                >{sortDirection === 'asc' ? '\u25b2' : '\u25bc'}</Button>
            </div>

            <div className={styles.genomeContext}>
                <strong>Genomes in this analysis ({selectedGenomeNames.length})</strong>
                <ul>{selectedGenomeNames.map((name) => <li key={name}><i>{name}</i></li>)}</ul>
            </div>

            <div className={styles.summary} aria-live="polite">
                {!browseOnly && <span>Selected: {selectedGroups.length.toLocaleString()}</span>}
                <span>{annotationProgress.running
                    ? `Retrieving NCBI names: ${annotationProgress.loaded.toLocaleString()} / ${annotationProgress.total.toLocaleString()}`
                    : `NCBI names loaded: ${Object.keys(annotations).filter((key) => key.includes('.')).length.toLocaleString()}`}</span>
                {browseOnly && (
                    <Button variant="outline-secondary" size="sm" onClick={loadVisibleAnnotations} disabled={annotationProgress.running}>
                        {annotationProgress.running ? "Loading names..." : "Load names for this page"}
                    </Button>
                )}
            </div>

            <div className={styles.tableContainer}>
                <Table striped bordered hover size="sm" className={`${styles.groupTable} ${browseOnly ? styles.browseTable : ''}`}>
                    <thead><tr>{!browseOnly && <th className={styles.selectColumn}>Select</th>}<th>Orthogroup ID</th><th>Gene family name</th></tr></thead>
                    <tbody>{visibleGroups.map((group) => {
                        const selected = selectedSet.has(group.groupId);
                        return <tr key={group.groupId} className={selected ? styles.selectedRow : ''}>
                            {!browseOnly && <td><Form.Check type="checkbox" aria-label={`Select ${group.groupId}`} checked={selected} onChange={() => handleGroupToggle(group.groupId)} disabled={isAnalyzing} /></td>}
                            <td className={styles.groupId}>{group.groupId}</td>
                            <td className={styles.familyName}>{group.annotation
                                ? <a href={group.annotation.sourceUrl} target="_blank" rel="noopener noreferrer">{group.familyName}</a>
                                : group.familyName
                                    ? group.familyName
                                    : <span className={styles.pendingName}>{annotationProgress.running ? 'Retrieving from NCBI...' : browseOnly ? 'Load names for this page' : 'Name unavailable'}</span>}</td>
                        </tr>;
                    })}</tbody>
                </Table>
                {visibleGroups.length === 0 && <p className={styles.emptySearch}>No gene families match “{query}”.</p>}
            </div>

            <div className={styles.pagination}>
                <Button variant="outline-secondary" size="sm" onClick={() => setPage((value) => Math.max(1, value - 1))} disabled={safePage === 1}>Previous</Button>
                <span>Page {safePage.toLocaleString()} of {pageCount.toLocaleString()}</span>
                <Button variant="outline-secondary" size="sm" onClick={() => setPage((value) => Math.min(pageCount, value + 1))} disabled={safePage === pageCount}>Next</Button>
            </div>

            {!browseOnly && <div className={styles.actions}>
                <Button variant="success" size="lg" onClick={onAnalyzeSelected} disabled={!canAnalyze || selectedGroups.length === 0 || isAnalyzing}>
                    {isAnalyzing ? <><Spinner animation="border" size="sm" className="me-2" />Submitting...</> : `Analyze Selected Groups (${selectedGroups.length.toLocaleString()})`}
                </Button>
                <Button variant="primary" size="lg" onClick={onAnalyzeAll} disabled={!canAnalyze || groups.length === 0 || isAnalyzing}>Analyze All Groups ({groups.length.toLocaleString()})</Button>
            </div>}
            {!browseOnly && <p className={styles.reuseNote}>
                These groups come from the existing strict 1:1 OrthoFinder result. Submitting a new selection reuses the species set and skips genome retrieval and OrthoFinder.
            </p>}
        </div>
    );
};

OrthologousGroupSelector.propTypes = {
    groups: PropTypes.array, selectedGroups: PropTypes.arrayOf(PropTypes.string).isRequired,
    onGroupSelectionChange: PropTypes.func.isRequired, onSelectAll: PropTypes.func.isRequired,
    onSelectNone: PropTypes.func.isRequired, onAnalyzeSelected: PropTypes.func.isRequired,
    onAnalyzeAll: PropTypes.func.isRequired, isLoading: PropTypes.bool.isRequired,
    errorMessage: PropTypes.string, canAnalyze: PropTypes.bool.isRequired,
    isAnalyzing: PropTypes.bool.isRequired, ncbiApiKey: PropTypes.string,
    onGroupsAnnotated: PropTypes.func,
    genomes: PropTypes.array,
    browseOnly: PropTypes.bool,
};
OrthologousGroupSelector.defaultProps = { groups: [], errorMessage: '', ncbiApiKey: '', onGroupsAnnotated: () => {}, genomes: [], browseOnly: false };

export default OrthologousGroupSelector;
