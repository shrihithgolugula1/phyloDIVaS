using CapstoneServices.Galaxy;

await GalaxyCredentialTests.RunAsync();

var passed = 0;
using var partialCollection = System.Text.Json.JsonDocument.Parse("{\"populated_state\":\"ok\",\"elements_states\":{\"ok\":9952,\"paused\":26}}");
Check("populated collection with paused genes is not complete", GalaxyCompletionEvidence.RecordState(partialCollection.RootElement) == "paused");
void Check(string name, bool condition)
{
    if (!condition) throw new Exception($"FAILED: {name}");
    Console.WriteLine($"PASS: {name}");
    passed++;
}

Check("one completed tool cannot imply full completion", !GalaxyCompletionEvidence.AllMethodsComplete(new[] { ("HyPhy-BUSTED", "ok") }));
Check("finished methods with gene exceptions remain reviewable", GalaxyCompletionEvidence.AllMethodsSettled(new[] { ("BUSTED", "paused"), ("MEME", "error"), ("aBSREL", "ok") }));
Check("queued methods are not settled", !GalaxyCompletionEvidence.AllMethodsSettled(new[] { ("BUSTED", "ok"), ("MEME", "queued"), ("aBSREL", "ok") }));
Check("missing methods are not settled", !GalaxyCompletionEvidence.AllMethodsSettled(new[] { ("BUSTED", "ok") }));
using var mixedCollection = System.Text.Json.JsonDocument.Parse("{\"elements_states\":{\"ok\":8,\"error\":1,\"running\":1}}");
Check("active genes cannot be masked by a failed sibling", GalaxyCompletionEvidence.RecordState(mixedCollection.RootElement) == "running");
Check("retry generation groups by method and artifact type, not collection name", GalaxyCompletionEvidence.SelectionOutputKey("HyPhy-aBSREL on collection 1") == GalaxyCompletionEvidence.SelectionOutputKey("HyPhy-aBSREL on collection 2"));
Check("Markdown and JSON stay distinct during retry selection", GalaxyCompletionEvidence.SelectionOutputKey("MEME Report (Markdown)") != GalaxyCompletionEvidence.SelectionOutputKey("HyPhy-MEME"));
Check("paused method prevents full completion", !GalaxyCompletionEvidence.AllMethodsComplete(new[] { ("BUSTED", "ok"), ("MEME", "paused"), ("aBSREL", "ok") }));
Check("three completed method records permit record-level completion", GalaxyCompletionEvidence.AllMethodsComplete(new[] { ("BUSTED", "ok"), ("MEME", "ok"), ("aBSREL", "ok") }));

var n0 = string.Join('\n', new[]
{
    "HOG\tOG\tGene Tree Parent Clade\tDrosophila_melanogaster\tDrosophila_simulans\tDrosophila_sechellia",
    "N0.HOG0000001\tOG0001\tN0\tXP_1.1\tXP_2.1\tXP_3.1",
    "N0.HOG0000002\tOG0002\tN0\tXP_4.1, XP_4.2\tXP_5.1\tXP_6.1",
    "N0.HOG0000003\tOG0003\tN0\tXP_7.1\t\tXP_9.1",
    "N1.HOG0000004\tOG0004\tN1\tXP_10.1\tXP_11.1\tXP_12.1",
    "N0.HOG0000005\tOG0005\tN0\tXP_1.1\tXP_13.1\tXP_14.1",
    "N0.HOG0000006\tOG0006\tN2\tXP_15.1\tXP_16.1\tXP_17.1"
});
var filtered = GalaxyOrthoFinderPipeline.FilterN0SingleCopyRows(n0, out var speciesCount, out var rowCount);
Check("detects three metadata columns", GalaxyOrthoFinderPipeline.FindN0SpeciesStartColumn(n0.Split('\n')[0].Split('\t')) == 3);
Check("detects three species", speciesCount == 3);
Check("keeps exactly two strict N0 1:1 rows", rowCount == 2);
Check("rejects multi-copy rows", !filtered.Contains("N0.HOG0000002"));
Check("rejects rows missing a species", !filtered.Contains("N0.HOG0000003"));
Check("does not globally reject a reused accession", filtered.Contains("N0.HOG0000005"));
Check("rejects non-N0 rows", !filtered.Contains("N1.HOG0000004"));
Check("requires the Gene Tree Parent Clade to be N0", !filtered.Contains("N0.HOG0000006"));
Check("removes filtered proteome suffix from recovered species", GalaxyOrthoFinderPipeline.NormalizeRecoveredSpeciesName("macaca_mulatta_proteome_filtered.faa") == "Macaca mulatta");
Check("removes filtered CDS suffix from recovered species", GalaxyOrthoFinderPipeline.NormalizeRecoveredSpeciesName("homo_sapiens_cds_filtered.fna") == "Homo sapiens");
var normalizedTree = GalaxyOrthoFinderPipeline.NormalizeSpeciesTreeLeafLabels("(macaca_mulatta_proteome_filtered:0.1,homo_sapiens_proteome_filtered:0.2);");
Check("normalizes recovered species-tree leaf labels", normalizedTree == "(Macaca_mulatta:0.1,Homo_sapiens:0.2);");
Check(
    "excludes generated HyPhy-ready collections from retry source discovery",
    GalaxyOrthoFinderPipeline.IsGeneratedHyphyRecoveryCollection(
        "phyloDIVaS HyPhy-ready gene trees v2 [abc123]"));
