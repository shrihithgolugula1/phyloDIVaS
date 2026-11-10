# phyloDIVaS Galaxy workflow

`Galaxy-Workflow-phyloDIVaS-Pipeline-MAFFT-raw-N0.ga` accepts three inputs:

1. A raw OrthoFinder `Phylogenetic_Hierarchical_Orthogroups/N0.tsv` file.
2. The merged, isoform-filtered protein FASTA used by OrthoFinder.
3. The merged, isoform-filtered CDS FASTA whose primary FASTA identifiers are the matching protein accessions.

The workflow detects the metadata columns from the N0 header and retains a row only when:

- the row belongs to node N0;
- every species column contains a value; and
- every species column contains exactly one gene ID (no comma-separated copies).

It converts every retained HOG into a one-column gene-ID collection element. The same element filters both the protein and CDS inputs, so MAFFT and EMBOSS `tranalign` receive corresponding records. IQ-TREE, MEME, BUSTED and aBSREL are then mapped over the codon-alignment collection.

All intermediate and final collections have named workflow outputs. Running an unmodified N0 file can create thousands of MAFFT, IQ-TREE and HyPhy jobs. For routine interactive use, invoke the downstream portion only for the orthogroups selected by the user.

## Application integration

The workflow is embedded in `CapstoneServices` and is imported into, or reused from, the active user's Galaxy account when they submit selected orthogroups. The application preserves the existing OrthoFinder stage. After selection, it constructs a raw-N0-compatible table containing only the requested groups, uploads that table and the merged filtered protein/CDS FASTA files, and invokes this workflow with those three datasets. The workflow invocation ID is returned to the client so progress is read from Galaxy rather than inferred from local UI state.

Before alignment, the application-generated workflow maps Galaxy's AWK tool over the protein and CDS collections. It rewrites only FASTA identifiers to the same accession-based, HyPhy-safe format; sequence characters are unchanged. MAFFT, `tranalign`, IQ-TREE, MEME, BUSTED and aBSREL are submitted as one Galaxy workflow invocation. Once Galaxy accepts that invocation, it schedules all dependent collection jobs itself, so closing phyloDIVaS or stopping localhost does not prevent HyPhy from starting.

The Galaxy API key is supplied only with the submission request and is not persisted by the application. Galaxy's invocation ID and history datasets are sufficient to continue the computation on Galaxy's infrastructure; users need to re-enter the key only when they want phyloDIVaS to read or modify their private history later.

Regenerate the workflow from the original export with:

```bash
node scripts/build-corrected-galaxy-workflow.mjs SOURCE.ga GalaxyWorkflows/Galaxy-Workflow-phyloDIVaS-Pipeline-MAFFT-raw-N0.ga
```
