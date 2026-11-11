import React from "react";
import styles from "./CitationsPage.module.css";

const citationGroups = [
    {
        title: "Genome and taxonomy resources",
        description: "Used to discover eligible genomes, resolve taxa, retrieve annotated RefSeq assemblies, and build the interactive reference-tree preview.",
        citations: [
            ["NCBI Datasets", "Genome, protein, CDS, assembly-metadata, and taxonomy retrieval", "O'Leary, N.A., Cox, E., Holmes, J.B., et al.", "Exploring and retrieving sequence and metadata for species across the tree of life with NCBI Datasets.", "Scientific Data 11, 732 (2024).", "10.1038/s41597-024-03571-y"],
            ["NCBI RefSeq", "Annotated reference genome, transcript, and protein records", "O'Leary, N.A., Wright, M.W., Brister, J.R., et al.", "Reference sequence (RefSeq) database at NCBI: current status, taxonomic expansion, and functional annotation.", "Nucleic Acids Research 44(D1), D733-D745 (2016).", "10.1093/nar/gkv1189"],
            ["NCBI Taxonomy", "Scientific-name, synonym, common-name, lineage, and TaxID resolution", "Schoch, C.L., Ciufo, S., Domrachev, M., et al.", "NCBI Taxonomy: a comprehensive update on curation, resources and tools.", "Database 2020, baaa062 (2020).", "10.1093/database/baaa062"],
            ["TimeTree 5", "Published divergence estimates and time-scaled reference trees", "Kumar, S., Suleski, M., Craig, J.E., et al.", "TimeTree 5: An Expanded Resource for Species Divergence Times.", "Molecular Biology and Evolution 39(8), msac174 (2022).", "10.1093/molbev/msac174"],
            ["Big Tree Viewer", "Interactive matched-tip comparison between the selected tree and its representative tree-of-life context", "Allard, J.B.", "Big Tree Viewer.", "Web application.", null, "https://bigtreeviewer.net/"],
            ["BUSCO", "Assembly-completeness values displayed with genome results when available", "Manni, M., Berkeley, M.R., Seppey, M., and Zdobnov, E.M.", "BUSCO Update: Novel and Streamlined Workflows along with Broader and Deeper Phylogenetic Coverage for Scoring of Eukaryotic, Prokaryotic, and Viral Genomes.", "Molecular Biology and Evolution 38(10), 4647-4654 (2021).", "10.1093/molbev/msab199"]
        ]
    },
    {
        title: "Galaxy workflow and orthology",
        description: "Used to store datasets and execute the reproducible comparative-genomics workflow.",
        citations: [
            ["Galaxy", "Workflow execution, histories, datasets, collections, and outputs", "The Galaxy Community", "The Galaxy platform for accessible, reproducible and collaborative biomedical analyses: 2022 update.", "Nucleic Acids Research 50(W1), W345-W351 (2022).", "10.1093/nar/gkac247"],
            ["OrthoFinder", "Orthogroup inference and the sequence-based analysis species tree", "Emms, D.M. and Kelly, S.", "OrthoFinder: phylogenetic orthology inference for comparative genomics.", "Genome Biology 20, 238 (2019).", "10.1186/s13059-019-1832-y"]
        ]
    },
    {
        title: "Sequence preparation and alignment",
        description: "Used inside Galaxy to translate coding sequences, align proteins, filter records, and project protein alignments back to codons.",
        citations: [
            ["MAFFT", "Default multiple protein-sequence alignment option", "Katoh, K. and Standley, D.M.", "MAFFT multiple sequence alignment software version 7: improvements in performance and usability.", "Molecular Biology and Evolution 30(4), 772-780 (2013).", "10.1093/molbev/mst010"],
            ["MUSCLE", "Optional multiple protein-sequence alignment method", "Edgar, R.C.", "MUSCLE: multiple sequence alignment with high accuracy and high throughput.", "Nucleic Acids Research 32(5), 1792-1797 (2004).", "10.1093/nar/gkh340"],
            ["Clustal W", "Optional progressive multiple-sequence alignment method", "Thompson, J.D., Higgins, D.G., and Gibson, T.J.", "CLUSTAL W: improving the sensitivity of progressive multiple sequence alignment through sequence weighting, position-specific gap penalties and weight matrix choice.", "Nucleic Acids Research 22(22), 4673-4680 (1994).", "10.1093/nar/22.22.4673"],
            ["EMBOSS Transeq", "Translation of nucleotide coding sequences to amino-acid sequences", "Rice, P., Longden, I., and Bleasby, A.", "EMBOSS: The European Molecular Biology Open Software Suite.", "Trends in Genetics 16(6), 276-277 (2000).", "10.1016/S0168-9525(00)02024-2"],
            ["Biopython-based Galaxy utilities", "FASTA filtering and codon-aware back-translation", "Cock, P.J.A., Antao, T., Chang, J.T., et al.", "Biopython: freely available Python tools for computational molecular biology and bioinformatics.", "Bioinformatics 25(11), 1422-1423 (2009).", "10.1093/bioinformatics/btp163"]
        ]
    },
    {
        title: "Selection analysis",
        description: "Used after codon-aware alignment to test orthologous groups for gene-wide episodic diversifying selection.",
        citations: [
            ["HyPhy 2.5", "Evolutionary model fitting and hypothesis-testing engine", "Kosakovsky Pond, S.L., Poon, A.F.Y., Velazquez, R., et al.", "HyPhy 2.5-A Customizable Platform for Evolutionary Hypothesis Testing Using Phylogenies.", "Molecular Biology and Evolution 37(1), 295-299 (2020).", "10.1093/molbev/msz197"],
            ["BUSTED", "Gene-wide test for episodic positive selection", "Murrell, B., Weaver, S., Smith, M.D., et al.", "Gene-wide identification of episodic selection.", "Molecular Biology and Evolution 32(5), 1365-1371 (2015).", "10.1093/molbev/msv035"],
            ["aBSREL", "Corrected branch-specific tests for episodic diversifying selection", "Smith, M.D., Wertheim, J.O., Weaver, S., et al.", "Less Is More: An Adaptive Branch-Site Random Effects Model for Efficient Detection of Episodic Diversifying Selection.", "Molecular Biology and Evolution 32(5), 1342-1353 (2015).", "10.1093/molbev/msv022"],
            ["MEME", "Codon-specific tests for episodic diversifying selection", "Murrell, B., Wertheim, J.O., Moola, S., et al.", "Detecting Individual Sites Subject to Episodic Diversifying Selection.", "PLoS Genetics 8(7), e1002764 (2012).", "10.1371/journal.pgen.1002764"],
            ["Preranked gene-set enrichment", "Exploratory weighted running-score plots; gene-set permutations and BH term correction are described in the viewer", "Subramanian, A., Tamayo, P., Mootha, V.K., et al.", "Gene set enrichment analysis: A knowledge-based approach for interpreting genome-wide expression profiles.", "PNAS 102(43), 15545-15550 (2005).", "10.1073/pnas.0506580102"],
            ["UniProt", "Reference-bat GO annotations mapped through unambiguous RefSeq accession stems", "The UniProt Consortium", "UniProt: the Universal Protein Knowledgebase in 2025.", "Nucleic Acids Research 53(D1), D609-D617 (2025).", "10.1093/nar/gkae1010"]
        ]
    }
].map((group) => ({
    ...group,
    citations: group.citations.map(([tool, use, authors, paper, journal, doi, url]) => ({ tool, use, authors, paper, journal, doi, url }))
}));