Check(
    "keeps raw IQ-TREE collections eligible for retry source discovery",
    !GalaxyOrthoFinderPipeline.IsGeneratedHyphyRecoveryCollection("gene_trees"));
Check(
    "excludes prior HyPhy result collections from retry source discovery",
    GalaxyOrthoFinderPipeline.IsGeneratedHyphyRecoveryCollection("HyPhy-BUSTED on collection 42"));
Check(
    "recognizes standalone native BUSTED JSON outputs",
    GalaxyOrthoFinderPipeline.ClassifyStandaloneHyphyOutput("HyPhy-BUSTED", "hyphy_results.json", "ok") == ("json", "BUSTED"));
Check(
    "recognizes Galaxy collection-element JSON outputs when datatype is omitted",
    GalaxyOrthoFinderPipeline.ClassifyStandaloneHyphyOutput("HyPhy-aBSREL", "", "ok") == ("json", "aBSREL"));
Check(
    "recognizes standalone MEME Markdown reports",
    GalaxyOrthoFinderPipeline.ClassifyStandaloneHyphyOutput("MEME Report (Markdown) for HyPhy-MEME", "markdown", "ok") == ("markdown", "MEME"));
Check(
    "ignores HyPhy preprocessing FASTA files",
    GalaxyOrthoFinderPipeline.ClassifyStandaloneHyphyOutput("hyphy_busted_cleaned_alignment.fasta", "fasta", "ok") is null);
Check(
    "ignores failed native HyPhy outputs from older retries",
    GalaxyOrthoFinderPipeline.ClassifyStandaloneHyphyOutput("HyPhy-aBSREL", "hyphy_results.json", "error") is null);
var iqtreeReport = """
IQ-TREE 2 report

TREE IN NEWICK FORMAT
---------------------
(Dmel:0.10,(Dsim:0.20,Dsec:0.30)99:0.40);

Analysis results written to files
""";
Check(
    "recovers the final Newick tree from an IQ-TREE report",
    GalaxyOrthoFinderPipeline.ExtractNewickFromIqtreeReport(iqtreeReport) ==
    "(Dmel:0.10,(Dsim:0.20,Dsec:0.30)99:0.40);");

using var treeHistory = System.Text.Json.JsonDocument.Parse("""
[
  { "id": "species-old", "name": "SpeciesTree_rooted.txt", "extension": "newick", "hid": 40, "history_content_type": "dataset", "state": "ok", "deleted": false },
  { "id": "gene-new", "name": "N0.HOG0005248 - gene tree", "extension": "newick", "hid": 400, "history_content_type": "dataset", "state": "ok", "deleted": false },
  { "id": "normalized-new", "name": "phyloDIVaS normalized gene trees", "extension": "newick", "hid": 401, "history_content_type": "dataset", "state": "ok", "deleted": false },
  { "id": "species-failed", "name": "OrthoFinder Species Tree rooted", "extension": "newick", "hid": 402, "history_content_type": "dataset", "state": "error", "deleted": false }
]
""");
Check(
    "selects the OrthoFinder species tree instead of newer HyPhy gene trees",
    GalaxyOrthoFinderPipeline.FindOrthoFinderSpeciesTreeDatasetId(treeHistory.RootElement) == "species-old");

