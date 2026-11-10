import { useState } from "react";
import styles from "./ExpandedGene.module.css";

const display = (value) => Array.isArray(value) ? (value.length ? value.join(", ") : "Not reported") : (value || "Not reported");
const FieldGrid = ({ fields }) => <dl className={styles.fieldGrid}>{fields.map(({ label, value }) => <div key={label}><dt>{label}</dt><dd className={value === "Not reported" ? styles.missing : ""}>{value}</dd></div>)}</dl>;

const ExpandedGene = ({ data }) => {
    const [tab, setTab] = useState("overview");
    if (!data) return <div className={styles.empty}>No detailed gene metadata is available.</div>;

    const overview = [
        { label: "Scientific name", value: display(data.taxName) },
        { label: "Common name", value: display(data.commonName) },
        { label: "Taxonomy ID", value: display(data.taxId) },
        { label: "Chromosome", value: display(data.chromosomes) },
        { label: "Orientation", value: display(data.orientation) },
        { label: "Synonyms", value: display(data.synonyms) }
    ];
    const records = [
        { label: "Transcript count", value: display(data.transcriptCount) },
        { label: "Protein count", value: display(data.proteinCount) },
        { label: "Ensembl Gene IDs", value: display(data.ensemblGeneIds) },
        { label: "Swiss-Prot accessions", value: display(data.swissProtAccessions) },
        { label: "OMIM IDs", value: display(data.omimIds) }
    ];

    return <section className={styles.expandedSection} aria-label="Gene details">
        <div className={styles.tabs} role="tablist">
            <button role="tab" aria-selected={tab === "overview"} className={tab === "overview" ? styles.active : ""} onClick={() => setTab("overview")}>Overview</button>
            <button role="tab" aria-selected={tab === "records"} className={tab === "records" ? styles.active : ""} onClick={() => setTab("records")}>Records</button>
        </div>
        <div className={styles.tabPanel} role="tabpanel">
            {tab === "overview" && <><p className={styles.summary}>{data.summary?.[0]?.description || data.description || "NCBI does not provide a summary for this gene."}</p><FieldGrid fields={overview} /></>}
            {tab === "records" && <FieldGrid fields={records} />}
        </div>
        <p className={styles.provenance}>Metadata and sequence records supplied by NCBI Gene and Datasets.</p>
    </section>;
};

export default ExpandedGene;
