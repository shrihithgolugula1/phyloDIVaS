#!/usr/bin/env node

import { readFile, writeFile, mkdir } from "node:fs/promises";
import { dirname } from "node:path";
import { randomUUID } from "node:crypto";

const [sourcePath, outputPath] = process.argv.slice(2);
if (!sourcePath || !outputPath) {
  throw new Error("Usage: build-corrected-galaxy-workflow.mjs SOURCE.ga OUTPUT.ga");
}

const source = JSON.parse(await readFile(sourcePath, "utf8"));
const connected = { __class__: "ConnectedValue" };

function dataInput(id, label, left, top) {
  return {
    annotation: "",
    content_id: null,
    errors: null,
    id,
    input_connections: {},
    inputs: [{ description: "", name: label }],
    label,
    name: "Input dataset",
    outputs: [],
    position: { left, top },
    tool_id: null,
    tool_state: JSON.stringify({ optional: false, tag: null }),
    tool_version: null,
    type: "data_input",
    uuid: randomUUID(),
    when: null,
    workflow_outputs: []
  };
}

function toolStep({ id, name, toolId, version, state, connections, outputs, left, top, workflowOutputs = [], postJobActions = {} }) {
  return {
    annotation: "",
    content_id: toolId,
    errors: null,
    id,
    input_connections: connections,
    inputs: [],
    label: null,
    name,
    outputs,
    position: { left, top },
    post_job_actions: postJobActions,
    tool_id: toolId,
    tool_state: JSON.stringify({ ...state, __page__: 0, __rerun_remap_job_id__: null }),
    tool_uuid: null,
    tool_version: version,
    type: "tool",
    uuid: randomUUID(),
    when: null,
    workflow_outputs: workflowOutputs
  };
}

function cloneTool(sourceId, id, connections, left, top, workflowOutputs = []) {
  const step = structuredClone(source.steps[String(sourceId)]);
  step.id = id;
  step.uuid = randomUUID();
  step.input_connections = connections;
  step.position = { left, top };
  step.workflow_outputs = workflowOutputs;
  return step;
}

const awkProgram = `BEGIN { FS = OFS = "\\t" }
NR == 1 {
  species_start = 1
  species_end = NF
  parent_col = 0
  for (i = 1; i <= NF; i++) {
    header = tolower($i)
    gsub(/^[[:space:]]+|[[:space:]]+$/, "", header)
    if (header == "gene tree parent clade") parent_col = i
    if (header == "hog" || header == "orthogroup" || header == "og" || header == "gene tree parent clade") species_start = i + 1
  }
  next
}
{
  hog = $1
  parent = parent_col ? tolower($(parent_col)) : ""
  if (parent_col && parent != "n0") next
  if (!parent_col && tolower(hog) !~ /^n0([.]|$)/) next
  valid = 1
  for (i = species_start; i <= species_end; i++) {
    gene = $i
    gsub(/^[[:space:]]+|[[:space:]]+$/, "", gene)
    if (gene == "" || gene == "-" || gene == "0" || index(gene, ",") > 0) { valid = 0; break }
  }
  if (valid) {
    for (i = species_start; i <= species_end; i++) {
      gene = $i
      gsub(/^[[:space:]]+|[[:space:]]+$/, "", gene)
      print hog, gene
    }
  }
}`;

const filterState = {
  adv_opts: { adv_opts_selector: "basic", __current_case__: 0 },
  id_opts: { id_opts_selector: "tabular", __current_case__: 0, input_tabular: connected, columns: ["1"] },
  input_file: connected,
  output_choice_cond: { output_choice: "pos", __current_case__: 1 }
};

const steps = {};
steps["0"] = dataInput(0, "Raw OrthoFinder N0.tsv", 0, 0);
steps["1"] = dataInput(1, "Merged filtered proteome", 0, 180);
steps["2"] = dataInput(2, "Merged filtered CDS with protein accession IDs", 0, 360);

steps["3"] = toolStep({
  id: 3,
  name: "Extract strict 1:1 N0 orthogroup gene IDs",
  toolId: "toolshed.g2.bx.psu.edu/repos/bgruening/text_processing/tp_awk_tool/9.11+galaxy0",
  version: "9.11+galaxy0",
  state: { infile: connected, code: awkProgram, variables: [] },
  connections: { infile: { id: 0, output_name: "output" } },
  outputs: [{ name: "outfile", type: "data" }],
  left: 280,
  top: 0,
  workflowOutputs: [{ label: "strict_n0_gene_map", output_name: "outfile" }]
});

steps["4"] = toolStep({
  id: 4,
  name: "Split strict orthogroups into a collection",
  toolId: "toolshed.g2.bx.psu.edu/repos/bgruening/split_file_on_column/tp_split_on_column/0.6",
  version: "0.6",
  state: { infile: connected, column: "1", include_header: false },
  connections: { infile: { id: 3, output_name: "outfile" } },
  outputs: [{ name: "split_output", type: "list" }],
  left: 560,
  top: 0,
  workflowOutputs: [{ label: "strict_orthogroup_gene_maps", output_name: "split_output" }]
});

steps["5"] = toolStep({
  id: 5,
  name: "Select gene ID column",
  toolId: "Cut1",
  version: "1.0.2",
  state: { columnList: "c2", delimiter: "T", input: connected },
  connections: { input: { id: 4, output_name: "split_output" } },
  outputs: [{ name: "out_file1", type: "tabular" }],
  left: 820,
  top: 0,
  workflowOutputs: [{ label: "orthogroup_id_lists", output_name: "out_file1" }]
});