var selectedGenomes = new List<GalaxyOrthoFinderPipeline.GenomeInfo>
{
    new("Drosophila melanogaster", "", "protein-1", "", "cds-1")
    {
        OriginalSpecies = "Drosophila melanogaster",
        StandardizedSpecies = "drosophila_melanogaster"
    },
    new("Drosophila simulans", "", "protein-2", "", "cds-2")
    {
        OriginalSpecies = "Drosophila simulans",
        StandardizedSpecies = "drosophila_simulans"
    },
    new("Drosophila sechellia", "", "protein-3", "", "cds-3")
    {
        OriginalSpecies = "Drosophila sechellia",
        StandardizedSpecies = "drosophila_sechellia"
    }
};
var selectedGroups = new List<GalaxyOrthoFinderPipeline.OrthologousGroup>
{
    new(
        "N0.HOG0000042",
        "OG0000042",
        new List<string> { "XP_42.1", "XP_43.1", "XP_44.1" },
        new Dictionary<string, string>
        {
            ["drosophila_melanogaster"] = "XP_42.1",
            ["Drosophila simulans"] = "XP_43.1",
            ["drosophila sechellia"] = "XP_44.1"
        })
};
var selectedN0 = GalaxyOrthoFinderPipeline.BuildSelectedN0Subset(selectedGroups, selectedGenomes);
Check("builds a raw-N0-compatible selected subset", selectedN0.Contains("HOG\tOG\tGene Tree Parent Clade") && selectedN0.Contains("N0.HOG0000042\tOG0000042\tN0"));
Check("resolves original and standardized species aliases", selectedN0.Contains("XP_42.1\tXP_43.1\tXP_44.1"));
var filteredHeaderGroup = selectedGroups[0] with
{
    SpeciesGenes = new Dictionary<string, string>
    {
        ["Drosophila_melanogaster_Proteome_filtered"] = "XP_42.1",
        ["Drosophila_simulans_Proteome_filtered"] = "XP_43.1",
        ["Drosophila_sechellia_Proteome_filtered"] = "XP_44.1"
    }
};
var filteredHeaderN0 = GalaxyOrthoFinderPipeline.BuildSelectedN0Subset(new[] { filteredHeaderGroup }, selectedGenomes);
Check("resolves filtered proteome column names during submission", filteredHeaderN0.Contains("XP_42.1\tXP_43.1\tXP_44.1"));
var concatenateInputs = GalaxyOrthoFinderPipeline.BuildConcatenateInputs(
    new[] { "protein-1", "protein-2", "protein-3", "protein-2" });
Check("sends one dataset to cat1 input1", concatenateInputs["input1"].ToString()!.Contains("protein-1"));
Check("sends remaining datasets through cat1 repeat inputs",
    concatenateInputs.ContainsKey("queries_0|input2") &&
    concatenateInputs.ContainsKey("queries_1|input2") &&
    !concatenateInputs.ContainsKey("queries_2|input2"));
var rejectsSelectedMulticopy = false;
try
{
    var multicopy = selectedGroups[0] with
    {
        SpeciesGenes = new Dictionary<string, string>(selectedGroups[0].SpeciesGenes)
        {
            ["Drosophila simulans"] = "XP_43.1, XP_43.2"
        }
    };
    GalaxyOrthoFinderPipeline.BuildSelectedN0Subset(new[] { multicopy }, selectedGenomes);
}
catch (InvalidOperationException)
{
    rejectsSelectedMulticopy = true;
}
Check("rejects a selected group that is not strict 1:1", rejectsSelectedMulticopy);
Check(
    "bundles the corrected Galaxy workflow into CapstoneServices",
    typeof(GalaxyOrthoFinderPipeline).Assembly.GetManifestResourceNames()
        .Contains("CapstoneServices.GalaxyWorkflows.phyloDIVaS.Selection.v2.ga", StringComparer.Ordinal));