const CitationsPage = () => (
    <main className={styles.body}>
        <div className={styles.container}>
            <header className={styles.header}>
                <p className={styles.eyebrow}>Methods and provenance</p>
                <h1 className={styles.title}>Research Tools and Citations</h1>
                <p className={styles.subtitle}>
                    Cite the methods relevant to the stages and outputs used in your phyloDIVaS analysis.
                    Links below open the original publication for each resource or method.
                </p>
            </header>

            {citationGroups.map((group) => (
                <section className={styles.section} key={group.title}>
                    <div className={styles.sectionHeading}>
                        <h2 className={styles.sectionTitle}>{group.title}</h2>
                        <p>{group.description}</p>
                    </div>
                    <div className={styles.citationList}>
                        {group.citations.map((citation) => (
                            <article className={styles.citation} key={citation.tool}>
                                <div className={styles.citationHeader}>
                                    <h3>{citation.tool}</h3>
                                    <span>{citation.use}</span>
                                </div>
                                <p className={styles.authors}>{citation.authors}</p>
                                <p className={styles.paper}>{citation.paper}</p>
                                <p className={styles.journal}><em>{citation.journal}</em></p>
                                <a className={styles.paperLink} href={citation.url || `https://doi.org/${citation.doi}`} target="_blank" rel="noopener noreferrer">
                                    {citation.doi ? `Original paper: ${citation.doi}` : "Open research tool"}
                                </a>
                            </article>
                        ))}
                    </div>
                </section>
            ))}

            <aside className={styles.note}>
                Galaxy archive extraction and dataset-collection operations are workflow utilities rather than
                independent scientific methods. They are covered by the Galaxy platform citation above.
            </aside>
        </div>
    </main>
);

export default CitationsPage;