steps["6"] = toolStep({
  id: 6,
  name: "Filter proteins by orthogroup ID",
  toolId: "toolshed.g2.bx.psu.edu/repos/peterjc/seq_filter_by_id/seq_filter_by_id/0.2.9",
  version: "0.2.9",
  state: filterState,
  connections: {
    "id_opts|input_tabular": { id: 5, output_name: "out_file1" },
    input_file: { id: 1, output_name: "output" }
  },
  outputs: [{ name: "output_pos", type: "input" }],
  left: 1080,
  top: 100,
  workflowOutputs: [{ label: "orthogroup_protein_fastas", output_name: "output_pos" }],
  postJobActions: {
    HideDatasetActionoutput_neg: { action_arguments: {}, action_type: "HideDatasetAction", output_name: "output_neg" }
  }
});

steps["7"] = toolStep({
  id: 7,
  name: "Filter CDS by orthogroup ID",
  toolId: "toolshed.g2.bx.psu.edu/repos/peterjc/seq_filter_by_id/seq_filter_by_id/0.2.9",
  version: "0.2.9",
  state: filterState,
  connections: {
    "id_opts|input_tabular": { id: 5, output_name: "out_file1" },
    input_file: { id: 2, output_name: "output" }
  },
  outputs: [{ name: "output_pos", type: "input" }],
  left: 1080,
  top: 340,
  workflowOutputs: [{ label: "orthogroup_cds_fastas", output_name: "output_pos" }],
  postJobActions: {
    HideDatasetActionoutput_neg: { action_arguments: {}, action_type: "HideDatasetAction", output_name: "output_neg" }
  }
});

steps["8"] = cloneTool(4, 8, { "input|batches_0|inputs": { id: 6, output_name: "output_pos" } }, 1340, 100, [
  { label: "mafft_protein_alignments", output_name: "outputAlignment" }
]);

steps["9"] = toolStep({
  id: 9,
  name: "Thread CDS onto protein alignment (tranalign)",
  toolId: "toolshed.g2.bx.psu.edu/repos/devteam/emboss_5/EMBOSS: tranalign100/5.0.0",
  version: "5.0.0",
  state: { input1: connected, input2: connected, table: "0", out_format1: "fasta" },
  connections: {
    input1: { id: 7, output_name: "output_pos" },
    input2: { id: 8, output_name: "outputAlignment" }
  },
  outputs: [{ name: "out_file1", type: "fasta" }],
  left: 1600,
  top: 220,
  workflowOutputs: [{ label: "codon_alignments", output_name: "out_file1" }]
});

steps["10"] = cloneTool(6, 10, { "general_options|s": { id: 9, output_name: "out_file1" } }, 1860, 220, [
  { label: "gene_trees", output_name: "treefile" },
  { label: "iqtree_reports", output_name: "iqtree" }
]);
steps["10"].post_job_actions = {};

steps["11"] = toolStep({
  id: 11,
  name: "HyPhy-MEME",
  toolId: "toolshed.g2.bx.psu.edu/repos/iuc/hyphy_meme/hyphy_meme/2.5.96+galaxy0",
  version: "2.5.96+galaxy0",
  state: {
    input_file: connected,
    input_nhx: connected,
    gencodeid: "Universal",
    branch_cond: { branch_sel: "All", __current_case__: 1 },
    p_value: "0.1",
    advanced_options: {
      resample: "0",
      rates: "2",
      multiple_hits_conditional: { multiple_hits: "None", __current_case__: 2 },
      impute_states: false,
      precision: "standard",
      kill_zero_lengths: "Yes",
      restrict_sites_conditional: { restrict_sites_flag: "false", __current_case__: 1 },
      full_model: true
    }
  },
  connections: {
    input_file: { id: 9, output_name: "out_file1" },
    input_nhx: { id: 10, output_name: "treefile" }
  },
  outputs: [
    { name: "meme_output", type: "hyphy_results.json" },
    { name: "meme_md_report", type: "markdown" }
  ],
  left: 2140,
  top: 40,
  workflowOutputs: [
    { label: "meme_json", output_name: "meme_output" },
    { label: "meme_report", output_name: "meme_md_report" }
  ]
});
steps["11"].tool_shed_repository = {
  changeset_revision: "67fe4f52c1e5",
  name: "hyphy_meme",
  owner: "iuc",
  tool_shed: "toolshed.g2.bx.psu.edu"
};

steps["12"] = cloneTool(8, 12, {
  input_file: { id: 9, output_name: "out_file1" },
  input_nhx: { id: 10, output_name: "treefile" }
}, 2140, 240, [
  { label: "busted_json", output_name: "busted_output" },
  { label: "busted_report", output_name: "busted_md_report" }
]);

steps["13"] = cloneTool(9, 13, {
  input_file: { id: 9, output_name: "out_file1" },
  input_nhx: { id: 10, output_name: "treefile" }
}, 2140, 440, [
  { label: "absrel_json", output_name: "absrel_output" },
  { label: "absrel_report", output_name: "absrel_md_report" }
]);

const workflow = {
  ...source,
  name: "phyloDIVaS downstream selection v2 (MAFFT)",
  annotation: "Filters strict 1:1 orthogroups from raw OrthoFinder N0.tsv, creates per-HOG protein/CDS collections, aligns proteins, creates codon alignments and runs IQ-TREE, MEME, BUSTED and aBSREL.",
  comments: [],
  steps,
  uuid: randomUUID()
};

await mkdir(dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(workflow, null, 2)}\n`, "utf8");