using (var workflowStream = typeof(GalaxyOrthoFinderPipeline).Assembly.GetManifestResourceStream(
    "CapstoneServices.GalaxyWorkflows.phyloDIVaS.Selection.v2.ga")!)
using (var workflowReader = new StreamReader(workflowStream))
{
    var workflowJson = workflowReader.ReadToEnd();
    var versioned = GalaxyOrthoFinderPipeline.VersionWorkflowDefinition(workflowJson);
    Check("assigns the bundled workflow a content revision", versioned.Name.Contains($"revision {versioned.Revision}"));
    Check("removes the exported workflow UUID before import", !versioned.Json.Contains("11920c74-9b8c-4fe8-8fdd-711f252e9194"));

    using var workflowDocument = System.Text.Json.JsonDocument.Parse(versioned.Json);
    var steps = workflowDocument.RootElement.GetProperty("steps");
    var memeInputs = steps.GetProperty("11").GetProperty("input_connections");
    var bustedInputs = steps.GetProperty("12").GetProperty("input_connections");
    var absrelInputs = steps.GetProperty("13").GetProperty("input_connections");
    Check("MEME receives codon alignment in input_file", memeInputs.GetProperty("input_file").GetProperty("id").GetInt32() == 9);
    Check("MEME receives IQ-TREE Newick in input_nhx", memeInputs.GetProperty("input_nhx").GetProperty("id").GetInt32() == 10);
    Check("bundled workflow uses standard HyPhy-MEME",
        steps.GetProperty("11").GetProperty("tool_id").GetString() ==
        "toolshed.g2.bx.psu.edu/repos/iuc/hyphy_meme/hyphy_meme/2.5.96+galaxy0");
    Check("standard MEME emits HyPhy JSON",
        steps.GetProperty("11").GetProperty("outputs")[0].GetProperty("name").GetString() == "meme_output" &&
        steps.GetProperty("11").GetProperty("outputs")[0].GetProperty("type").GetString() == "hyphy_results.json");
    Check("BUSTED receives codon alignment in input_file", bustedInputs.GetProperty("input_file").GetProperty("id").GetInt32() == 9);
    Check("BUSTED receives IQ-TREE Newick in input_nhx", bustedInputs.GetProperty("input_nhx").GetProperty("id").GetInt32() == 10);
    Check("aBSREL receives codon alignment in input_file", absrelInputs.GetProperty("input_file").GetProperty("id").GetInt32() == 9);
    Check("aBSREL receives IQ-TREE Newick in input_nhx", absrelInputs.GetProperty("input_nhx").GetProperty("id").GetInt32() == 10);

    var retryJson = GalaxyOrthoFinderPipeline.BuildHyphyRetryWorkflowDefinition(workflowJson);
    using var retryDocument = System.Text.Json.JsonDocument.Parse(retryJson);
    var retrySteps = retryDocument.RootElement.GetProperty("steps");
    Check("HyPhy retry workflow accepts an alignment collection", retrySteps.GetProperty("0").GetProperty("type").GetString() == "data_collection_input");
    Check("HyPhy retry workflow accepts a tree collection", retrySteps.GetProperty("1").GetProperty("type").GetString() == "data_collection_input");
    Check("HyPhy retry skips upstream alignment tools", retrySteps.EnumerateObject().Count() == 5);
    Check("HyPhy retry maps MEME to recovered collections",
        retrySteps.GetProperty("2").GetProperty("input_connections").GetProperty("input_file").GetProperty("id").GetInt32() == 0 &&
        retrySteps.GetProperty("2").GetProperty("input_connections").GetProperty("input_nhx").GetProperty("id").GetInt32() == 1);
    Check("HyPhy retry has no HyphAEon model configuration",
        !retryJson.Contains("hyphaeon", StringComparison.OrdinalIgnoreCase) &&
        !retryJson.Contains("model_source", StringComparison.OrdinalIgnoreCase));
    Check("HyPhy retry maps BUSTED to recovered collections",
        retrySteps.GetProperty("3").GetProperty("input_connections").GetProperty("input_file").GetProperty("id").GetInt32() == 0 &&
        retrySteps.GetProperty("3").GetProperty("input_connections").GetProperty("input_nhx").GetProperty("id").GetInt32() == 1);
    Check("HyPhy retry maps aBSREL to recovered collections",
        retrySteps.GetProperty("4").GetProperty("input_connections").GetProperty("input_file").GetProperty("id").GetInt32() == 0 &&
        retrySteps.GetProperty("4").GetProperty("input_connections").GetProperty("input_nhx").GetProperty("id").GetInt32() == 1);

    var preparationJson = GalaxyOrthoFinderPipeline.BuildPreparationWorkflowDefinition(workflowJson);
    using var preparationDocument = System.Text.Json.JsonDocument.Parse(preparationJson);
    var preparationSteps = preparationDocument.RootElement.GetProperty("steps");
    Check("preparation workflow stops after IQ-TREE", preparationSteps.EnumerateObject().Count() == 11);
    Check("preparation workflow defers all HyPhy jobs", !preparationJson.Contains("hyphy_", StringComparison.OrdinalIgnoreCase));

    var backgroundJson = GalaxyOrthoFinderPipeline.BuildBackgroundSelectionWorkflowDefinition(workflowJson);
    GalaxyOrthoFinderPipeline.ValidateUninterruptedSelectionWorkflow(backgroundJson);
    Check("unattended workflow passes dependency validation", true);
    var rejectedIncompleteWorkflow = false;
    try { GalaxyOrthoFinderPipeline.ValidateUninterruptedSelectionWorkflow(preparationJson); }
    catch (InvalidOperationException) { rejectedIncompleteWorkflow = true; }
    Check("unattended submission rejects a workflow ending at IQ-TREE", rejectedIncompleteWorkflow);
    using var backgroundDocument = System.Text.Json.JsonDocument.Parse(backgroundJson);
    var backgroundSteps = backgroundDocument.RootElement.GetProperty("steps");
    Check("background workflow retains all three HyPhy tools",
        backgroundSteps.TryGetProperty("11", out _) &&
        backgroundSteps.TryGetProperty("12", out _) &&
        backgroundSteps.TryGetProperty("13", out _));
    Check("background workflow normalizes protein and CDS headers inside Galaxy",
        backgroundSteps.GetProperty("14").GetProperty("tool_id").GetString() ==
            "toolshed.g2.bx.psu.edu/repos/bgruening/text_processing/tp_awk_tool/9.11+galaxy0" &&
        backgroundSteps.GetProperty("15").GetProperty("tool_id").GetString() ==
            "toolshed.g2.bx.psu.edu/repos/bgruening/text_processing/tp_awk_tool/9.11+galaxy0");
    Check("MAFFT consumes normalized protein identifiers",
        backgroundSteps.GetProperty("8").GetProperty("input_connections")
            .GetProperty("input|batches_0|inputs").GetProperty("id").GetInt32() == 14);
    Check("tranalign consumes normalized CDS identifiers",
        backgroundSteps.GetProperty("9").GetProperty("input_connections")
            .GetProperty("input1").GetProperty("id").GetInt32() == 15);
    Check("HyPhy remains downstream of Galaxy IQ-TREE in the uninterrupted workflow",
        backgroundSteps.GetProperty("11").GetProperty("input_connections").GetProperty("input_nhx").GetProperty("id").GetInt32() == 10 &&
        backgroundSteps.GetProperty("12").GetProperty("input_connections").GetProperty("input_nhx").GetProperty("id").GetInt32() == 10 &&
        backgroundSteps.GetProperty("13").GetProperty("input_connections").GetProperty("input_nhx").GetProperty("id").GetInt32() == 10);

    var postMafftJson = GalaxyOrthoFinderPipeline.BuildPostMafftRecoveryWorkflowDefinition(workflowJson);
    using var postMafftDocument = System.Text.Json.JsonDocument.Parse(postMafftJson);
    var postMafftSteps = postMafftDocument.RootElement.GetProperty("steps");
    Check("post-MAFFT recovery accepts alignment and CDS collections",
        postMafftSteps.GetProperty("0").GetProperty("type").GetString() == "data_collection_input" &&
        postMafftSteps.GetProperty("1").GetProperty("type").GetString() == "data_collection_input");
    Check("post-MAFFT recovery starts at back-translation",
        postMafftSteps.GetProperty("2").GetProperty("input_connections").GetProperty("input1").GetProperty("id").GetInt32() == 1 &&
        postMafftSteps.GetProperty("2").GetProperty("input_connections").GetProperty("input2").GetProperty("id").GetInt32() == 0);
    Check("post-MAFFT recovery routes IQ-TREE into every HyPhy tool",
        postMafftSteps.GetProperty("4").GetProperty("input_connections").GetProperty("input_nhx").GetProperty("id").GetInt32() == 3 &&
        postMafftSteps.GetProperty("5").GetProperty("input_connections").GetProperty("input_nhx").GetProperty("id").GetInt32() == 3 &&
        postMafftSteps.GetProperty("6").GetProperty("input_connections").GetProperty("input_nhx").GetProperty("id").GetInt32() == 3);
}

