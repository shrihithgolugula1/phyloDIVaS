# Large orthogroup selections

Selections below 10,000 groups keep the existing downstream path. At 10,000 or
more, phyloDIVaS partitions whole orthogroup rows into inputs of at most 1,000
groups. Each input repeats the N0 header; no genes are truncated or split across
batches. Duplicate or malformed group identifiers are rejected before uploads.

The app uploads all bounded inputs into one list collection and submits one
Galaxy-owned workflow invocation. Galaxy maps gene-map extraction and the split
step across that list, producing nested orthogroup collections. Its built-in
Flatten collection operation joins these into one list of existing datasets;
it does not dynamically create another dataset for every group.

Downstream sequence extraction, MAFFT, codon back-translation, IQ-TREE and all
three HyPhy methods map over the combined list. Galaxy schedules independent
jobs concurrently within the user's server quotas. The flatten step is a fan-in
barrier: alignment waits for the batch splits to populate successfully.

Batch labels use `batch0001:N0.HOG...` rather than an underscore separator. This
preserves an unambiguous canonical HOG identifier for existing result discovery.
The viewer reads one combined collection per tool/output type, removes the batch
namespace, and retains one result per original orthogroup. BUSTED correction is
therefore calculated over the full readable family, not separately per batch.

Once the workflow invocation is accepted, Galaxy owns all dependencies: browser
polling and a retained API key are not needed to initiate later stages. Preparing
and uploading inputs still requires the initial connection to remain available.

For example, 11,822 groups produce 12 bounded inputs (11 × 1,000 plus 822), then
one combined orthogroup list and one complete result family. Existing failed
invocations are not rewritten automatically; retry downstream selection using
the updated application and the successful existing OrthoFinder/protein/CDS data.

## Resuming a stopped downstream run

Load Existing History inspects both dataset/collection activity and the latest
workflow invocation. An empty collection from a failed dynamic split is a
stopped workflow, even when Galaxy reports zero failed child datasets.

- Completed histories open the shared results dashboard.
- Active jobs or a workflow still being scheduled keep submission locked.
- A failed/cancelled downstream invocation, or completed OrthoFinder results
  without a downstream submission, reopens gene selection and loads the strict
  root N0 single-copy rows from the existing table. If no saved filtered table
  exists, the raw N0 table is filtered in memory.

Loading a history does not resubmit a selection. Choose genes (or all groups)
and confirm analysis to start a new downstream invocation in the same history.
The existing OrthoFinder results are reused; the historical failed outputs remain
in Galaxy for inspection. A transient status timeout does not prove failure.
