import { useEffect, useState } from "react";
import { getAssemblyPublicationDetails } from "../../../../api/dndsApi";
import styles from "./ExpandedGenome.module.css";

const display = (value, suffix = "") => value !== null && value !== undefined && value !== "" ? `${value}${suffix}` : "Not reported";

const FieldGrid = ({ fields }) => (
    <dl className={styles.fieldGrid}>
        {fields.map(({ label, value }) => <div key={label}><dt>{label}</dt><dd className={value === "Not reported" ? styles.missing : ""}>{value}</dd></div>)}
    </dl>
);

const ExpandedGenome = ({ data }) => {
    const [tab, setTab] = useState("overview");
    const [publication, setPublication] = useState(null);
    const [publicationLoading, setPublicationLoading] = useState(false);

    useEffect(() => {
        if (!data?.accession) return;
        let cancelled = false;
        setPublicationLoading(true);
        getAssemblyPublicationDetails(data.accession)
            .then(value => { if (!cancelled) setPublication(value); })
            .catch(() => { if (!cancelled) setPublication(null); })
            .finally(() => { if (!cancelled) setPublicationLoading(false); });
        return () => { cancelled = true; };
    }, [data?.accession]);

    if (!data) return <div className={styles.empty}>No assembly metadata is available.</div>;

    const tabs = [
        { id: "overview", label: "Overview" },
        { id: "quality", label: "Quality" },
        { id: "annotation", label: "Annotation" },
        { id: "publication", label: "Submission" }
    ];
    const overview = [
        { label: "RefSeq accession", value: display(data.accession) },
        { label: "Scientific name", value: display(data.scientificName) },
        { label: "Common name", value: display(data.commonName) },
        { label: "Assembly name", value: display(data.assemblyName) },
        { label: "Assembly level", value: display(data.assemblyLevel) },
        { label: "Assembly status", value: display(data.assemblyStatus) },
        { label: "Assembly type", value: display(data.assemblyType) },
        { label: "BioProject", value: display(data.bioprojectAccession) }
    ];
    const quality = [
        { label: "Genome size", value: display(data.genomeSize, data.genomeSize ? " bp" : "") },
        { label: "Ungapped length", value: display(data.totalUngappedLength, data.totalUngappedLength ? " bp" : "") },
        { label: "Chromosomes", value: display(data.numChromosomes) },
        { label: "Scaffolds", value: display(data.numScaffolds) },
        { label: "Scaffold N50", value: display(data.scaffoldN50, data.scaffoldN50 ? " bp" : "") },
        { label: "Contigs", value: display(data.numContigs) },
        { label: "Contig N50", value: display(data.contigN50, data.contigN50 ? " bp" : "") },
        { label: "GC content", value: display(data.gcPercent, data.gcPercent ? "%" : "") },
        { label: "BUSCO completeness", value: data.buscoComplete != null ? `${(Number(data.buscoComplete) * 100).toFixed(1)}%` : "Not reported" }
    ];
    const annotation = [
        { label: "Provider", value: display(data.annotationProvider) },
        { label: "Annotation name", value: display(data.annotationName) },
        { label: "Annotation date", value: display(data.annotationDate) },
        { label: "Genes", value: display(data.genes) },
        { label: "Protein-coding genes", value: display(data.proteinCoding) },
        { label: "Release date", value: display(data.releaseDate) }
    ];
    const publicationFields = [
        { label: "Genome submitter", value: display(publication?.submitter || data.submitter) },
        { label: "Institution / consortium", value: display(publication?.institution) },
        { label: "Submission date", value: display(publication?.submissionDate || data.submissionDate) },
        { label: "Genome release date", value: display(publication?.releaseDate || data.releaseDate) },
        { label: "BioProject", value: display(publication?.bioProjectAccession || data.bioprojectAccession) },
        { label: "BioProject title", value: display(publication?.bioProjectTitle || data.bioprojectTitle) }
    ];

    return <section className={styles.expandedSection} aria-label={`${data.assemblyName || "Assembly"} details`}>
        <div className={styles.tabs} role="tablist">
            {tabs.map(item => <button key={item.id} role="tab" aria-selected={tab === item.id} className={tab === item.id ? styles.active : ""} onClick={() => setTab(item.id)}>{item.label}</button>)}
        </div>
        <div className={styles.tabPanel} role="tabpanel">
            {tab === "overview" && <><FieldGrid fields={overview} />{data.assemblyMethods && <div className={styles.note}><strong>Assembly methods</strong><p>{data.assemblyMethods}</p></div>}</>}
            {tab === "quality" && <FieldGrid fields={quality} />}
            {tab === "annotation" && <FieldGrid fields={annotation} />}
            {tab === "publication" && <>
                {publicationLoading && <p className={styles.loading}>Loading NCBI BioProject metadata...</p>}
                <FieldGrid fields={publicationFields} />
                {(publication?.contributors?.length > 0) && <div className={styles.note}><strong>Contacts and contributors</strong><p>{publication.contributors.join(", ")}</p></div>}
                {data.description && <div className={styles.note}><strong>Assembly description</strong><p>{data.description}</p></div>}
            </>}
        </div>
        <p className={styles.provenance}>Metadata supplied by NCBI Datasets. “Not reported” means the field was absent from the current NCBI response.</p>
    </section>;
};

export default ExpandedGenome;