var agpatLikeFasta = string.Join('\n', new[]
{
    ">NP_723398.1 [gene=Agpat2] [protein=AGPAT2]", "ATGGCTGCT",
    ">XP_016024200.1 [gene=Agpat2]", "ATGGCCGCT",
    ">XP_043071194.1 [gene=Agpat2]", "ATGGCTGCC",
    ">XP_017155652.1 [gene=Agpat2]", "ATGGCTGCA",
    ">XP_023163838.1 [gene=Agpat2]", "ATGGCGGCT",
    ">XP_033218421.1 [gene=Agpat2]", "ATGGCTGCG",
    ">XP_041155619.1 [gene=Agpat2]", "ATGGCCGCC",
    ">XP_047489390.1 [gene=Agpat2]", "ATGGCGGCC"
});
var agpatLikeTree = "((NP_723398.1:0.0131514887,XP_016024200.1:0.0024643127)'node one'[&support=98]:0.01,(XP_043071194.1:0.02,XP_017155652.1:0.03){Foreground}:0.04,(XP_023163838.1:0.05,(XP_033218421.1:0.06,(XP_041155619.1:0.07,XP_047489390.1:0.08):0.09):0.10):0.11);";
var normalizedHyphy = HyphyInputNormalizer.NormalizeAndValidate(agpatLikeFasta, agpatLikeTree);
Check("removes FASTA metadata and normalizes accession punctuation",
    normalizedHyphy.Fasta.Contains(">NP_723398_1\n") &&
    !normalizedHyphy.Fasta.Contains("[gene="));
Check("applies the same mapping to Newick leaves",
    normalizedHyphy.Newick.Contains("NP_723398_1:0.0131514887") &&
    normalizedHyphy.Newick.Contains("XP_016024200_1:0.0024643127"));
Check("preserves Newick branch lengths and annotations",
    normalizedHyphy.Newick.Contains("'node one'[&support=98]:0.01") &&
    normalizedHyphy.Newick.Contains("{Foreground}:0.04"));
var normalizedAgain = HyphyInputNormalizer.NormalizeAndValidate(normalizedHyphy.Fasta, normalizedHyphy.Newick);
Check("HyPhy normalization is idempotent",
    normalizedAgain.Fasta == normalizedHyphy.Fasta && normalizedAgain.Newick == normalizedHyphy.Newick);

var alternateHeaderFasta = string.Join('\n', new[]
{
    ">lcl|sample-a_cds_XP_016024200.1 [protein_id=XP_016024200.1]", "ATGGCCGCT",
    ">lcl|sample-b_cds_XP_043071194.1 [protein_id=XP_043071194.1]", "ATGGCTGCC",
    ">lcl|sample-c_cds_XP_017155652.1 [protein_id=XP_017155652.1]", "ATGGCTGCA"
});
var alternateHeaderTree = "(XP_016024200.1:0.1,(XP_043071194.1:0.2,XP_017155652.1:0.3):0.4);";
var reconciledHyphy = HyphyInputNormalizer.NormalizeAndValidate(alternateHeaderFasta, alternateHeaderTree);
Check("rewrites alternate FASTA headers to the corresponding Newick leaf identifiers",
    reconciledHyphy.Fasta.Contains(">XP_016024200_1\nATGGCCGCT") &&
    reconciledHyphy.Fasta.Contains(">XP_043071194_1\nATGGCTGCC") &&
    reconciledHyphy.Fasta.Contains(">XP_017155652_1\nATGGCTGCA"));
Check("does not modify alignment nucleotide data while reconciling headers",
    reconciledHyphy.Fasta.Split('\n').Where(line => line.Length > 0 && !line.StartsWith('>'))
        .SequenceEqual(new[] { "ATGGCCGCT", "ATGGCTGCC", "ATGGCTGCA" }));
Check("keeps reconciled alignment and Newick identifiers identical",
    reconciledHyphy.Newick.Contains("XP_016024200_1:0.1") &&
    reconciledHyphy.Newick.Contains("XP_043071194_1:0.2") &&
    reconciledHyphy.Newick.Contains("XP_017155652_1:0.3"));

void CheckHyphyRejected(string name, string fasta, string tree, string expectedMessage)
{
    try
    {
        HyphyInputNormalizer.NormalizeAndValidate(fasta, tree);
        throw new Exception($"FAILED: {name}");
    }
    catch (InvalidOperationException ex)
    {
        Check(name, ex.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase));
    }
}

CheckHyphyRejected(
    "rejects normalized identifier collisions",
    ">A.1\nATGGCT\n>A_1\nATGGCT\n>B.1\nATGGCT\n",
    "(A.1:0.1,A_1:0.1,B.1:0.1);",
    "collision");
CheckHyphyRejected(
    "rejects duplicate FASTA identifiers",
    ">A.1\nATGGCT\n>A.1\nATGGCT\n>B.1\nATGGCT\n",
    "(A.1:0.1,B.1:0.1,C.1:0.1);",
    "duplicate FASTA identifier");
CheckHyphyRejected(
    "rejects unequal alignment lengths",
    ">A.1\nATGGCT\n>B.1\nATG\n>C.1\nATGGCT\n",
    "(A.1:0.1,B.1:0.1,C.1:0.1);",
    "lengths differ");
CheckHyphyRejected(
    "rejects alignments outside the codon frame",
    ">A.1\nATGG\n>B.1\nATGG\n>C.1\nATGG\n",
    "(A.1:0.1,B.1:0.1,C.1:0.1);",
    "divisible by three");
CheckHyphyRejected(
    "rejects invalid nucleotide characters",
    ">A.1\nATGXCT\n>B.1\nATGGCT\n>C.1\nATGGCT\n",
    "(A.1:0.1,B.1:0.1,C.1:0.1);",
    "invalid nucleotide");
CheckHyphyRejected(
    "rejects partial codon gaps",
    ">A.1\nATGG-T\n>B.1\nATGGCT\n>C.1\nATGGCT\n",
    "(A.1:0.1,B.1:0.1,C.1:0.1);",
    "partial codon gap");
CheckHyphyRejected(
    "rejects internal stop codons",
    ">A.1\nATGTAAATG\n>B.1\nATGGCTATG\n>C.1\nATGGCTATG\n",
    "(A.1:0.1,B.1:0.1,C.1:0.1);",
    "internal stop");
CheckHyphyRejected(
    "rejects missing or extra tree taxa",
    ">A.1\nATGGCT\n>B.1\nATGGCT\n>C.1\nATGGCT\n",
    "(A.1:0.1,B.1:0.1,D.1:0.1);",
    "not present in the alignment");

var proteins = string.Join('\n', new[]
{
    ">XP_100.1 short isoform", "MK",
    ">XP_101.1 long isoform", "MKKK",
    ">XP_200.2 second gene", "MPP"
});
var cds = string.Join('\n', new[]
{
    ">cds1 [gene=alpha] [protein_id=XP_100.1]", "ATGAAA",
    ">cds2 [gene=alpha] [protein_id=XP_101.1]", "ATGAAAAAAAAA",
    ">cds3 [locus_tag=LOC2] [protein_id=XP_200.1]", "ATGCCCCCC"
});
var isoforms = GalaxyOrthoFinderPipeline.SelectLongestIsoformPairs(proteins, cds);
GalaxyOrthoFinderPipeline.EnsureIsoformSelectionIsUsable(isoforms, "test species");
Check("selects one isoform per gene or locus tag", isoforms.MatchedPairs == 2);
Check("selects the longest CDS isoform", isoforms.Cds.Contains("XP_101.1") && !isoforms.Cds.Contains("XP_100.1"));
Check("retains the matching protein", isoforms.Proteins.Contains("XP_101.1") && !isoforms.Proteins.Contains("XP_100.1"));
Check("matches versionless protein accessions", isoforms.Proteins.Contains("XP_200.2"));
Check("normalizes CDS primary IDs to protein accessions", isoforms.Cds.Contains(">XP_101.1 [gene=alpha]") && isoforms.Cds.Contains(">XP_200.2 [locus_tag=LOC2]"));
Check("uses the matched protein version for both FASTA files", isoforms.Proteins.Contains(">XP_200.2 second gene") && isoforms.Cds.Contains(">XP_200.2 [locus_tag=LOC2]"));
Check("does not retain original CDS primary IDs", !isoforms.Cds.Contains(">cds1 ") && !isoforms.Cds.Contains(">cds2 ") && !isoforms.Cds.Contains(">cds3 "));

var translationChecked = GalaxyOrthoFinderPipeline.SelectLongestIsoformPairs(
    string.Join('\n', new[]
    {
        ">XP_500.1 corrected low-quality model", "MKK",
        ">XP_501.1 valid model", "MKK"
    }),
    string.Join('\n', new[]
    {
        ">bad [gene=corrected] [protein_id=XP_500.1]", "ATGTAGAAA",
        ">good [gene=valid] [protein_id=XP_501.1]", "ATGAAAAAA"
    }));
GalaxyOrthoFinderPipeline.EnsureIsoformSelectionIsUsable(translationChecked, "translation test species");
Check("excludes CDS records that do not translate to their paired protein", translationChecked.MatchedPairs == 1 && translationChecked.TranslationInvalidPairs == 1);
Check("keeps translation-compatible protein/CDS pairs", translationChecked.Proteins.Contains("XP_501.1") && !translationChecked.Proteins.Contains("XP_500.1"));

var rejectsUntaggedCds = false;
try
{
    var untagged = GalaxyOrthoFinderPipeline.SelectLongestIsoformPairs(
        ">XP_300.1 protein\nMAAAA\n",
        ">cds_without_required_tags\nATGAAAAAA\n");
    GalaxyOrthoFinderPipeline.EnsureIsoformSelectionIsUsable(untagged, "untagged species");
}
catch (InvalidOperationException)
{
    rejectsUntaggedCds = true;
}
Check("does not silently send an unfiltered proteome to OrthoFinder", rejectsUntaggedCds);

var incomplete = GalaxyOrthoFinderPipeline.SelectLongestIsoformPairs(
    ">XP_400.1 protein\nMKK\n",
    string.Join('\n', new[]
    {
        ">cds4 [gene=gamma] [protein_id=XP_400.1]", "ATGAAAAAA",
        ">cds5 [gene=delta] [protein_id=XP_401.1]", "ATGCCCCCC"
    }));
GalaxyOrthoFinderPipeline.EnsureIsoformSelectionIsUsable(incomplete, "incomplete species");
Check("retains valid pairs when an NCBI CDS has no protein record",
    incomplete.MatchedPairs == 1 && incomplete.UnmatchedProteinPairs == 1);
Check("does not pass unmatched CDS records to downstream tools",
    !incomplete.Cds.Contains("XP_401.1") && !incomplete.Proteins.Contains("XP_401.1"));

if (args.Length > 0)
{
    var referenceN0 = File.ReadAllText(args[0]);
    GalaxyOrthoFinderPipeline.FilterN0SingleCopyRows(
        referenceN0, out var referenceSpeciesCount, out var referenceRowCount);
    Console.WriteLine($"REFERENCE: {referenceRowCount} strict N0 1:1 rows across {referenceSpeciesCount} species");
    if (args.Length > 1 && int.TryParse(args[1], out var expectedReferenceRows))
        Check($"reference N0 filter returns {expectedReferenceRows} rows", referenceRowCount == expectedReferenceRows);
}

Console.WriteLine($"{passed} pipeline logic tests passed.");
