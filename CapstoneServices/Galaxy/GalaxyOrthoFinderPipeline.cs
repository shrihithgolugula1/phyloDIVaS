using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Collections.Concurrent;
using CapstoneServices.NCBI;
using Microsoft.Extensions.Logging;

namespace CapstoneServices.Galaxy
{
    public sealed class GalaxyOrthoFinderPipeline
    {
        public const int MinSpeciesCount = 3;
        public const int MaxSpeciesCount = 10;

        private readonly GalaxyClient _galaxy;
        private readonly NcbiClient _ncbi;
        private readonly GalaxyOptions _opts;
        private readonly ILogger<GalaxyOrthoFinderPipeline> _log;
        private const string MetadataDatasetName = "dnds-genome-analysis.json"; // retained for backward compatibility only
        private const string StrictN0DatasetPrefix = "phyloDIVaS strict 1-to-1 N0 orthogroups (";
        private const string NormalizedHyphyCodonCollectionName = "phyloDIVaS HyPhy-ready codon alignments v4";
        private const string NormalizedHyphyTreeCollectionName = "phyloDIVaS HyPhy-ready gene trees v4";
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> StrictN0HistoryLocks =
            new(StringComparer.OrdinalIgnoreCase);

        public GalaxyOrthoFinderPipeline(GalaxyClient galaxy, NcbiClient ncbi, Microsoft.Extensions.Options.IOptions<GalaxyOptions> opts, ILogger<GalaxyOrthoFinderPipeline> log)
        { 
            _galaxy = galaxy; 
            _ncbi = ncbi; 
            _opts = opts.Value; 
            _log = log; 
        }

        private string BuildDatasetDownloadUrl(string datasetId)
        {
            var baseUrl = _opts.BaseUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return string.Empty;
            baseUrl = baseUrl.TrimEnd('/');
            if (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
                baseUrl = baseUrl[..^4];
            return $"{baseUrl}/api/datasets/{Uri.EscapeDataString(datasetId)}/display";
        }

        public sealed record StartGenomeAnalysisResult(
            string HistoryId, 
            string OrthoFinderJobId, 
            JsonElement InvocationRaw, 
            Dictionary<string, string> StepDatasetIds,
            List<GenomeInfo> Genomes,
            string? WorkflowInvocationId = null,
            string? Status = null,
            string? StatusDetail = null,
            List<HyphyOutput>? HyphyMarkdownOutputs = null,
            List<HyphyOutput>? HyphyJsonOutputs = null
        );

        public sealed record GenomeInfo(string Species, string ProteinUrl, string ProteinDatasetId, string CdsUrl, string CdsDatasetId)
        {
            // Legacy aliases to keep existing DTO projections working
            public string GenomeUrl => ProteinUrl;
            public string DatasetId => ProteinDatasetId;
            public string OriginalSpecies { get; init; } = Species;
            public string StandardizedSpecies { get; init; } = Species;
        }
        
        // Helper record to track unzip jobs that need to complete before extracting dataset IDs
        private sealed record UnzipJobInfo(string JobId, string CollectionId, string TargetFilePath);
        private sealed record UploadedZip(string Species, string ProteinZipId, string CdsZipId);

        public sealed record OrthologousGroup(string GroupId, string GroupName, List<string> GeneIds, Dictionary<string, string> SpeciesGenes);

        public sealed record SelectionAnalysisResult(
            string HistoryId,
            string CodonAlignedDatasetId,
            string BustedJobId,
            JsonElement InvocationRaw,
            Dictionary<string, string> StepDatasetIds
        );

        public sealed record HyphyRetryResult(
            string HistoryId,
            string InvocationId,
            string CodonAlignmentCollectionId,
            string GeneTreeCollectionId,
            string WorkflowId,
            JsonElement InvocationRaw);

        public sealed record DownstreamRecoveryResult(
            string HistoryId,
            string InvocationId,
            string MafftCollectionId,
            string CdsCollectionId,
            string WorkflowId,
            JsonElement InvocationRaw);

        private sealed record ToolRun(string JobId, string OutputDatasetId, JsonElement Raw);
        private sealed record StrictN0DatasetCandidate(string Id, string Name, string State, int Hid);

        private sealed record GenomeAnalysisMetadata(
            string HistoryId,
            string? OrthoFinderJobId,
            List<GenomeInfo> Genomes,
            string Status,
            string? StatusDetail = null,
            string? ProteomeCollectionId = null,
            string? CdsCollectionId = null,
            string? SpeciesMappingId = null,
            string? WorkflowInvocationId = null,
            List<HyphyOutput>? HyphyMarkdownOutputs = null,
            List<HyphyOutput>? HyphyJsonOutputs = null
        );

        public sealed record HyphyOutput(
            string Id,
            string Name,
            string? ElementIdentifier = null,
            string? Method = null,
            string? State = null);

        public async Task<(List<HyphyOutput> Markdown, List<HyphyOutput> Json)> GetHyphyOutputsAsync(
            string historyId,
            string? galaxyApiKey,
            CancellationToken ct)
        {
            using var apiScope = UseApiKeyForHistory(historyId, galaxyApiKey);
            using var historyReadCache = _galaxy.UseHistoryReadCache();
            await _galaxy.PrimeHistoryContentsCacheAsync(historyId, ct);
            return await FindHyphyOutputsInHistoryAsync(historyId, ct);
        }

        public sealed record ResultArtifact(
            string Id,
            string Name,
            string ElementIdentifier,
            string Kind,
            string? Method,
            string? Extension,
            string State,
            string CollectionName);

        internal static (string Kind, string Method)? ClassifyStandaloneHyphyOutput(
            string? name,
            string? extension,
            string? state)
        {
            if (!string.Equals(state, "ok", StringComparison.OrdinalIgnoreCase))
                return null;

            var outputName = name ?? string.Empty;
            var outputExtension = extension ?? string.Empty;
            var method = outputName.Contains("absrel", StringComparison.OrdinalIgnoreCase) ? "aBSREL" :
                outputName.Contains("busted", StringComparison.OrdinalIgnoreCase) ? "BUSTED" :
                outputName.Contains("meme", StringComparison.OrdinalIgnoreCase) ? "MEME" : null;
            if (method is null)
                return null;

            var isMarkdown = outputExtension.Contains("markdown", StringComparison.OrdinalIgnoreCase) ||
                outputName.Contains("report", StringComparison.OrdinalIgnoreCase) ||
                outputName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
            if (isMarkdown)
                return ("markdown", method);

            var isNativeJson = outputExtension.Contains("hyphy_results.json", StringComparison.OrdinalIgnoreCase) ||
                outputExtension.Equals("json", StringComparison.OrdinalIgnoreCase) ||
                outputName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                outputName.StartsWith("HyPhy-BUSTED", StringComparison.OrdinalIgnoreCase) ||
                outputName.StartsWith("HyPhy-aBSREL", StringComparison.OrdinalIgnoreCase) ||
                outputName.StartsWith("HyPhy-MEME", StringComparison.OrdinalIgnoreCase);
            return isNativeJson ? ("json", method) : null;
        }

        public sealed record OrthoFinderOutputItem(string Label, string DatasetId, string Description, string? SuggestedExtension = null);

        public sealed record OrthoFinderOutputs(
            string HistoryId,
            string OrthoFinderJobId,
            string? SpeciesTreeDatasetId,
            string? SpeciesTreeNewick,
            string? OrthogroupsDatasetId,
            string? SingleCopyDatasetId,
            List<OrthoFinderOutputItem> Outputs,
            bool SingleCopySourceIsFiltered = false);

        [Obsolete("Galaxy API keys are request-scoped and are never cached by history.")]
        public void RememberApiKeyForHistory(string historyId, string? apiKey)
        {
            // Intentionally empty. Retaining credentials beyond the active request is prohibited.
        }

        private IDisposable UseApiKeyForHistory(string historyId, string? apiKey = null)
        {
            return string.IsNullOrWhiteSpace(apiKey) ? NoopDisposable.Instance : _galaxy.UseApiKey(apiKey);
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }

        public async Task<OrthoFinderOutputs> GetOrthoFinderOutputsAsync(string historyId, string orthoFinderJobId, CancellationToken ct)
        {
            using var apiScope = UseApiKeyForHistory(historyId);
            using var outputReadCache = _galaxy.UseHistoryReadCache();
            await _galaxy.PrimeHistoryContentsCacheAsync(historyId, ct);

            var isLikelyJobId = !string.IsNullOrWhiteSpace(orthoFinderJobId) && orthoFinderJobId.Length >= 16;
            string? speciesTreeId = null;
            string? speciesTreeNewick = null;
            string? orthoGroupsDatasetId = null;
            string? singleCopyDatasetId = null;
            string? hierarchicalDatasetId = null;

            try
            {
                speciesTreeId = await FindNewickTreeInHistoryAsync(historyId, ct);
                if (!string.IsNullOrWhiteSpace(speciesTreeId))
                {
                    speciesTreeNewick = await _galaxy.DownloadDatasetAsync(historyId, speciesTreeId, ct);
                    var recovered = await TryRehydrateGenomesFromCollectionsAsync(historyId, ct);
                    speciesTreeNewick = NormalizeSpeciesTreeLeafLabels(speciesTreeNewick, recovered?.Genomes);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Unable to fetch species tree for history {HistoryId}", historyId);
            }

            try
            {
                JsonElement outputs = default;
                var hasJobOutputs = false;
                if (isLikelyJobId)
                {
                    outputs = await _galaxy.GetJobOutputsAsync(orthoFinderJobId, ct);
                    hasJobOutputs = outputs.ValueKind != JsonValueKind.Undefined && outputs.ValueKind != JsonValueKind.Null;
                }
                // Prefer standard Orthogroups.tsv, but keep hierarchical as a fallback
                if (hasJobOutputs)
                {
                    orthoGroupsDatasetId = FindOrthoGroupsOutputDataset(outputs);
                    hierarchicalDatasetId = await FindHierarchicalOrthoGroupsOutputDatasetAsync(historyId, outputs, ct);
                }

                if (string.IsNullOrWhiteSpace(orthoGroupsDatasetId))
                    orthoGroupsDatasetId = await FindOrthoGroupsDatasetInHistoryAsync(historyId, ct);

                if (string.IsNullOrWhiteSpace(hierarchicalDatasetId))
                    hierarchicalDatasetId = await FindHierarchicalOrthoGroupsDatasetInHistoryAsync(historyId, ct);

                if (string.IsNullOrWhiteSpace(orthoGroupsDatasetId) && !string.IsNullOrWhiteSpace(hierarchicalDatasetId))
                    orthoGroupsDatasetId = hierarchicalDatasetId;

            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Unable to locate orthogroups output for history {HistoryId}", historyId);
            }
            finally
            {
                if (string.IsNullOrWhiteSpace(orthoGroupsDatasetId))
                {
                    try { orthoGroupsDatasetId = await FindOrthoGroupsDatasetInHistoryAsync(historyId, ct); } catch (Exception ex) { _log.LogWarning(ex, "History scan failed for Orthogroups.tsv in history {HistoryId}", historyId); }
                }
                if (string.IsNullOrWhiteSpace(hierarchicalDatasetId))
                {
                    try { hierarchicalDatasetId = await FindHierarchicalOrthoGroupsDatasetInHistoryAsync(historyId, ct); } catch (Exception ex) { _log.LogWarning(ex, "History scan failed for hierarchical orthogroups in history {HistoryId}", historyId); }
                }
                if (string.IsNullOrWhiteSpace(orthoGroupsDatasetId) && !string.IsNullOrWhiteSpace(hierarchicalDatasetId))
                    orthoGroupsDatasetId = hierarchicalDatasetId;
            }

            try
            {
                singleCopyDatasetId = await FindFilteredSingleCopyDatasetInHistoryAsync(historyId, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Unable to locate filtered single-copy dataset for history {HistoryId}", historyId);
            }

            var downloads = new List<OrthoFinderOutputItem>();

            if (!string.IsNullOrWhiteSpace(orthoGroupsDatasetId))
            {
                downloads.Add(new OrthoFinderOutputItem(
                    "OrthoFinder membership table",
                    orthoGroupsDatasetId,
                    "Available primary orthogroup membership output; not necessarily the strict 1:1 subset.",
                    "tsv"));
            }

            if (!string.IsNullOrWhiteSpace(singleCopyDatasetId))
            {
                downloads.Add(new OrthoFinderOutputItem(
                    "Filtered Single-Copy Orthogroups",
                    singleCopyDatasetId,
                    "Single-copy orthogroups after Filter1 (tabular).",
                    "tsv"));
            }

            if (!string.IsNullOrWhiteSpace(speciesTreeId))
            {
                downloads.Add(new OrthoFinderOutputItem(
                    "Species Tree (Newick)",
                    speciesTreeId,
                    "OrthoFinder inferred species tree (Newick).",
                    "nwk"));
            }

            if (string.IsNullOrWhiteSpace(singleCopyDatasetId) && !string.IsNullOrWhiteSpace(hierarchicalDatasetId))
                downloads.Add(new OrthoFinderOutputItem("Raw hierarchical orthogroups (N0)", hierarchicalDatasetId,
                    "Unfiltered source table. The application applies strict root 1:1 filtering in memory; this download contains the original rows.", "tsv"));

            return new OrthoFinderOutputs(
                historyId,
                orthoFinderJobId,
                speciesTreeId,
                speciesTreeNewick,
                orthoGroupsDatasetId,
                singleCopyDatasetId ?? hierarchicalDatasetId,
                downloads,
                !string.IsNullOrWhiteSpace(singleCopyDatasetId));
        }

        private static string StandardizeSpeciesName(string species)
        {
            if (string.IsNullOrWhiteSpace(species))
                return string.Empty;

            var sb = new StringBuilder();
            bool lastUnderscore = false;

            foreach (var ch in species.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    lastUnderscore = false;
                }
                else
                {
                    if (!lastUnderscore)
                    {
                        sb.Append('_');
                        lastUnderscore = true;
                    }
                }
            }

            var result = sb.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(result) ? string.Empty : result;
        }

        private async Task<string?> UploadSpeciesMappingAsync(string historyId, IReadOnlyList<(string Original, string Standardized)> speciesRows, CancellationToken ct)
        {
            if (speciesRows is null || speciesRows.Count == 0)
                return null;

            var contentBuilder = new StringBuilder();
            contentBuilder.AppendLine("original_species\tstandardized_species");

            foreach (var row in speciesRows)
            {
                if (string.IsNullOrWhiteSpace(row.Original) && string.IsNullOrWhiteSpace(row.Standardized))
                    continue;

                contentBuilder.Append(row.Original);
                contentBuilder.Append('\t');
                contentBuilder.AppendLine(row.Standardized);
            }

            var content = contentBuilder.ToString();
            if (string.IsNullOrWhiteSpace(content))
                return null;

            try
            {
                var datasetId = await _galaxy.UploadTextContentAsync(historyId, content, "species_mapping.tabular", "tabular", ct);
                _log.LogInformation("Uploaded species mapping table to Galaxy history {HistoryId} as dataset {DatasetId}", historyId, datasetId);
                return datasetId;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to upload species mapping table to history {HistoryId}", historyId);
                return null;
            }
        }

        private static string? ExtractInvocationId(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            // Extract the last hex-like token with length multiple of 16
            var tokens = System.Text.RegularExpressions.Regex.Matches(content, @"[a-fA-F0-9]{16,}")
                .Select(m => m.Value)
                .Where(v => v.Length % 16 == 0)
                .OrderByDescending(v => v.Length)
                .ToList();
            return tokens.FirstOrDefault();
        }

        public async Task<Dictionary<string, List<string>>> GetSingleCopyGeneIdsAsync(string historyId, string orthogroupId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(orthogroupId))
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            var filteredId = await FindFilteredSingleCopyDatasetInHistoryAsync(historyId, ct);
            if (string.IsNullOrWhiteSpace(filteredId))
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            var content = await _galaxy.DownloadDatasetAsync(historyId, filteredId!, ct);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            var headerParts = lines[0].Split('\t');
            var target = orthogroupId.Trim();
            Dictionary<string, List<string>> result = new(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\t');
                if (parts.Length == 0) continue;
                if (!string.Equals(parts[0].Trim(), target, StringComparison.OrdinalIgnoreCase))
                    continue;

                for (int col = 3; col < parts.Length && col < headerParts.Length; col++)
                {
                    var species = headerParts[col].Trim();
                    if (string.IsNullOrWhiteSpace(species)) continue;
                    var ids = parts[col]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                    result[species] = ids;
                }
                break;
            }

            return result;
        }

        private async Task<(List<HyphyOutput> Markdown, List<HyphyOutput> Json)> FindHyphyOutputsFromInvocationAsync(string invocationId, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(invocationId) || invocationId.Length < 16)
                    return (new List<HyphyOutput>(), new List<HyphyOutput>());

                var inv = await _galaxy.GetInvocationAsync(invocationId, ct);
                var historyId = inv.TryGetProperty("history_id", out var hid) ? hid.GetString() : null;
                if (string.IsNullOrWhiteSpace(historyId))
                    return (new List<HyphyOutput>(), new List<HyphyOutput>());

                if (!inv.TryGetProperty("output_collections", out var oc) || oc.ValueKind != JsonValueKind.Object)
                    return (new List<HyphyOutput>(), new List<HyphyOutput>());

                var outputCollections = new List<(string Id, string Method, bool IsMarkdown)>();

                foreach (var prop in oc.EnumerateObject())
                {
                    var key = prop.Name ?? string.Empty;
                    var val = prop.Value;
                    var colId = val.ValueKind == JsonValueKind.Object && val.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    if (string.IsNullOrWhiteSpace(colId))
                        continue;

                    var method = key.Contains("absrel", StringComparison.OrdinalIgnoreCase) ? "aBSREL" :
                        key.Contains("meme", StringComparison.OrdinalIgnoreCase) ? "MEME" :
                        key.Contains("busted", StringComparison.OrdinalIgnoreCase) ? "BUSTED" : null;
                    if (method is null)
                        continue;
                    var isMarkdown = key.Contains("report", StringComparison.OrdinalIgnoreCase) ||
                                     key.Contains("markdown", StringComparison.OrdinalIgnoreCase);
                    outputCollections.Add((colId, method, isMarkdown));
                }

                var markdown = new List<HyphyOutput>();
                var json = new List<HyphyOutput>();

                async Task AddFromCollection(string? collectionId, string method, bool isMarkdown)
                {
                    if (string.IsNullOrWhiteSpace(collectionId))
                        return;

                    var details = await _galaxy.GetCollectionDetailsAsync(historyId!, collectionId!, ct);
                    if (details.ValueKind != JsonValueKind.Object || !details.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
                        return;

                    foreach (var el in elements.EnumerateArray())
                    {
                        var identifier = el.TryGetProperty("element_identifier", out var ident) ? ident.GetString() : null;
                        var dsId = el.TryGetProperty("object", out var obj) && obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("id", out var idProp)
                            ? idProp.GetString()
                            : el.TryGetProperty("id", out var directId) ? directId.GetString() : null;
                        if (string.IsNullOrWhiteSpace(dsId))
                            continue;

                        var name = identifier ?? dsId;
                        var hy = new HyphyOutput(dsId!, $"{method} - {name}", identifier, method);
                        if (isMarkdown)
                            markdown.Add(hy);
                        else
                            json.Add(hy);
                    }
                }

                foreach (var output in outputCollections)
                    await AddFromCollection(output.Id, output.Method, output.IsMarkdown);

                return (markdown, json);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Failed to read HyPhy outputs from invocation {InvocationId}", invocationId);
                return (new List<HyphyOutput>(), new List<HyphyOutput>());
            }
        }

        /// <summary>
        /// Step 1: Start the genome-based analysis workflow by downloading genomes and running OrthoFinder
        /// </summary>
        public async Task<StartGenomeAnalysisResult> StartGenomeAnalysisAsync(
            IReadOnlyList<string> species, 
            string? ncbiApiKey,
            string? galaxyApiKey,
            CancellationToken ct,
            IProgressReporter? progressReporter = null)
        {
            if (species is null || species.Count < MinSpeciesCount || species.Count > MaxSpeciesCount)
                throw new ArgumentException($"Provide between {MinSpeciesCount} and {MaxSpeciesCount} distinct species", nameof(species));

            progressReporter ??= NullProgressReporter.Instance;
            using var apiScope = _galaxy.UseApiKey(galaxyApiKey);
            
            progressReporter.ReportProgress("Initializing", "Creating Galaxy history for analysis", 0, species.Count);

            // 1) Create history
            string historyId = await _galaxy.CreateHistoryAsync($"orthofinder-genomes-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}", ct);

            // Upload standardized species mapping table to keep labels consistent in downstream workflows
            var speciesMappings = species
                .Select(sp => sp?.Trim())
                .Where(sp => !string.IsNullOrWhiteSpace(sp))
                .Select(sp => (Original: sp!, Standardized: StandardizeSpeciesName(sp!)))
                .ToList();

            string? speciesMappingId = null;
            if (speciesMappings.Count > 0)
            {
                progressReporter.ReportProgress(
                    "Preparing Species Map", 
                    "Uploading standardized species mapping table to Galaxy", 
                    0, 
                    species.Count,
                    new { historyId });

                speciesMappingId = await UploadSpeciesMappingAsync(historyId, speciesMappings, ct);
            }

            // 2) Download and upload genome files (protein + CDS FASTA)
            var genomes = new List<GenomeInfo>();
            var genomeIssues = new List<GenomeLookupFailure>();
            
            // Track unzip jobs to wait for them all concurrently
            var unzipJobs = new List<(string Species, string ProteinUrl, string CdsUrl, Task<UnzipJobInfo> ProteinTask, Task<UnzipJobInfo> CdsTask)>();
            var uploadedZips = new List<UploadedZip>();
            
            int processedSpecies = 0;
            
            foreach (var sp in species)
            {
                var originalSpecies = sp?.Trim() ?? string.Empty;
                var lookupSpecies = string.IsNullOrWhiteSpace(originalSpecies)
                    ? string.Empty
                    : originalSpecies.Replace('_', ' ').Replace("  ", " ").Trim();
                var standardizedSpecies = StandardizeSpeciesName(originalSpecies);
                if (string.IsNullOrWhiteSpace(standardizedSpecies) && !string.IsNullOrWhiteSpace(originalSpecies))
                    standardizedSpecies = originalSpecies.Trim().Replace(' ', '_').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(standardizedSpecies))
                    standardizedSpecies = "species";
                var displaySpecies = string.IsNullOrWhiteSpace(originalSpecies) ? standardizedSpecies : originalSpecies;

                if (string.IsNullOrWhiteSpace(originalSpecies))
                {
                    genomeIssues.Add(new GenomeLookupFailure(
                        originalSpecies, 
                        "Species name was empty or null.", 
                        "Remove blank entries before starting analysis."));
                    continue;
                }

                try
                {
                    progressReporter.ReportProgress(
                        "Fetching Genomes", 
                        $"Fetching genome data for {displaySpecies}", 
                        processedSpecies, 
                        species.Count,
                        new { species = displaySpecies, standardized = standardizedSpecies, step = "lookup" });
                    
                    var ncbiQueryName = string.IsNullOrWhiteSpace(lookupSpecies) ? originalSpecies : lookupSpecies;
                    _log.LogInformation("Attempting to fetch genome URLs for species: {Species} (lookup: {Lookup}, standardized: {Standardized})", originalSpecies, ncbiQueryName, standardizedSpecies);
                    
                    var genomeUrls = await _ncbi.GetGenomeDownloadUrlAsync(ncbiQueryName, ncbiApiKey, ct);
                    
                    if (genomeUrls is null)
                    {
                        _log.LogWarning("GetGenomeDownloadUrlAsync returned null for species: {Species}", ncbiQueryName);
                        genomeIssues.Add(new GenomeLookupFailure(
                            originalSpecies,
                            "NCBI Assembly lookup returned no results. The species name may not match any records in NCBI's database.",
                            "Verify the species scientific name is correct (e.g., 'Homo sapiens' not 'Human'). Check NCBI Taxonomy or Genome databases for exact spelling. Providing an NCBI API key may help with rate-limited searches."));
                        continue;
                    }
                    
                    var urls = genomeUrls.Value;
                    
                    if (string.IsNullOrWhiteSpace(urls.ProteinFaaUrl))
                    {
                        _log.LogWarning("Protein FASTA URL is empty for species: {Species}. CDS URL status: {CdsStatus}", 
                            originalSpecies, 
                            string.IsNullOrWhiteSpace(urls.CdsFnaUrl) ? "also empty" : "available");
                        
                        genomeIssues.Add(new GenomeLookupFailure(
                            originalSpecies,
                            "NCBI Assembly database has a record for this species, but no protein FASTA file (translated_cds.faa.gz or protein.faa.gz) is available for download. This may occur if: (1) the genome assembly lacks protein annotations, (2) the assembly is incomplete or draft quality, (3) only raw sequence data exists without gene predictions.",
                            "Try searching NCBI Genome for this species to verify annotation availability. Consider using a different strain or assembly if available. Some species may only have nucleotide data without protein predictions."));
                        continue;
                    }

                    var cdsUrl = urls.CdsFnaUrl;
                    if (string.IsNullOrWhiteSpace(cdsUrl))
                    {
                        _log.LogInformation("CDS URL not found in initial lookup for {Species}, attempting fallback method", originalSpecies);
                        cdsUrl = await _ncbi.GetGenomeCdsDownloadUrlAsync(originalSpecies, ncbiApiKey, ct);
                    }

                    if (string.IsNullOrWhiteSpace(cdsUrl))
                    {
                        _log.LogWarning("CDS FASTA URL is empty for species: {Species} after both primary and fallback attempts", originalSpecies);
                        
                        genomeIssues.Add(new GenomeLookupFailure(
                            originalSpecies,
                            "NCBI Assembly database has a protein FASTA but no CDS nucleotide FASTA file (cds_from_genomic.fna.gz) is available. This is required for codon-level alignments and selection pressure analysis. The assembly may lack CDS feature annotations or may only provide protein sequences.",
                            "Verify the genome assembly has CDS annotations in NCBI. Some assemblies only provide predicted proteins without the underlying CDS sequences. Consider using a reference genome or more complete assembly if available."));
                        continue;
                    }
                    
                    _log.LogInformation("Successfully retrieved URLs for {Species}: Protein={ProteinUrl}, CDS={CdsUrl}", 
                        originalSpecies, 
                        urls.ProteinFaaUrl.Length > 50 ? urls.ProteinFaaUrl.Substring(0, 50) + "..." : urls.ProteinFaaUrl,
                        cdsUrl.Length > 50 ? cdsUrl.Substring(0, 50) + "..." : cdsUrl);

                    progressReporter.ReportProgress(
                        "Uploading Files", 
                        $"Uploading genome files for {displaySpecies}", 
                        processedSpecies, 
                        species.Count,
                        new { species = displaySpecies, standardized = standardizedSpecies, step = "upload" });
                    
                    _log.LogInformation("Uploading genome files to Galaxy for {Species} (standardized: {Standardized})", originalSpecies, standardizedSpecies);
                    
                    // Upload the zip files from NCBI
                    var proteinZipDatasetId = await _galaxy.UploadFromUrlAsync(historyId, urls.ProteinFaaUrl, $"{standardizedSpecies}_proteome.zip", "zip", ct);
                    _log.LogDebug("Protein zip uploaded: {DatasetId}", proteinZipDatasetId);
                    
                    var cdsZipDatasetId = await _galaxy.UploadFromUrlAsync(historyId, cdsUrl, $"{standardizedSpecies}_cds.zip", "zip", ct);
                    _log.LogDebug("CDS zip uploaded: {DatasetId}", cdsZipDatasetId);
                    uploadedZips.Add(new UploadedZip(standardizedSpecies, proteinZipDatasetId, cdsZipDatasetId));
                    
                    progressReporter.ReportProgress(
                        "Starting Unzip", 
                        $"Starting unzip jobs for {displaySpecies}", 
                        processedSpecies, 
                        species.Count,
                        new { species = displaySpecies, standardized = standardizedSpecies, step = "unzip_start" });
                    
                    // Start unzip jobs concurrently (don't await them yet)
                    _log.LogInformation("Starting unzip jobs for {Species} (will complete concurrently with other species)", standardizedSpecies);
                    var proteinUnzipTask = StartUnzipJobAsync(historyId, proteinZipDatasetId, "ncbi_dataset/data/*/protein.faa", ct);
                    var cdsUnzipTask = StartUnzipJobAsync(historyId, cdsZipDatasetId, "ncbi_dataset/data/*/cds_from_genomic.fna", ct);
                    
                    unzipJobs.Add((standardizedSpecies, urls.ProteinFaaUrl, cdsUrl, proteinUnzipTask, cdsUnzipTask));
                    
                    genomes.Add(new GenomeInfo(standardizedSpecies, urls.ProteinFaaUrl, proteinZipDatasetId, cdsUrl, cdsZipDatasetId)
                    {
                        OriginalSpecies = originalSpecies,
                        StandardizedSpecies = standardizedSpecies
                    });

                    processedSpecies++;
                    
                    progressReporter.ReportProgress(
                        "Genome Uploaded", 
                        $"Successfully uploaded files for {displaySpecies}", 
                        processedSpecies, 
                        species.Count,
                        new { species = displaySpecies, standardized = standardizedSpecies, step = "uploaded" });
                    
                    _log.LogInformation("Successfully processed genome data for {Species} (standardized: {Standardized})", originalSpecies, standardizedSpecies);
                }
                catch (HttpRequestException httpEx)
                {
                    _log.LogError(httpEx, "HTTP error while fetching genome data for {Species}", originalSpecies);
                    
                    var detailedMessage = httpEx.StatusCode.HasValue 
                        ? $"HTTP {(int)httpEx.StatusCode.Value} error while contacting NCBI: {httpEx.Message}. Status: {httpEx.StatusCode.Value}"
                        : $"HTTP error while contacting NCBI: {httpEx.Message}";
                    
                    var suggestion = httpEx.StatusCode?.ToString() switch
                    {
                        "TooManyRequests" or "429" => "NCBI rate limit exceeded. Wait a few minutes and retry, or provide an NCBI API key to increase your rate limit from 3 to 10 requests per second.",
                        "NotFound" or "404" => "The genome resource was not found at NCBI. The assembly may have been removed, updated, or relocated. Try searching NCBI Genome database directly for this species.",
                        "ServiceUnavailable" or "503" => "NCBI services are temporarily unavailable. Please retry in a few minutes.",
                        "Forbidden" or "403" => "Access to the NCBI resource was denied. This may indicate an API key issue or access restrictions.",
                        _ => "Network or NCBI connectivity issue. Verify your internet connection and that NCBI services are accessible. Retry the operation or check NCBI service status."
                    };
                    
                    genomeIssues.Add(new GenomeLookupFailure(originalSpecies, detailedMessage, suggestion));
                }
                catch (TaskCanceledException tcEx) when (tcEx.InnerException is TimeoutException)
                {
                    _log.LogError(tcEx, "Timeout while fetching genome data for {Species}", originalSpecies);
                    genomeIssues.Add(new GenomeLookupFailure(
                        originalSpecies,
                        $"Request to NCBI timed out after waiting for response: {tcEx.Message}",
                        "NCBI may be experiencing slow response times. Try again later, or check if the genome files are particularly large."));
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Unexpected error while processing genome for {Species}: {ExceptionType}", originalSpecies, ex.GetType().Name);
                    genomeIssues.Add(new GenomeLookupFailure(
                        originalSpecies,
                        $"Unexpected error during genome processing: {ex.GetType().Name} - {ex.Message}",
                        "This may indicate a problem with the genome file format, Galaxy upload, or network connectivity. Check the logs for detailed error information."));
                }
            }

            if (genomeIssues.Count > 0)
                throw new GenomeLookupException(genomeIssues);

            // Wait for all unzip jobs to complete concurrently
            progressReporter.ReportProgress(
                "Extracting Files", 
                $"Waiting for {unzipJobs.Count} species to finish extracting...", 
                0, 
                unzipJobs.Count);
            
            _log.LogInformation("Waiting for {Count} unzip jobs to complete...", unzipJobs.Count * 2);
            
            for (int i = 0; i < unzipJobs.Count; i++)
            {
                var (speciesName, proteinUrl, cdsUrl, proteinTask, cdsTask) = unzipJobs[i];
                
                try
                {
                    progressReporter.ReportProgress(
                        "Extracting Files", 
                        $"Extracting FASTA files for {speciesName}", 
                        i, 
                        unzipJobs.Count,
                        new { species = speciesName, step = "extracting" });
                    
                    // Wait for both unzip jobs for this species
                    var proteinJobInfo = await proteinTask;
                    var cdsJobInfo = await cdsTask;
                    
                    // Wait for the jobs to complete
                    await WaitForJobCompletionAsync(proteinJobInfo.JobId, ct);
                    await WaitForJobCompletionAsync(cdsJobInfo.JobId, ct);
                    
                    // Extract the dataset IDs from the collections
                    _log.LogInformation("Extracting FASTA files from completed unzip jobs for {Species}", speciesName);
                    var proteinDatasetId = await ExtractDatasetFromCollectionAsync(historyId, proteinJobInfo.CollectionId, proteinJobInfo.TargetFilePath, ct);
                    var cdsDatasetId = await ExtractDatasetFromCollectionAsync(historyId, cdsJobInfo.CollectionId, cdsJobInfo.TargetFilePath, ct);

                    (proteinDatasetId, cdsDatasetId) = await FilterLongestIsoformPairsAsync(
                        historyId, speciesName, proteinDatasetId, cdsDatasetId, ct);
                    
                    // Update the genome info with the extracted dataset IDs
                    genomes[i] = genomes[i] with { ProteinDatasetId = proteinDatasetId, CdsDatasetId = cdsDatasetId };

                    try
                    {
                        var sanitized = SanitizeName(speciesName);
                        await _galaxy.RenameHistoryContentAsync(
                            historyId, proteinDatasetId, $"{sanitized}_proteome_filtered", ct);
                        await _galaxy.RenameHistoryContentAsync(
                            historyId, cdsDatasetId, $"{sanitized}_cds_filtered", ct);
                    }
                    catch (Exception renameEx)
                    {
                        _log.LogWarning(renameEx, "Failed to rename extracted datasets for species {Species}", speciesName);
                    }
                    
                    progressReporter.ReportProgress(
                        "Files Extracted", 
                        $"Successfully extracted FASTA files for {speciesName}", 
                        i + 1, 
                        unzipJobs.Count,
                        new { species = speciesName, step = "extracted" });

                    _log.LogInformation("Successfully processed genome data for {Species}", speciesName);

                    // Purge original uploaded zip datasets to save space
                    try
                    {
                        var uploaded = uploadedZips.FirstOrDefault(z => z.Species == speciesName);
                        if (uploaded is not null)
                        {
                            await _galaxy.DeleteHistoryDatasetAsync(historyId, uploaded.ProteinZipId, ct);
                            await _galaxy.DeleteHistoryDatasetAsync(historyId, uploaded.CdsZipId, ct);
                            _log.LogInformation("Purged original zip datasets for {Species}: {ProtZip} and {CdsZip}", speciesName, uploaded.ProteinZipId, uploaded.CdsZipId);
                        }
                    }
                    catch (Exception purgeEx)
                    {
                        _log.LogWarning(purgeEx, "Failed to purge zip datasets for {Species}", speciesName);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error completing unzip jobs for {Species}", speciesName);
                    try
                    {
                        _log.LogWarning("Galaxy unzip failed for {Species}; falling back to local archive extraction", speciesName);
                        var sanitized = SanitizeName(speciesName);
                        var proteinDatasetId = await DownloadExtractAndUploadFastaAsync(
                            historyId, proteinUrl, "protein.faa", sanitized, ct);
                        var cdsDatasetId = await DownloadExtractAndUploadFastaAsync(
                            historyId, cdsUrl, "cds_from_genomic.fna", $"{sanitized}_cds", ct);

                        // Galaxy's upload endpoint returns before large datasets are
                        // necessarily readable. Do not start paired isoform filtering
                        // until both fallback uploads have left the queued state.
                        await WaitForDatasetReadyAsync(historyId, proteinDatasetId, ct);
                        await WaitForDatasetReadyAsync(historyId, cdsDatasetId, ct);

                        // Recovery must produce the same validated inputs as the normal
                        // Galaxy extraction path. Raw proteomes must never reach OrthoFinder.
                        (proteinDatasetId, cdsDatasetId) = await FilterLongestIsoformPairsAsync(
                            historyId, speciesName, proteinDatasetId, cdsDatasetId, ct);
                        await _galaxy.RenameHistoryContentAsync(
                            historyId, proteinDatasetId, $"{sanitized}_proteome_filtered", ct);
                        await _galaxy.RenameHistoryContentAsync(
                            historyId, cdsDatasetId, $"{sanitized}_cds_filtered", ct);

                        genomes[i] = genomes[i] with
                        {
                            ProteinDatasetId = proteinDatasetId,
                            CdsDatasetId = cdsDatasetId
                        };

                        progressReporter.ReportProgress(
                            "Files Extracted",
                            $"Recovered FASTA files for {speciesName} using local extraction",
                            i + 1,
                            unzipJobs.Count,
                            new { species = speciesName, step = "local_fallback" });
                    }
                    catch (Exception fallbackEx)
                    {
                        _log.LogError(fallbackEx, "Local extraction fallback failed for {Species}", speciesName);
                        genomeIssues.Add(new GenomeLookupFailure(
                            speciesName,
                            $"Galaxy extraction failed ({ex.Message}); local fallback also failed ({fallbackEx.Message}).",
                            "The NCBI archive could not be converted into Galaxy FASTA datasets. Check the archive contents and Galaxy upload status."));
                    }
                }
            }
            
            if (genomeIssues.Count > 0)
                throw new GenomeLookupException(genomeIssues);

            // Final sweep: remove any leftover zip datasets to avoid accidental use downstream
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in contents.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.Object &&
                                    e.TryGetProperty("extension", out var extProp) &&
                                    (extProp.GetString() ?? string.Empty).Equals("zip", StringComparison.OrdinalIgnoreCase)))
                    {
                        var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : string.Empty;
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            try
                            {
                                await _galaxy.DeleteHistoryDatasetAsync(historyId, id, ct);
                                _log.LogInformation("Purged leftover zip dataset {DatasetId} ({Name}) after extraction", id, name);
                            }
                            catch (Exception delEx)
                            {
                                _log.LogDebug(delEx, "Failed to purge zip dataset {DatasetId}", id);
                            }
                        }
                    }
                }
            }
            catch (Exception purgeSweepEx)
            {
                _log.LogWarning(purgeSweepEx, "Failed to sweep leftover zip datasets in history {HistoryId}", historyId);
            }

            // Create collections for Proteomes and CDS
            string? proteomeCollectionId = null;
            string? cdsCollectionId = null;
            List<string> proteomeIds = new();
            List<string> cdsIds = new();

            try 
            {
                proteomeIds = await FilterToReadyFastaDatasetsAsync(genomes.Select(g => g.ProteinDatasetId).ToList(), ct, "proteome", null);
                cdsIds = await FilterToReadyFastaDatasetsAsync(genomes.Select(g => g.CdsDatasetId).ToList(), ct, "cds", null);

                if (proteomeIds.Count < 3)
                    throw new InvalidOperationException($"Only {proteomeIds.Count} proteome FASTA files are ready; need at least 3.");

                var proteomeElements = genomes
                    .Where(g => proteomeIds.Contains(g.ProteinDatasetId))
                    .Select(g => (g.StandardizedSpecies, g.ProteinDatasetId))
                    .ToList();
                proteomeCollectionId = await _galaxy.CreateListCollectionAsync(historyId, "Proteomes", proteomeElements, ct);
                
                var cdsElements = genomes
                    .Where(g => cdsIds.Contains(g.CdsDatasetId))
                    .Select(g => (g.StandardizedSpecies, g.CdsDatasetId))
                    .ToList();
                cdsCollectionId = await _galaxy.CreateListCollectionAsync(historyId, "CDS", cdsElements, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to create genome collections");
            }

            // 3) Run OrthoFinder directly on the extracted protein FASTA datasets
            progressReporter.ReportProgress(
                "Running OrthoFinder", 
                "Starting OrthoFinder to identify orthologous groups", 
                unzipJobs.Count, 
                unzipJobs.Count);
            
            var proteomeDatasetIds = proteomeIds ?? new List<string>();
            if (proteomeDatasetIds.Count < genomes.Count || proteomeDatasetIds.Count == 0)
                throw new InvalidOperationException("All proteome FASTA datasets must be ready before starting OrthoFinder.");

            // 4) Run OrthoFinder on amino-acid sequences
            var orthoFinderToolId = "toolshed.g2.bx.psu.edu/repos/iuc/orthofinder_onlygroups/orthofinder_onlygroups/2.5.5+galaxy1";
            var (jobId, raw) = await RunOrthoFinderAsync(historyId, proteomeDatasetIds, orthoFinderToolId, ct);

            progressReporter.ReportProgress(
                "OrthoFinder Running",
                "OrthoFinder job submitted to Galaxy.",
                unzipJobs.Count,
                unzipJobs.Count,
                new { jobId, historyId, species = genomes.Select(g => g.Species).ToList() });

            progressReporter.ReportProgress(
                "Analysis Started", 
                "OrthoFinder analysis is running in Galaxy", 
                unzipJobs.Count, 
                unzipJobs.Count,
                new { 
                    jobId, 
                    historyId, 
                    orthoFinderJobId = jobId,
                    genomes = genomes.Select(g => new {
                        species = g.Species,
                        proteinUrl = g.ProteinUrl,
                        proteinDatasetId = g.ProteinDatasetId,
                        cdsUrl = g.CdsUrl,
                        cdsDatasetId = g.CdsDatasetId
                    }).ToList()
                });

            var stepDatasets = new Dictionary<string, string>();
            for (int i = 0; i < genomes.Count; i++)
            {
                stepDatasets[$"genome_{genomes[i].Species}"] = genomes[i].ProteinDatasetId;
                stepDatasets[$"proteome_{genomes[i].Species}"] = genomes[i].ProteinDatasetId;
                stepDatasets[$"cds_{genomes[i].Species}"] = genomes[i].CdsDatasetId;
            }
            stepDatasets["orthofinder_job"] = jobId;

            return new StartGenomeAnalysisResult(historyId, jobId, raw, stepDatasets, genomes, null, "orthofinder_running", "OrthoFinder running", new List<HyphyOutput>(), new List<HyphyOutput>());
        }

        public async Task<StartGenomeAnalysisResult> ContinuePreparedGenomeAnalysisAsync(
            string historyId,
            string? galaxyApiKey,
            CancellationToken ct,
            IProgressReporter? progressReporter = null)
        {
            progressReporter ??= NullProgressReporter.Instance;
            using var apiScope = UseApiKeyForHistory(historyId, galaxyApiKey);

            var contents = await _galaxy.ListHistoryContentsAsync(
                historyId, ct, includeHidden: true, includeDeleted: false);
            if (contents.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Galaxy did not return the prepared genome datasets.");

            var items = contents.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Hid = item.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var value) ? value : 0,
                    Type = item.TryGetProperty("history_content_type", out var type) ? type.GetString() ?? "dataset" : "dataset",
                    State = item.TryGetProperty("state", out var state) ? state.GetString() ?? "unknown" : "unknown"
                })
                .ToList();

            var zipItems = items
                .Where(item => item.Type.Equals("dataset", StringComparison.OrdinalIgnoreCase)
                    && item.State.Equals("ok", StringComparison.OrdinalIgnoreCase)
                    && (item.Name.EndsWith("_proteome.zip", StringComparison.OrdinalIgnoreCase)
                        || item.Name.EndsWith("_cds.zip", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var speciesNames = zipItems
                .Select(item => Regex.Replace(item.Name, "_(?:proteome|cds)\\.zip$", string.Empty, RegexOptions.IgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (speciesNames.Count < MinSpeciesCount)
                throw new InvalidOperationException(
                    $"Only {speciesNames.Count} prepared species archive set(s) were found; need at least {MinSpeciesCount}.");

            var genomes = new List<GenomeInfo>();
            for (var index = 0; index < speciesNames.Count; index++)
            {
                var species = speciesNames[index];
                progressReporter.ReportProgress(
                    "Filtering Isoforms",
                    $"Recovering and filtering prepared FASTA files for {NormalizeRecoveredSpeciesName(species)}",
                    index,
                    speciesNames.Count,
                    new { historyId, species });

                var proteinZip = zipItems.FirstOrDefault(item => item.Name.Equals($"{species}_proteome.zip", StringComparison.OrdinalIgnoreCase));
                var cdsZip = zipItems.FirstOrDefault(item => item.Name.Equals($"{species}_cds.zip", StringComparison.OrdinalIgnoreCase));
                if (proteinZip is null || cdsZip is null)
                    throw new InvalidOperationException($"Prepared protein/CDS archives are incomplete for {species}.");

                var proteinCollection = items.FirstOrDefault(item =>
                    item.Type.Equals("dataset_collection", StringComparison.OrdinalIgnoreCase)
                    && item.Name.Equals($"Unzip on dataset {proteinZip.Hid} all files as collection", StringComparison.OrdinalIgnoreCase));
                var cdsCollection = items.FirstOrDefault(item =>
                    item.Type.Equals("dataset_collection", StringComparison.OrdinalIgnoreCase)
                    && item.Name.Equals($"Unzip on dataset {cdsZip.Hid} all files as collection", StringComparison.OrdinalIgnoreCase));
                if (proteinCollection is null || cdsCollection is null)
                    throw new InvalidOperationException($"Completed unzip collections could not be matched to {species}.");

                var proteinDatasetId = await ExtractDatasetFromCollectionAsync(
                    historyId, proteinCollection.Id, "ncbi_dataset/data/*/protein.faa", ct);
                var cdsDatasetId = await ExtractDatasetFromCollectionAsync(
                    historyId, cdsCollection.Id, "ncbi_dataset/data/*/cds_from_genomic.fna", ct);
                (proteinDatasetId, cdsDatasetId) = await FilterLongestIsoformPairsAsync(
                    historyId, species, proteinDatasetId, cdsDatasetId, ct);

                await _galaxy.RenameHistoryContentAsync(
                    historyId, proteinDatasetId, $"{SanitizeName(species)}_proteome_filtered", ct);
                await _galaxy.RenameHistoryContentAsync(
                    historyId, cdsDatasetId, $"{SanitizeName(species)}_cds_filtered", ct);

                genomes.Add(new GenomeInfo(species, string.Empty, proteinDatasetId, string.Empty, cdsDatasetId)
                {
                    OriginalSpecies = NormalizeRecoveredSpeciesName(species),
                    StandardizedSpecies = species
                });
            }

            var proteomeElements = genomes.Select(genome => (genome.StandardizedSpecies, genome.ProteinDatasetId)).ToList();
            var cdsElements = genomes.Select(genome => (genome.StandardizedSpecies, genome.CdsDatasetId)).ToList();
            await _galaxy.CreateListCollectionAsync(historyId, "Proteomes", proteomeElements, ct);
            await _galaxy.CreateListCollectionAsync(historyId, "CDS", cdsElements, ct);

            progressReporter.ReportProgress(
                "Running OrthoFinder",
                "Filtered genome collections are ready. Submitting OrthoFinder to Galaxy.",
                speciesNames.Count,
                speciesNames.Count,
                new { historyId });

            const string orthoFinderToolId = "toolshed.g2.bx.psu.edu/repos/iuc/orthofinder_onlygroups/orthofinder_onlygroups/2.5.5+galaxy1";
            var (jobId, raw) = await RunOrthoFinderAsync(
                historyId, genomes.Select(genome => genome.ProteinDatasetId).ToList(), orthoFinderToolId, ct);

            progressReporter.ReportProgress(
                "Analysis Started",
                "OrthoFinder analysis is running in Galaxy.",
                speciesNames.Count,
                speciesNames.Count,
                new
                {
                    historyId,
                    orthoFinderJobId = jobId,
                    genomes = genomes.Select(genome => new
                    {
                        species = genome.OriginalSpecies,
                        proteinDatasetId = genome.ProteinDatasetId,
                        cdsDatasetId = genome.CdsDatasetId
                    }).ToList()
                });

            return new StartGenomeAnalysisResult(
                historyId, jobId, raw, new Dictionary<string, string>(), genomes,
                null, "orthofinder_running", "OrthoFinder running", new List<HyphyOutput>(), new List<HyphyOutput>());
        }

        private async Task<string> DownloadExtractAndUploadFastaAsync(
            string historyId,
            string archiveUrl,
            string targetFileName,
            string datasetName,
            CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await http.GetAsync(archiveUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var archiveStream = await response.Content.ReadAsStreamAsync(ct);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
            var entry = archive.Entries.FirstOrDefault(item =>
                item.FullName.EndsWith($"/{targetFileName}", StringComparison.OrdinalIgnoreCase) ||
                item.FullName.Equals(targetFileName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new InvalidOperationException($"NCBI archive did not contain {targetFileName}.");

            await using var entryStream = entry.Open();
            await using var fastaBuffer = new MemoryStream();
            await entryStream.CopyToAsync(fastaBuffer, ct);
            if (fastaBuffer.Length == 0)
                throw new InvalidOperationException($"NCBI archive contained an empty {targetFileName}.");

            return await _galaxy.UploadBinaryContentAsync(
                historyId,
                fastaBuffer.ToArray(),
                datasetName,
                "fasta",
                ct);
        }

        /// <summary>
        /// Resume an existing genome analysis by loading metadata stored in the Galaxy history.
        /// </summary>
        public async Task<StartGenomeAnalysisResult> ResumeGenomeAnalysisAsync(
            string historyId,
            string? orthoFinderJobId,
            string? galaxyApiKey,
            CancellationToken ct,
            IProgressReporter? progressReporter = null)
        {
            progressReporter ??= NullProgressReporter.Instance;
            using var apiScope = UseApiKeyForHistory(historyId, galaxyApiKey);
            using var historyReadCache = _galaxy.UseHistoryReadCache();

            // PrimeHistoryContentsCacheAsync validates both the history identifier and
            // credentials. Avoid the separate history-details request here: Galaxy's
            // large-history response is occasionally truncated even while the paginated
            // contents endpoint remains healthy, which previously made resume fail before
            // the recoverable workflow state could be reconstructed.
            await _galaxy.PrimeHistoryContentsCacheAsync(historyId, ct);

            progressReporter.ReportProgress("Loading Metadata", $"Reading existing analysis metadata from history {historyId}", 0, null);

            var metadata = await LoadMetadataAsync(historyId, ct);
            var resolvedJobId = string.IsNullOrWhiteSpace(orthoFinderJobId) ? metadata.OrthoFinderJobId : orthoFinderJobId!;
            if (metadata.Genomes.Count == 0)
            {
                var rehydrated = await TryRehydrateGenomesFromCollectionsAsync(historyId, ct);
                if (rehydrated is not null && rehydrated.Genomes.Count > 0)
                {
                    metadata = metadata with
                    {
                        Genomes = rehydrated.Genomes,
                        ProteomeCollectionId = rehydrated.ProteomeCollectionId ?? metadata.ProteomeCollectionId,
                        CdsCollectionId = rehydrated.CdsCollectionId ?? metadata.CdsCollectionId,
                        Status = string.IsNullOrWhiteSpace(metadata.Status) ? "uploading" : metadata.Status,
                        StatusDetail = string.IsNullOrWhiteSpace(metadata.StatusDetail) ? "Recovered FASTA collections from Galaxy." : metadata.StatusDetail
                    };
                }
            }

            string? existingHierarchical = null;
            string? existingOrthogroups = null;
            try
            {
                existingHierarchical = await FindHierarchicalOrthoGroupsDatasetInHistoryAsync(historyId, ct);
                existingOrthogroups = await FindOrthoGroupsDatasetInHistoryAsync(historyId, ct);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Failed to detect existing OrthoFinder outputs in history {HistoryId}", historyId);
            }
            var hasOrthoFinderOutputs = !string.IsNullOrWhiteSpace(existingHierarchical)
                || !string.IsNullOrWhiteSpace(existingOrthogroups);
            if (string.IsNullOrWhiteSpace(resolvedJobId) && hasOrthoFinderOutputs)
            {
                // Provide a stable sentinel so the UI can request outputs via history scan
                resolvedJobId = "history-scan";
            }

            // Empty JSON element for responses
            var emptyJson = JsonDocument.Parse("{}").RootElement.Clone();

            // Check if metadata is empty (history still being set up)
            string? filteredIdEarly = null;
            try { filteredIdEarly = await FindFilteredSingleCopyDatasetInHistoryAsync(historyId, ct); } catch { }
            var hasOutputs = hasOrthoFinderOutputs || !string.IsNullOrWhiteSpace(filteredIdEarly);

            // Large all-orthogroup workflows can contain tens of thousands of child
            // datasets. Attaching must not block on enumerating HyPhy artifacts or
            // expanding those result collections. A workflow marker is sufficient to
            // reconnect the UI; the read-only status and result endpoints hydrate the
            // detailed state incrementally after the attach response is returned.
            string? quickWorkflowInvocationId = null;
            try
            {
                quickWorkflowInvocationId = await GetWorkflowInvocationIdFromMarkerAsync(historyId, ct);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Unable to read the latest workflow marker during fast attach for history {HistoryId}", historyId);
            }

            if (!string.IsNullOrWhiteSpace(quickWorkflowInvocationId))
            {
                HistoryJobCounts? quickCounts = null;
                try { quickCounts = await GetLatestWorkflowJobCountsAsync(historyId, ct); }
                catch (Exception ex) { _log.LogDebug(ex, "Unable to read compact workflow counts during fast attach for history {HistoryId}", historyId); }

                var running = quickCounts?.Running ?? 0;
                var queued = quickCounts?.Queued ?? 0;
                var failed = quickCounts?.Failed ?? 0;
                var quickStatus = failed > 0 && running == 0 && queued == 0
                    ? "workflow_failed"
                    : "workflow_running";
                var quickDetail = quickStatus == "workflow_failed"
                    ? $"Connected to Galaxy. The latest workflow generation has {failed} failed item(s); loading diagnostics."
                    : running > 0 || queued > 0
                        ? $"Connected to Galaxy. Current workflow activity: {running} running, {queued} queued."
                        : "Connected to Galaxy. Verifying the latest workflow outputs.";

                progressReporter.ReportProgress(
                    "History Connected",
                    quickDetail,
                    quickStatus == "workflow_failed" ? 100 : 85,
                    null,
                    new { historyId, workflowInvocationId = quickWorkflowInvocationId, status = quickStatus });

                return new StartGenomeAnalysisResult(
                    historyId,
                    resolvedJobId ?? string.Empty,
                    emptyJson,
                    new Dictionary<string, string>(),
                    metadata.Genomes,
                    quickWorkflowInvocationId,
                    quickStatus,
                    quickDetail,
                    new List<HyphyOutput>(),
                    new List<HyphyOutput>());
            }

            if (metadata.Genomes.Count == 0 && string.IsNullOrWhiteSpace(resolvedJobId) && !hasOutputs)
            {
                progressReporter.ReportProgress(
                    "Initializing", 
                    "Galaxy history is being set up. Genomes are being downloaded and uploaded.", 
                    10, 
                    null,
                    new { historyId, status = "initializing" });
                
                // Return partial result - frontend should poll again
                return new StartGenomeAnalysisResult(historyId, string.Empty, emptyJson, new Dictionary<string, string>(), new List<GenomeInfo>(), metadata.WorkflowInvocationId, metadata.Status, metadata.StatusDetail, metadata.HyphyMarkdownOutputs, metadata.HyphyJsonOutputs);
            }

            // Attempt to auto-start OrthoFinder if genomes are present but no job id yet
            if (metadata.Genomes.Count > 0 && string.IsNullOrWhiteSpace(resolvedJobId))
            {
                var maybeJob = await EnsureOrthoFinderStartedAsync(historyId, metadata, ct);
                if (!string.IsNullOrWhiteSpace(maybeJob))
                {
                    resolvedJobId = maybeJob!;
                    metadata = metadata with { OrthoFinderJobId = resolvedJobId };
                    progressReporter.ReportProgress(
                        "OrthoFinder Resumed", 
                        "OrthoFinder started automatically after reconnect.", 
                        60, 
                        null,
                        new 
                        { 
                            historyId, 
                            status = "orthofinder_running",
                            genomes = metadata.Genomes.Select(g => new
                            {
                            species = g.Species,
                            proteinDatasetId = g.ProteinDatasetId,
                            cdsDatasetId = g.CdsDatasetId
                        })
                    });
                }
                else if (string.IsNullOrWhiteSpace(existingHierarchical))
                {
                    progressReporter.ReportProgress(
                        "Uploading Genomes", 
                    $"Processing {metadata.Genomes.Count} genome(s). Waiting for OrthoFinder to start.", 
                    50, 
                    null,
                    new 
                    { 
                        historyId, 
                        status = "uploading",
                        genomes = metadata.Genomes.Select(g => new
                        {
                            species = g.Species,
                            proteinDatasetId = g.ProteinDatasetId,
                            cdsDatasetId = g.CdsDatasetId
                        })
                    });
                
                    // Return partial result - frontend should poll again
                    return new StartGenomeAnalysisResult(historyId, string.Empty, emptyJson, new Dictionary<string, string>(), metadata.Genomes, metadata.WorkflowInvocationId, metadata.Status, metadata.StatusDetail, metadata.HyphyMarkdownOutputs, metadata.HyphyJsonOutputs);
                }
                else
                {
                    progressReporter.ReportProgress(
                        "OrthoFinder Detected",
                        "Existing OrthoFinder outputs found; continuing with filtering.",
                        60,
                        null,
                        new
                        {
                            historyId,
                            status = "orthofinder_complete",
                            genomes = metadata.Genomes.Select(g => new
                            {
                                species = g.Species,
                                proteinDatasetId = g.ProteinDatasetId,
                                cdsDatasetId = g.CdsDatasetId
                            })
                        });
                }
            }

            string? jobState = null;
            var isLikelyJobId = !string.IsNullOrWhiteSpace(resolvedJobId) && resolvedJobId.Length >= 16;
            if (isLikelyJobId)
            {
                try
                {
                    var job = await _galaxy.GetJobAsync(resolvedJobId!, ct);
                    jobState = job.TryGetProperty("state", out var s) ? s.GetString() : "unknown";
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Unable to read OrthoFinder job state for history {HistoryId}", historyId);
                }
            }
            else if (!string.IsNullOrWhiteSpace(resolvedJobId))
            {
                _log.LogDebug("Skipping OrthoFinder job lookup for history {HistoryId} because job id {JobId} is not a valid Galaxy job id.", historyId, resolvedJobId);
            }
            if (string.IsNullOrWhiteSpace(jobState) && hasOrthoFinderOutputs)
            {
                jobState = "ok";
            }

            var proteomeIds = await FilterToReadyFastaDatasetsAsync(
                metadata.Genomes.Select(g => g.ProteinDatasetId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
                ct,
                "proteome",
                null);
            bool proteomeReady = metadata.Genomes.Count > 0 && proteomeIds.Count == metadata.Genomes.Count;

            var filteredId = await FindFilteredSingleCopyDatasetInHistoryAsync(historyId, ct);
            if (string.IsNullOrWhiteSpace(filteredId))
                filteredId = filteredIdEarly;
            var workflowInvoked = await HasOrthogroupWorkflowMarkerAsync(historyId, ct);
            var workflowInvocationId = metadata.WorkflowInvocationId;
            if (string.IsNullOrWhiteSpace(workflowInvocationId))
            {
                try { workflowInvocationId = await GetWorkflowInvocationIdFromMarkerAsync(historyId, ct); }
                catch (Exception ex) { _log.LogDebug(ex, "Failed to parse workflow invocation id from marker in history {HistoryId}", historyId); }
            }
            if (!string.IsNullOrWhiteSpace(workflowInvocationId))
                workflowInvoked = true;
            HistoryJobCounts? counts = null;
            List<HyphyOutput> hyphyMarkdown = metadata.HyphyMarkdownOutputs ?? new List<HyphyOutput>();
            List<HyphyOutput> hyphyJson = metadata.HyphyJsonOutputs ?? new List<HyphyOutput>();

            // If OrthoFinder outputs are present but the filtered table is missing, kick off the filter job automatically
            if (string.IsNullOrWhiteSpace(filteredId) && !string.IsNullOrWhiteSpace(existingHierarchical))
            {
                try
                {
                    // Ensure the N0 dataset is ready before reading headers or invoking filter
                    await WaitForDatasetReadyAsync(existingHierarchical, ct);

                    var speciesCount = metadata.Genomes?.Count ?? 0;
                    if (speciesCount <= 0)
                    {
                        var header = await ReadFirstLineAsync(historyId, existingHierarchical, ct);
                        speciesCount = DeriveSpeciesCountFromHeader(header);
                    }
                    if (speciesCount <= 0)
                    {
                        _log.LogWarning("Unable to derive species count for filtering N0 dataset {DatasetId} in history {HistoryId}; skipping filter invocation.", existingHierarchical, historyId);
                    }
                    else
                    {
                        var result = await EnsureFilteredSingleCopyDatasetAsync(historyId, existingHierarchical, speciesCount, ct);
                        filteredId = result.DatasetId;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to start filtering N0 orthogroups for history {HistoryId}", historyId);
                }
            }

            if (workflowInvoked)
            {
                try { counts = await GetLatestWorkflowJobCountsAsync(historyId, ct); } catch { }
                workflowInvocationId ??= "history-scan-workflow";
                try
                {
                    var outputs = await FindHyphyOutputsInHistoryAsync(historyId, ct);
                    if (outputs.Markdown?.Count > 0) hyphyMarkdown = outputs.Markdown;
                    if (outputs.Json?.Count > 0) hyphyJson = outputs.Json;
                    if ((hyphyMarkdown.Count == 0 && hyphyJson.Count == 0) && !string.IsNullOrWhiteSpace(workflowInvocationId))
                    {
                        var invOutputs = await FindHyphyOutputsFromInvocationAsync(workflowInvocationId, ct);
                        if (invOutputs.Markdown?.Count > 0) hyphyMarkdown = invOutputs.Markdown;
                        if (invOutputs.Json?.Count > 0) hyphyJson = invOutputs.Json;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Failed to locate HYPHY outputs in history {HistoryId}", historyId);
                }
            }
            else
            {
                try
                {
                    var outputs = await FindHyphyOutputsInHistoryAsync(historyId, ct);
                    if ((outputs.Markdown?.Count ?? 0) > 0 || (outputs.Json?.Count ?? 0) > 0)
                    {
                        hyphyMarkdown = outputs.Markdown;
                        hyphyJson = outputs.Json;
                        workflowInvoked = true; // Evidence that the workflow already ran
                        workflowInvocationId ??= "history-scan-workflow";
                    }
                    else if (!string.IsNullOrWhiteSpace(workflowInvocationId))
                    {
                        var invOutputs = await FindHyphyOutputsFromInvocationAsync(workflowInvocationId, ct);
                        if ((invOutputs.Markdown?.Count ?? 0) > 0 || (invOutputs.Json?.Count ?? 0) > 0)
                        {
                            hyphyMarkdown = invOutputs.Markdown;
                            hyphyJson = invOutputs.Json;
                            workflowInvoked = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "HYPHY output scan failed for history {HistoryId}", historyId);
                }
            }

            // Final attempt to load HyPhy outputs even if the workflow marker is missing
            if ((hyphyMarkdown.Count == 0 && hyphyJson.Count == 0))
            {
                try
                {
                    var outputs = await FindHyphyOutputsInHistoryAsync(historyId, ct);
                    if ((outputs.Markdown?.Count ?? 0) > 0 || (outputs.Json?.Count ?? 0) > 0)
                    {
                        hyphyMarkdown = outputs.Markdown;
                        hyphyJson = outputs.Json;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Final HyPhy scan failed for history {HistoryId}", historyId);
                }
            }

            string status = "initializing";
            string statusDetail = "Preparing analysis.";
            int statusProgress = 10;

            var historyContents = await _galaxy.ListHistoryContentsAsync(
                historyId, ct, includeHidden: true, includeDeleted: false);
            var completedCodonCollectionId = FindLatestHistoryCollectionId(historyContents, name =>
                !IsGeneratedHyphyRecoveryCollection(name) &&
                (name.Contains("codon_alignments", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("codon alignment", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("tranalign", StringComparison.OrdinalIgnoreCase)));
            var completedTreeCollectionId = FindLatestHistoryCollectionId(historyContents, name =>
                !IsGeneratedHyphyRecoveryCollection(name) &&
                (name.Equals("gene_trees", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("gene trees", StringComparison.OrdinalIgnoreCase) ||
                 (name.Contains("IQ-TREE", StringComparison.OrdinalIgnoreCase) &&
                  !name.Contains("report", StringComparison.OrdinalIgnoreCase))));
            var hyphyInputsReady = !string.IsNullOrWhiteSpace(completedCodonCollectionId) &&
                                   !string.IsNullOrWhiteSpace(completedTreeCollectionId);
            var hyphyRetryStarted = historyContents.ValueKind == JsonValueKind.Array &&
                historyContents.EnumerateArray().Any(item =>
                    item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("name", out var name) &&
                    (name.GetString() ?? string.Empty).Contains(
                        "orthogroup_workflow_invoked_hyphy_retry", StringComparison.OrdinalIgnoreCase));

            if (workflowInvoked)
            {
                var running = counts?.Running ?? 0;
                var queued = counts?.Queued ?? 0;
                var failed = counts?.Failed ?? 0;
                if (failed > 0 && running == 0 && queued == 0)
                {
                    status = "workflow_failed";
                    statusDetail = $"Galaxy workflow failed. Failed jobs: {failed}.";
                    statusProgress = 100;
                }
                else if (running > 0 || queued > 0)
                {
                    status = "workflow_running";
                    statusDetail = failed > 0
                        ? $"Galaxy workflow running. Jobs: running {running}, queued {queued}, failed {failed}."
                        : $"Galaxy workflow running. Jobs: running {running}, queued {queued}.";
                    statusProgress = 90;
                }
                else if (hyphyJson.Count > 0 || hyphyMarkdown.Count > 0)
                {
                    status = "workflow_complete";
                    statusDetail = "Galaxy workflow completed.";
                    statusProgress = 100;
                }
                else if (hyphyRetryStarted)
                {
                    status = "workflow_preparing";
                    statusDetail = "Galaxy accepted the HyPhy workflow and is preparing MEME, BUSTED and aBSREL jobs.";
                    statusProgress = 90;
                }
                else if (hyphyInputsReady)
                {
                    status = "hyphy_ready";
                    statusDetail = "Codon alignments and IQ-TREE gene trees are complete. HyPhy inputs are ready for validation.";
                    statusProgress = 85;
                }
                else
                {
                    status = "workflow_running";
                    statusDetail = "Galaxy completed the visible jobs, but expected downstream outputs are not yet available.";
                    statusProgress = 85;
                }
            }
            else if (!string.IsNullOrWhiteSpace(filteredId))
            {
                status = "orthogroups_ready";
                statusDetail = "Filtered orthogroup tables available.";
                statusProgress = 80;
            }
            else if (hasOrthoFinderOutputs || string.Equals(jobState, "ok", StringComparison.OrdinalIgnoreCase))
            {
                status = "orthofinder_complete";
                statusDetail = "OrthoFinder completed. Select orthogroups to analyze.";
                statusProgress = 70;
            }
            else if (!string.IsNullOrWhiteSpace(jobState) && jobState != "error" && jobState != "failed")
            {
                status = "orthofinder_running";
                statusDetail = $"OrthoFinder state: {jobState}";
                statusProgress = 60;
            }
            else if (proteomeReady)
            {
                status = "unzipped";
                statusDetail = "Genomes uploaded and extracted.";
                statusProgress = 50;
            }
            else if (metadata.Genomes.Count > 0)
            {
                status = "uploading";
                statusDetail = "Uploading genomes to Galaxy.";
                statusProgress = 30;
            }

            progressReporter.ReportProgress("Metadata Loaded", "Reusing existing Galaxy history", statusProgress, null, new
            {
                historyId,
                orthoFinderJobId = isLikelyJobId ? resolvedJobId : string.Empty,
                workflowInvocationId,
                genomes = metadata.Genomes.Select(g => new
                {
                    species = g.Species,
                    proteinDatasetId = g.ProteinDatasetId,
                    cdsDatasetId = g.CdsDatasetId
                }),
                status,
                statusDetail
            });

            return new StartGenomeAnalysisResult(
                historyId,
                resolvedJobId,
                emptyJson,
                new Dictionary<string, string>(),
                metadata.Genomes,
                workflowInvocationId,
                status,
                statusDetail,
                hyphyMarkdown,
                hyphyJson);
        }

        private Task PersistMetadataAsync(
            string historyId, 
            string? orthoFinderJobId, 
            List<GenomeInfo> genomes, 
            string status, 
            string? statusDetail, 
            string? proteomeCollectionId,
            string? cdsCollectionId,
            string? speciesMappingId,
            string? workflowInvocationId,
            List<HyphyOutput>? hyphyMarkdownOutputs,
            List<HyphyOutput>? hyphyJsonOutputs,
            CancellationToken ct)
        {
            // Metadata persistence removed; rely on Galaxy state instead.
            return Task.CompletedTask;
        }

        private async Task<GenomeAnalysisMetadata> LoadMetadataAsync(string historyId, CancellationToken ct)
        {
            var rehydrated = await TryRehydrateGenomesFromCollectionsAsync(historyId, ct);
            if (rehydrated is null)
            {
                return new GenomeAnalysisMetadata(historyId, null, new List<GenomeInfo>(), "initializing");
            }

            var genomes = rehydrated.Genomes ?? new List<GenomeInfo>();
            var status = genomes.Count > 0 ? "uploading" : "initializing";

            return new GenomeAnalysisMetadata(
                historyId,
                null,
                genomes,
                status,
                status == "uploading" ? "Recovered FASTA collections from Galaxy." : "Preparing analysis.",
                rehydrated.ProteomeCollectionId,
                rehydrated.CdsCollectionId,
                null,
                null,
                new List<HyphyOutput>(),
                new List<HyphyOutput>()
            );
        }

        /// <summary>
        /// Public helper to recover genomes from existing Proteomes/CDS collections in a history.
        /// </summary>
        public async Task<List<GenomeInfo>> GetGenomesFromHistoryAsync(string historyId, CancellationToken ct)
        {
            var rehydrated = await TryRehydrateGenomesFromCollectionsAsync(historyId, ct);
            return rehydrated?.Genomes ?? new List<GenomeInfo>();
        }

        /// <summary>
        /// If genomes are present in metadata but no OrthoFinder job has been recorded, attempt to (re)start it.
        /// This supports reconnect/resume scenarios after the client disconnects mid-upload.
        /// </summary>
        private async Task<string?> EnsureOrthoFinderStartedAsync(string historyId, GenomeAnalysisMetadata meta, CancellationToken ct)
        {
            if (meta is null)
                return null;

            if (!string.IsNullOrWhiteSpace(meta.OrthoFinderJobId))
                return meta.OrthoFinderJobId;

            var genomes = meta.Genomes ?? new List<GenomeInfo>();
            if (genomes.Count < 3)
                return null;

            if (genomes.Any(g => string.IsNullOrWhiteSpace(g?.ProteinDatasetId)))
                return null; // still uploading/unzipping

            // If hierarchical orthogroups already exist in the history, assume OrthoFinder completed; do not rerun.
            try
            {
                var existingHierarchical = await FindHierarchicalOrthoGroupsDatasetInHistoryAsync(historyId, ct);
                if (!string.IsNullOrWhiteSpace(existingHierarchical))
                {
                    _log.LogInformation("Detected existing hierarchical orthogroups dataset {DatasetId} in history {HistoryId}; skipping OrthoFinder rerun.", existingHierarchical, historyId);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Failed to check for existing hierarchical orthogroups in history {HistoryId}", historyId);
            }

            // Create proteome/CDS collections if missing (helpful for downstream steps)
            var proteomeCollectionId = meta.ProteomeCollectionId;
            var cdsCollectionId = meta.CdsCollectionId;
            try
            {
                if (string.IsNullOrWhiteSpace(proteomeCollectionId))
                {
                    var proteomeElements = genomes
                        .Where(g => !string.IsNullOrWhiteSpace(g.ProteinDatasetId))
                        .Select(g => (SanitizeName(string.IsNullOrWhiteSpace(g.StandardizedSpecies) ? g.Species : g.StandardizedSpecies), g.ProteinDatasetId))
                        .ToList();
                    if (proteomeElements.Count > 0)
                        proteomeCollectionId = await _galaxy.CreateListCollectionAsync(historyId, "Proteomes", proteomeElements, ct);
                }

                if (string.IsNullOrWhiteSpace(cdsCollectionId))
                {
                    var cdsElements = genomes
                        .Where(g => !string.IsNullOrWhiteSpace(g.CdsDatasetId))
                        .Select(g => (SanitizeName(string.IsNullOrWhiteSpace(g.StandardizedSpecies) ? g.Species : g.StandardizedSpecies), g.CdsDatasetId))
                        .ToList();
                    if (cdsElements.Count > 0)
                        cdsCollectionId = await _galaxy.CreateListCollectionAsync(historyId, "CDS", cdsElements, ct);
                }
            }
            catch (Exception ex)
                {
                    _log.LogWarning(ex, "Unable to create proteome/CDS collections while resuming history {HistoryId}", historyId);
                }

            // Update the in-memory metadata copy with collection ids so downstream logic can use them
            if (!string.Equals(meta.ProteomeCollectionId, proteomeCollectionId, StringComparison.Ordinal) ||
                !string.Equals(meta.CdsCollectionId, cdsCollectionId, StringComparison.Ordinal))
            {
                meta = meta with { ProteomeCollectionId = proteomeCollectionId, CdsCollectionId = cdsCollectionId };
            }

            try
            {
                var proteomeIds = await FilterToReadyFastaDatasetsAsync(
                    genomes.Select(g => g.ProteinDatasetId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
                    ct,
                    "proteome",
                    null);
                if (proteomeIds.Count < genomes.Count || proteomeIds.Count == 0)
                    return null;

                // Ensure species mapping exists before starting OrthoFinder
                await EnsureSpeciesMappingAsync(historyId, genomes, ct);

                var orthoFinderToolId = "toolshed.g2.bx.psu.edu/repos/iuc/orthofinder_onlygroups/orthofinder_onlygroups/2.5.5+galaxy1";
                var (jobId, raw) = await RunOrthoFinderAsync(historyId, proteomeIds, orthoFinderToolId, ct);

                return jobId;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to auto-start OrthoFinder while resuming history {HistoryId}", historyId);
                return null;
            }
        }

        /// <summary>
        /// Unzip a dataset using the Galaxy unzip tool to extract a specific file
        /// </summary>
        /// <summary>
        /// Starts an unzip job in Galaxy without waiting for it to complete.
        /// Returns job info that can be used to wait for completion later.
        /// </summary>
        private async Task<UnzipJobInfo> StartUnzipJobAsync(string historyId, string zipDatasetId, string targetFilePath, CancellationToken ct)
        {
            var unzipToolId = _opts.UnzipToolId ?? "toolshed.g2.bx.psu.edu/repos/imgteam/unzip/unzip/6.0+galaxy2";
            
            _log.LogDebug("Starting unzip job for dataset {DatasetId} to extract file: {FilePath}", zipDatasetId, targetFilePath);
            
            var build = await _galaxy.GetToolBuildAsync(unzipToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, unzipToolId);
            
            // Find the input parameter name (typically "input" or "input_file")
            var inputParam = FindSingleDataParamRecursive(inputsNode) ?? "input";
            
            var inputs = new Dictionary<string, object>
            {
                [inputParam] = new { src = "hda", id = zipDatasetId },
                ["extract_single_file"] = new 
                {
                    single_file_selector = "single",  // "Single file" mode
                    filepath = targetFilePath          // Path to the specific file to extract
                }
            };
            
            var (_, raw) = await _galaxy.InvokeToolAsync(historyId, unzipToolId, inputs, ct);
            
            // Extract job ID and collection ID from the response
            string? jobId = null;
            string? collectionId = null;
            
            if (raw.TryGetProperty("jobs", out var jobs) && 
                jobs.ValueKind == JsonValueKind.Array && 
                jobs.GetArrayLength() > 0)
            {
                var firstJob = jobs[0];
                if (firstJob.TryGetProperty("id", out var jid))
                {
                    jobId = jid.GetString();
                }
            }
            
            if (raw.TryGetProperty("output_collections", out var collections) && 
                collections.ValueKind == JsonValueKind.Array && 
                collections.GetArrayLength() > 0)
            {
                collectionId = collections[0].GetProperty("id").GetString();
            }
            
            if (string.IsNullOrEmpty(jobId) || string.IsNullOrEmpty(collectionId))
            {
                throw new InvalidOperationException($"Failed to extract job ID or collection ID from unzip response for {targetFilePath}");
            }
            
            _log.LogDebug("Started unzip job {JobId} creating collection {CollectionId}", jobId, collectionId);
            return new UnzipJobInfo(jobId, collectionId, targetFilePath);
        }
        
        /// <summary>
        /// Extracts a dataset ID from a completed collection by finding the target file.
        /// </summary>
        private async Task<string> ExtractDatasetFromCollectionAsync(string historyId, string collectionId, string targetFilePath, CancellationToken ct)
        {
            _log.LogDebug("Extracting dataset from collection {CollectionId} for file {FilePath}", collectionId, targetFilePath);
            
            var collectionDetails = await _galaxy.GetCollectionDetailsAsync(historyId, collectionId, ct);
            
            // Log the entire collection structure for debugging
            _log.LogInformation("Collection structure: {Json}", collectionDetails.ToString());
            
            // Look for the protein.faa or cds_from_genomic.fna file in the collection
            var targetFileName = targetFilePath.Contains("protein.faa") ? "protein.faa" : "cds_from_genomic.fna";
            
            // Try to find the file in the collection hierarchy
            var datasetId = await FindFileInCollectionAsync(historyId, collectionDetails, targetFileName, ct);
            
            if (!string.IsNullOrEmpty(datasetId))
            {
                _log.LogInformation("Found '{TargetFile}' with dataset ID: {DatasetId}", targetFileName, datasetId);
                return datasetId;
            }
            
            // Build error message with available elements
            var elementNames = new List<string>();
            if (collectionDetails.TryGetProperty("elements", out var elements) && 
                elements.ValueKind == JsonValueKind.Array)
            {
                elementNames = elements.EnumerateArray()
                    .Where(e => e.TryGetProperty("element_identifier", out _))
                    .Select(e => e.GetProperty("element_identifier").GetString() ?? "")
                    .ToList();
            }
            
            throw new InvalidOperationException(
                $"Could not find FASTA file '{targetFileName}' in unzipped collection {collectionId}. " +
                $"Available elements: {string.Join(", ", elementNames)}");
        }
        
        /// <summary>
        /// Recursively searches for a file in a collection and its nested collections.
        /// </summary>
        private async Task<string?> FindFileInCollectionAsync(string historyId, JsonElement collection, string targetFileName, CancellationToken ct)
        {
            if (!collection.TryGetProperty("elements", out var elements) || 
                elements.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            
            // List of known metadata files to skip
            var metadataFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "README", "README.md", "md5sum", "md5sum.txt",
                "ncbi_dataset_data_assembly_data_report",
                "ncbi_dataset_data_dataset_catalog"
            };
            
            foreach (var element in elements.EnumerateArray())
            {
                if (!element.TryGetProperty("element_identifier", out var identifier))
                    continue;
                    
                var elementName = identifier.GetString() ?? "";
                
                if (!element.TryGetProperty("object", out var obj))
                    continue;
                
                // Check if this object has an element_type property to determine if it's a collection
                var isCollection = obj.TryGetProperty("element_type", out var elementType) && 
                                   elementType.GetString() == "dataset_collection";
                
                if (isCollection)
                {
                    // This is a nested collection - fetch its details and search recursively
                    _log.LogDebug("Found nested collection '{CollectionName}', fetching details...", elementName);
                    
                    if (obj.TryGetProperty("id", out var nestedCollectionId))
                    {
                        var nestedId = nestedCollectionId.GetString();
                        if (!string.IsNullOrEmpty(nestedId))
                        {
                            var nestedCollectionDetails = await _galaxy.GetCollectionDetailsAsync(historyId, nestedId, ct);
                            var foundDatasetId = await FindFileInCollectionAsync(historyId, nestedCollectionDetails, targetFileName, ct);
                            if (!string.IsNullOrEmpty(foundDatasetId))
                            {
                                _log.LogInformation("Found '{TargetFile}' in nested collection '{CollectionName}'", targetFileName, elementName);
                                return foundDatasetId;
                            }
                        }
                    }
                }
                else
                {
                    // This is a dataset - check if it matches our target file
                    
                    // First, check for exact or path matches
                    bool isMatch = elementName.Equals(targetFileName, StringComparison.OrdinalIgnoreCase) ||
                                   elementName.EndsWith($"/{targetFileName}", StringComparison.OrdinalIgnoreCase) ||
                                   elementName.EndsWith($"\\{targetFileName}", StringComparison.OrdinalIgnoreCase);
                    
                    // If no exact match, check if this is the actual FASTA file
                    // (i.e., not a metadata file like README, md5sum, etc.)
                    if (!isMatch && !metadataFiles.Contains(elementName))
                    {
                        // This appears to be a data file (not metadata)
                        // In NCBI dataset extractions, this is typically the FASTA file we want
                        _log.LogDebug("Element '{ElementName}' appears to be the extracted FASTA file (not metadata)", elementName);
                        isMatch = true;
                    }
                    
                    if (isMatch)
                    {
                        if (obj.TryGetProperty("id", out var datasetIdProperty))
                        {
                            var datasetId = datasetIdProperty.GetString();
                            if (!string.IsNullOrEmpty(datasetId))
                            {
                                _log.LogInformation("Found match for '{TargetFile}' in element '{ElementName}'", targetFileName, elementName);
                                return datasetId;
                            }
                        }
                    }
                }
            }
            
            return null;
        }

        private async Task<string> UnzipAsync(string historyId, string zipDatasetId, string targetFilePath, CancellationToken ct)
        {
            var unzipToolId = _opts.UnzipToolId ?? "toolshed.g2.bx.psu.edu/repos/imgteam/unzip/unzip/6.0+galaxy2";
            
            _log.LogDebug("Unzipping dataset {DatasetId} to extract file: {FilePath} using tool {ToolId}", 
                zipDatasetId, targetFilePath, unzipToolId);
            
            var build = await _galaxy.GetToolBuildAsync(unzipToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, unzipToolId);
            
            // Find the input parameter name (typically "input" or "input_file")
            var inputParam = FindSingleDataParamRecursive(inputsNode) ?? "input";
            
            var inputs = new Dictionary<string, object>
            {
                [inputParam] = new { src = "hda", id = zipDatasetId },
                ["extract_single_file"] = new 
                {
                    single_file_selector = "single",  // "Single file" mode
                    filepath = targetFilePath          // Path to the specific file to extract
                }
            };
            
            var (_, raw) = await _galaxy.InvokeToolAsync(historyId, unzipToolId, inputs, ct);
            
            // The unzip tool may return either a single dataset or a collection
            // First try to get from outputs (single dataset)
            if (raw.TryGetProperty("outputs", out var outputs) && 
                outputs.ValueKind == JsonValueKind.Array && 
                outputs.GetArrayLength() > 0)
            {
                var datasetId = outputs[0].GetProperty("id").GetString();
                _log.LogDebug("Unzip extracted file to dataset: {DatasetId}", datasetId);
                return datasetId!;
            }
            
            // If not in outputs, check output_collections
            if (raw.TryGetProperty("output_collections", out var collections) && 
                collections.ValueKind == JsonValueKind.Array && 
                collections.GetArrayLength() > 0)
            {
                var collectionId = collections[0].GetProperty("id").GetString();
                _log.LogInformation("Unzip created collection: {CollectionId}, waiting for it to be populated before extracting FASTA file", collectionId);
                
                // Extract the job ID so we can poll for completion
                string? jobId = null;
                if (raw.TryGetProperty("jobs", out var jobs) && 
                    jobs.ValueKind == JsonValueKind.Array && 
                    jobs.GetArrayLength() > 0)
                {
                    var firstJob = jobs[0];
                    if (firstJob.TryGetProperty("id", out var jid))
                    {
                        jobId = jid.GetString();
                        _log.LogInformation("Unzip job ID: {JobId}, waiting for completion...", jobId);
                    }
                }
                
                // Wait for the unzip job to complete
                if (!string.IsNullOrEmpty(jobId))
                {
                    await WaitForJobCompletionAsync(jobId, ct);
                }
                else
                {
                    // If we couldn't get the job ID, wait a bit for the collection to populate
                    _log.LogWarning("Could not extract job ID from unzip response, waiting 5 seconds for collection to populate");
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                
                // Get the collection details to find the actual FASTA file
                var collectionDetails = await _galaxy.GetCollectionDetailsAsync(historyId, collectionId!, ct);
                
                // Log the entire collection structure for debugging
                _log.LogInformation("Collection structure after job completion: {Json}", collectionDetails.ToString());
                
                // Look for the protein.faa or cds_from_genomic.fna file in the collection
                var targetFileName = targetFilePath.Contains("protein.faa") ? "protein.faa" : "cds_from_genomic.fna";
                
                // Declare elementNames outside the if block so it's available in the exception message
                List<string?> elementNames = new List<string?>();
                
                if (collectionDetails.TryGetProperty("elements", out var elements) && 
                    elements.ValueKind == JsonValueKind.Array)
                {
                    // Log all available elements for debugging
                    elementNames = elements.EnumerateArray()
                        .Where(e => e.TryGetProperty("element_identifier", out _))
                        .Select(e => e.GetProperty("element_identifier").GetString())
                        .ToList();
                    
                    _log.LogInformation("Collection {CollectionId} contains {Count} elements: {Elements}", 
                        collectionId, elementNames.Count, string.Join(", ", elementNames));
                    
                    // The collection structure from the screenshot shows:
                    // - README
                    // - md5sum
                    // - ncbi_dataset_data_GCF_030788295 (this is the element containing protein.faa)
                    // We need to find the element that looks like "ncbi_dataset_data_*" or contains "data"
                    
                    // First, check if any elements directly match the FASTA filename
                    foreach (var element in elements.EnumerateArray())
                    {
                        if (element.TryGetProperty("element_identifier", out var identifier))
                        {
                            var elementName = identifier.GetString() ?? "";
                            
                            // Direct match for the FASTA file
                            if (elementName.Equals(targetFileName, StringComparison.OrdinalIgnoreCase) ||
                                elementName.EndsWith($"/{targetFileName}", StringComparison.OrdinalIgnoreCase) ||
                                elementName.EndsWith($"\\{targetFileName}", StringComparison.OrdinalIgnoreCase))
                            {
                                if (element.TryGetProperty("object", out var obj) && 
                                    obj.TryGetProperty("id", out var id))
                                {
                                    var fastaDatasetId = id.GetString();
                                    _log.LogInformation("Found direct match for '{TargetFile}' with dataset ID: {DatasetId}", 
                                        targetFileName, fastaDatasetId);
                                    return fastaDatasetId!;
                                }
                            }
                        }
                    }
                    
                    // If not found directly, look for the ncbi_dataset/data subfolder structure
                    // The element might be a nested collection that we need to traverse
                    foreach (var element in elements.EnumerateArray())
                    {
                        if (element.TryGetProperty("element_identifier", out var identifier))
                        {
                            var elementName = identifier.GetString() ?? "";
                            _log.LogInformation("Examining element: {ElementName}, Type: {ElementType}", 
                                elementName, 
                                element.TryGetProperty("element_type", out var et) ? et.GetString() : "unknown");
                            
                            // Check if this is a nested collection (folder) that might contain our FASTA
                            if (element.TryGetProperty("object", out var obj))
                            {
                                // Check if it's a dataset_collection (nested folder)
                                if (obj.TryGetProperty("id", out var objId) && 
                                    obj.TryGetProperty("collection_type", out _))
                                {
                                    // This is a nested collection - recursively get its details
                                    var nestedCollectionId = objId.GetString();
                                    _log.LogInformation("Element '{ElementName}' is a nested collection: {NestedId}", 
                                        elementName, nestedCollectionId);
                                    
                                    try
                                    {
                                        var nestedDetails = await _galaxy.GetCollectionDetailsAsync(historyId, nestedCollectionId!, ct);
                                        if (nestedDetails.TryGetProperty("elements", out var nestedElements))
                                        {
                                            foreach (var nestedElement in nestedElements.EnumerateArray())
                                            {
                                                if (nestedElement.TryGetProperty("element_identifier", out var nestedId))
                                                {
                                                    var nestedName = nestedId.GetString() ?? "";
                                                    _log.LogInformation("  Nested element: {NestedName}", nestedName);
                                                    
                                                    // Check if this nested element is our FASTA
                                                    if (nestedName.Equals(targetFileName, StringComparison.OrdinalIgnoreCase) ||
                                                        nestedName.EndsWith($"/{targetFileName}", StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        if (nestedElement.TryGetProperty("object", out var nestedObj) && 
                                                            nestedObj.TryGetProperty("id", out var nestedDatasetId))
                                                        {
                                                            var fastaDatasetId = nestedDatasetId.GetString();
                                                            _log.LogInformation("Found FASTA in nested collection: {DatasetId}", 
                                                                fastaDatasetId);
                                                            return fastaDatasetId!;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _log.LogWarning(ex, "Failed to traverse nested collection {NestedId}", nestedCollectionId);
                                    }
                                }
                                // Regular dataset (not a collection)
                                else if (obj.TryGetProperty("id", out var datasetId))
                                {
                                    // Look for elements that match the NCBI dataset data pattern
                                    if ((elementName.StartsWith("ncbi_dataset_data_", StringComparison.OrdinalIgnoreCase) ||
                                         elementName.Contains("GCF_", StringComparison.OrdinalIgnoreCase) ||
                                         elementName.Contains("GCA_", StringComparison.OrdinalIgnoreCase) ||
                                         elementName.EndsWith(".faa", StringComparison.OrdinalIgnoreCase) ||
                                         elementName.EndsWith(".fna", StringComparison.OrdinalIgnoreCase) ||
                                         (elementName.Contains("data", StringComparison.OrdinalIgnoreCase) &&
                                          !elementName.Contains("README", StringComparison.OrdinalIgnoreCase) &&
                                          !elementName.Contains("md5sum", StringComparison.OrdinalIgnoreCase))))
                                    {
                                        var fastaDatasetId = datasetId.GetString();
                                        _log.LogInformation("Found potential FASTA element '{ElementName}' with dataset ID: {DatasetId}", 
                                            elementName, fastaDatasetId);
                                        return fastaDatasetId!;
                                    }
                                }
                            }
                        }
                    }
                    
                    // If still not found, log all elements with their full structure for debugging
                    _log.LogWarning("Could not find FASTA file in collection. Available elements: {Json}", 
                        string.Join(", ", elementNames));
                }
                
                throw new InvalidOperationException(
                        $"Could not find FASTA file '{targetFileName}' in unzipped collection {collectionId}. " +
                        $"Available elements: {string.Join(", ", elementNames ?? new List<string?>())}");
            }
            
            throw new InvalidOperationException($"Unzip tool did not return a dataset or collection for file: {targetFilePath}");
        }

        /// <summary>
        /// Step 2: Get orthologous groups from completed OrthoFinder run
        /// </summary>
        public Task<IReadOnlyList<OrthologousGroup>> GetOrthologousGroupsAsync(
            string historyId, 
            string orthoFinderJobId, 
            CancellationToken ct)
        {
            return GetOrthologousGroupsAsync(historyId, orthoFinderJobId, null, ct);
        }

        public async Task<IReadOnlyList<OrthologousGroup>> GetOrthologousGroupsAsync(
            string historyId, 
            string orthoFinderJobId, 
            IReadOnlyCollection<string>? selectedOrthogroupIds,
            CancellationToken ct)
        {
            using var apiScope = UseApiKeyForHistory(historyId);

            // Wait for job completion if needed
            string? jobState = null;
            var isLikelyJobId = !string.IsNullOrWhiteSpace(orthoFinderJobId) && orthoFinderJobId.Length >= 16;
            if (isLikelyJobId)
            {
                try
                {
                    var job = await _galaxy.GetJobAsync(orthoFinderJobId, ct);
                    jobState = job.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;
                    if (jobState != "ok")
                    {
                        _log.LogInformation("OrthoFinder job {JobId} state is {State}; proceeding to look for existing outputs in history.", orthoFinderJobId, jobState);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Unable to read OrthoFinder job state for job {JobId}; proceeding to look for outputs in history.", orthoFinderJobId);
                }
            }
            else if (!string.IsNullOrWhiteSpace(orthoFinderJobId))
            {
                jobState = "ok";
            }

            // Get the orthologous groups output file - look for the hierarchical N0.tsv file
            JsonElement? outputs = null;
            if (isLikelyJobId)
            {
                try
                {
                    outputs = await _galaxy.GetJobOutputsAsync(orthoFinderJobId, ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to read outputs for OrthoFinder job {JobId}; falling back to history scan.", orthoFinderJobId);
                }
            }

            var standardOrthoGroupsId = (outputs != null ? FindOrthoGroupsOutputDataset(outputs.Value) : null)
                ?? await FindOrthoGroupsDatasetInHistoryAsync(historyId, ct);
            var hierarchicalOrthoGroupsId = outputs != null
                ? await FindHierarchicalOrthoGroupsOutputDatasetAsync(historyId, outputs.Value, ct)
                : await FindHierarchicalOrthoGroupsDatasetInHistoryAsync(historyId, ct);

            if (string.IsNullOrEmpty(standardOrthoGroupsId) && string.IsNullOrEmpty(hierarchicalOrthoGroupsId))
                throw new InvalidOperationException("Could not find orthologous groups output from OrthoFinder");

            var groupsDatasetId = standardOrthoGroupsId ?? hierarchicalOrthoGroupsId!;
            await WaitForDatasetReadyAsync(groupsDatasetId, ct);
            var orthoGroupsContent = await _galaxy.DownloadDatasetAsync(historyId, groupsDatasetId, ct);
            var parsedGroups = ParseOrthologousGroups(orthoGroupsContent);

            var selectedGroupIdSet = selectedOrthogroupIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (selectedGroupIdSet is { Count: > 0 })
            {
                parsedGroups = parsedGroups
                    .Where(g => selectedGroupIdSet.Contains(g.GroupId))
                    .ToList();
                _log.LogInformation("Received {Count} selected orthogroup(s); filtering returned groups and downstream packaging to the selection.", parsedGroups.Count);
            }

            // If hierarchical output exists, continue to build single-copy filtered set for downstream workflows
            if (!string.IsNullOrWhiteSpace(hierarchicalOrthoGroupsId))
            {
                var metadata = await LoadMetadataAsync(historyId, ct);
                var genomes = metadata.Genomes ?? new List<GenomeInfo>();
                var speciesCount = genomes.Count;

                if (speciesCount <= 0)
                {
                    // Fallback: derive species count from N0 header
                    var n0Header = await ReadFirstLineAsync(historyId, hierarchicalOrthoGroupsId!, ct);
                    speciesCount = DeriveSpeciesCountFromHeader(n0Header);
                    if (speciesCount <= 0 && parsedGroups is { Count: > 0 })
                    {
                        speciesCount = parsedGroups
                            .SelectMany(g => g.SpeciesGenes.Keys)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();
                        _log.LogInformation("Derived species count {Count} from Orthogroups.tsv species keys", speciesCount);
                    }
                    if (speciesCount <= 0)
                        throw new InvalidOperationException("Could not determine species count for orthogroup filtering.");
                    _log.LogInformation("Derived species count {Count} from N0 header", speciesCount);
                }

                // Ensure the filtered single-copy dataset exists (reuse if already present)
                var (filteredDatasetId, reusedFiltered) = await EnsureFilteredSingleCopyDatasetAsync(
                    historyId, hierarchicalOrthoGroupsId!, speciesCount, ct);

                var filteredContentFinal = await _galaxy.DownloadDatasetAsync(historyId, filteredDatasetId, ct);

                _log.LogInformation("Parsing filtered single-copy orthogroups from Filter1 output");
                var singleCopyGroups = ParseHierarchicalSingleCopyOrthogroups(filteredContentFinal, genomes);

                // If the reused dataset was empty, force a fresh filter run to make sure we populate results
                if (singleCopyGroups.Count == 0 && reusedFiltered)
                {
                    _log.LogWarning("Filtered orthogroups dataset {DatasetId} contained 0 groups; rerunning filter on N0 dataset {N0DatasetId}", filteredDatasetId, hierarchicalOrthoGroupsId);
                    filteredDatasetId = await FilterHierarchicalOrthoGroupsAsync(historyId, hierarchicalOrthoGroupsId!, speciesCount, ct);
                    filteredContentFinal = await _galaxy.DownloadDatasetAsync(historyId, filteredDatasetId, ct);
                    singleCopyGroups = ParseHierarchicalSingleCopyOrthogroups(filteredContentFinal, genomes);
                }

                // Prefer the filtered single-copy (1:1) orthogroups for user selection
                if (singleCopyGroups.Count > 0)
                {
                    parsedGroups = selectedGroupIdSet is { Count: > 0 }
                        ? singleCopyGroups.Where(g => selectedGroupIdSet.Contains(g.GroupId)).ToList()
                        : singleCopyGroups.ToList();
                    _log.LogInformation("Using {Count} single-copy orthogroups (1:1) for selection and downstream workflows.", parsedGroups.Count);
                }

                // Collect key OrthoFinder outputs for later download and visualization
                try
                {
                    var outputManifest = await GetOrthoFinderOutputsAsync(historyId, orthoFinderJobId, ct);
                    _log.LogInformation("Collected OrthoFinder outputs for history {HistoryId}: {Count} items", historyId, outputManifest.Outputs.Count);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to collect OrthoFinder outputs for history {HistoryId}", historyId);
                }

                // Fetching orthogroups is read-only. Downstream Galaxy work starts only
                // after the user explicitly submits a selection.
                _log.LogInformation("Orthogroup retrieval complete; waiting for an explicit downstream selection submission.");
            }
            else
            {
                // Fallback: derive single-copy (1:1) orthogroups directly from Orthogroups.tsv when no hierarchical output is found
                var metadata = await LoadMetadataAsync(historyId, ct);
                var genomes = metadata.Genomes ?? new List<GenomeInfo>();
                var speciesSet = genomes
                    .Select(g => g.Species)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (speciesSet.Count > 0)
                {
                    var singleCopy = parsedGroups.Where(g =>
                    {
                        if (g.SpeciesGenes == null) return false;
                        foreach (var species in speciesSet)
                        {
                            if (!g.SpeciesGenes.TryGetValue(species, out var genesRaw) || string.IsNullOrWhiteSpace(genesRaw))
                                return false;
                            var genes = genesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            if (genes.Length != 1) return false;
                        }
                        return true;
                    }).ToList();

                    if (singleCopy.Count > 0)
                    {
                        parsedGroups = selectedGroupIdSet is { Count: > 0 }
                            ? singleCopy.Where(g => selectedGroupIdSet.Contains(g.GroupId)).ToList()
                            : singleCopy;
                        _log.LogInformation("Using {Count} single-copy orthogroups (1:1) from Orthogroups.tsv fallback.", parsedGroups.Count);
                    }
                    else
                    {
                        _log.LogWarning("Hierarchical orthogroups (N0) not found and no single-copy fallback groups detected; returning all groups.");
                    }
                }
                else
                {
                    _log.LogWarning("Hierarchical orthogroups (N0) not found; returning groups from Orthogroups.tsv without single-copy filtering.");
                }
            }

            return parsedGroups;
        }

        /// <summary>
        /// Step 3: Run selection pressure analysis on selected orthologous groups
        /// This combines CDS extraction, alignment, translation, and HYPHY BUSTED analysis
        /// </summary>
        public async Task<List<SelectionAnalysisResult>> RunSelectionAnalysisAsync(
            string historyId,
            List<OrthologousGroup> selectedGroups,
            List<GenomeInfo> genomes,
            string? ncbiApiKey,
            string alignment = "mafft",
            CancellationToken ct = default)
        {
            using var apiScope = UseApiKeyForHistory(historyId);

            if (selectedGroups is null || selectedGroups.Count == 0)
                throw new InvalidOperationException("Select at least one strict 1:1 orthogroup before starting downstream analysis.");
            if (genomes is null || genomes.Count < MinSpeciesCount)
                throw new InvalidOperationException($"At least {MinSpeciesCount} genomes are required for downstream analysis.");
            if (!string.Equals(alignment?.Trim(), "mafft", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The integrated phyloDIVaS v2 Galaxy workflow currently requires MAFFT.");

            var selectedN0 = BuildSelectedN0Subset(selectedGroups, genomes);
            var batchStamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var selectedN0Id = await _galaxy.UploadTextContentAsync(
                historyId,
                selectedN0,
                $"phyloDIVaS selected N0 orthogroups ({selectedGroups.Count}) {batchStamp}.tsv",
                "tabular",
                ct);

            var historyContents = await _galaxy.ListHistoryContentsAsync(
                historyId, ct, includeHidden: true, includeDeleted: false);
            var mergedProteomeId = FindLatestHistoryDatasetId(
                historyContents,
                name => name.Equals("phyloDIVaS merged filtered proteomes v2.fasta", StringComparison.OrdinalIgnoreCase));
            var mergedCdsId = FindLatestHistoryDatasetId(
                historyContents,
                name => name.Equals("phyloDIVaS merged normalized CDS v2.fasta", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(mergedProteomeId))
            {
                mergedProteomeId = await ConcatenateFastasAsync(
                    historyId,
                    genomes.Select(genome => genome.ProteinDatasetId).ToList(),
                    "phyloDIVaS merged filtered proteomes v2.fasta",
                    ct);
            }
            if (string.IsNullOrWhiteSpace(mergedCdsId))
            {
                mergedCdsId = await ConcatenateFastasAsync(
                    historyId,
                    genomes.Select(genome => genome.CdsDatasetId).ToList(),
                    "phyloDIVaS merged normalized CDS v2.fasta",
                    ct);
            }

            await WaitForDatasetReadyAsync(historyId, mergedProteomeId, ct);
            await WaitForDatasetReadyAsync(historyId, mergedCdsId, ct);
            var workflowJson = BuildBackgroundSelectionWorkflowDefinition(LoadSelectionWorkflowDefinition());
            ValidateUninterruptedSelectionWorkflow(workflowJson);
            var versionedWorkflow = VersionWorkflowDefinition(workflowJson);
            var workflowId = await _galaxy.EnsureWorkflowImportedAsync(
                versionedWorkflow.Name,
                versionedWorkflow.Json,
                ct);

            var workflowInputs = new Dictionary<string, object>
            {
                ["0"] = new { src = "hda", id = selectedN0Id },
                ["1"] = new { src = "hda", id = mergedProteomeId },
                ["2"] = new { src = "hda", id = mergedCdsId }
            };
            var (invocationId, invocationRaw) = await _galaxy.InvokeWorkflowAsync(
                workflowId, historyId, workflowInputs, ct);
            if (string.IsNullOrWhiteSpace(invocationId))
                throw new InvalidOperationException("Galaxy accepted the downstream workflow but did not return an invocation id.");

            await CreateOrthogroupWorkflowMarkerAsync(historyId, invocationId, ct);
            _log.LogInformation(
                "Started phyloDIVaS downstream workflow {WorkflowId} invocation {InvocationId} for {Count} selected orthogroups.",
                workflowId, invocationId, selectedGroups.Count);

            return selectedGroups.Select(group => new SelectionAnalysisResult(
                historyId,
                string.Empty,
                string.Empty,
                invocationRaw,
                new Dictionary<string, string>
                {
                    ["group_id"] = group.GroupId,
                    ["workflow_id"] = workflowId,
                    ["workflow_invocation"] = invocationId,
                    ["selected_n0"] = selectedN0Id,
                    ["merged_proteomes"] = mergedProteomeId,
                    ["merged_cds"] = mergedCdsId
                })).ToList();
        }

        internal static string BuildSelectedN0Subset(
            IReadOnlyList<OrthologousGroup> selectedGroups,
            IReadOnlyList<GenomeInfo> genomes)
        {
            static string Clean(string value) =>
                (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

            static string? FindSingleGene(OrthologousGroup group, GenomeInfo genome)
            {
                var aliases = new[] { genome.Species, genome.OriginalSpecies, genome.StandardizedSpecies }
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Select(alias => alias.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var alias in aliases)
                {
                    if (group.SpeciesGenes.TryGetValue(alias, out var exact))
                        return exact;
                }

                var normalizedAliases = aliases
                    .Select(alias => SanitizeName(NormalizeRecoveredSpeciesName(alias)))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return group.SpeciesGenes
                    .FirstOrDefault(entry => normalizedAliases.Contains(
                        SanitizeName(NormalizeRecoveredSpeciesName(entry.Key))))
                    .Value;
            }

            var headerSpecies = genomes.Select(genome =>
                Clean(string.IsNullOrWhiteSpace(genome.StandardizedSpecies) ? genome.Species : genome.StandardizedSpecies));
            var output = new StringBuilder()
                .Append("HOG\tOG\tGene Tree Parent Clade\t")
                .AppendLine(string.Join('\t', headerSpecies));

            foreach (var group in selectedGroups.DistinctBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase))
            {
                var genes = new List<string>(genomes.Count);
                foreach (var genome in genomes)
                {
                    var raw = FindSingleGene(group, genome);
                    var ids = (raw ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (ids.Length != 1 || ids[0] is "-" or "0")
                        throw new InvalidOperationException(
                            $"{group.GroupId} is not strict 1:1 for {genome.Species}; expected exactly one gene ID.");
                    genes.Add(Clean(ids[0]));
                }

                output.Append(Clean(group.GroupId)).Append('\t')
                    .Append(Clean(group.GroupName)).Append("\tN0\t")
                    .AppendLine(string.Join('\t', genes));
            }

            return output.ToString();
        }

        private static string LoadSelectionWorkflowDefinition()
        {
            const string resourceName = "CapstoneServices.GalaxyWorkflows.phyloDIVaS.Selection.v2.ga";
            var assembly = typeof(GalaxyOrthoFinderPipeline).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Bundled Galaxy workflow resource '{resourceName}' was not found.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        internal static (string Name, string Json, string Revision) VersionWorkflowDefinition(string workflowJson)
        {
            var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workflowJson)))
                .ToLowerInvariant()[..12];
            var workflow = JsonNode.Parse(workflowJson)?.AsObject()
                ?? throw new InvalidOperationException("The bundled Galaxy workflow is not a JSON object.");
            var baseName = workflow["name"]?.GetValue<string>()
                ?? throw new InvalidOperationException("The bundled Galaxy workflow has no name.");
            var versionedName = $"{baseName} [revision {revision}]";

            // Galaxy assigns identity on import. Retaining the exported UUID can cause
            // a corrected definition to resolve back to an older user-owned workflow.
            workflow.Remove("uuid");
            workflow.Remove("id");
            workflow["name"] = versionedName;
            return (versionedName, workflow.ToJsonString(), revision);
        }

        internal static string BuildHyphyRetryWorkflowDefinition(string selectionWorkflowJson)
        {
            var source = JsonNode.Parse(selectionWorkflowJson)?.AsObject()
                ?? throw new InvalidOperationException("The bundled Galaxy workflow is not a JSON object.");
            var sourceSteps = source["steps"]?.AsObject()
                ?? throw new InvalidOperationException("The bundled Galaxy workflow has no steps.");

            static JsonObject CollectionInput(int id, string name, int top)
            {
                return new JsonObject
                {
                    ["id"] = id,
                    ["type"] = "data_collection_input",
                    ["name"] = "Input dataset collection",
                    ["label"] = name,
                    ["annotation"] = "",
                    ["content_id"] = null,
                    ["input_connections"] = new JsonObject(),
                    ["inputs"] = new JsonArray(new JsonObject
                    {
                        ["name"] = name,
                        ["description"] = "",
                        ["collection_type"] = "list"
                    }),
                    ["outputs"] = new JsonArray(),
                    ["position"] = new JsonObject { ["left"] = 0, ["top"] = top },
                    ["tool_state"] = "{\"collection_type\":\"list\"}",
                    ["workflow_outputs"] = new JsonArray()
                };
            }

            static JsonObject HyphyStep(JsonObject sourceStep, int id, int top, string treeInput)
            {
                var step = sourceStep.DeepClone().AsObject();
                step["id"] = id;
                step.Remove("uuid");
                step["position"] = new JsonObject { ["left"] = 360, ["top"] = top };
                step["input_connections"] = new JsonObject
                {
                    ["input_file"] = new JsonObject { ["id"] = 0, ["output_name"] = "output" },
                    [treeInput] = new JsonObject { ["id"] = 1, ["output_name"] = "output" }
                };
                return step;
            }

            static JsonObject StandardMemeStep()
            {
                const string toolId = "toolshed.g2.bx.psu.edu/repos/iuc/hyphy_meme/hyphy_meme/2.5.96+galaxy0";
                var toolState = new JsonObject
                {
                    ["input_file"] = new JsonObject { ["__class__"] = "ConnectedValue" },
                    ["input_nhx"] = new JsonObject { ["__class__"] = "ConnectedValue" },
                    ["gencodeid"] = "Universal",
                    ["branch_cond"] = new JsonObject { ["branch_sel"] = "All", ["__current_case__"] = 1 },
                    ["p_value"] = "0.1",
                    ["advanced_options"] = new JsonObject
                    {
                        ["resample"] = "0",
                        ["rates"] = "2",
                        ["multiple_hits_conditional"] = new JsonObject
                        {
                            ["multiple_hits"] = "None",
                            ["__current_case__"] = 2
                        },
                        ["impute_states"] = false,
                        ["precision"] = "standard",
                        ["kill_zero_lengths"] = "Yes",
                        ["restrict_sites_conditional"] = new JsonObject
                        {
                            ["restrict_sites_flag"] = "false",
                            ["__current_case__"] = 1
                        },
                        ["full_model"] = true
                    },
                    ["__page__"] = 0,
                    ["__rerun_remap_job_id__"] = null
                };
                return new JsonObject
                {
                    ["id"] = 2,
                    ["type"] = "tool",
                    ["name"] = "HyPhy-MEME",
                    ["label"] = null,
                    ["annotation"] = "Standard HyPhy Mixed Effects Model of Evolution",
                    ["content_id"] = toolId,
                    ["tool_id"] = toolId,
                    ["tool_version"] = "2.5.96+galaxy0",
                    ["tool_state"] = toolState.ToJsonString(),
                    ["tool_shed_repository"] = new JsonObject
                    {
                        ["name"] = "hyphy_meme",
                        ["owner"] = "iuc",
                        ["changeset_revision"] = "67fe4f52c1e5",
                        ["tool_shed"] = "toolshed.g2.bx.psu.edu"
                    },
                    ["input_connections"] = new JsonObject
                    {
                        ["input_file"] = new JsonObject { ["id"] = 0, ["output_name"] = "output" },
                        ["input_nhx"] = new JsonObject { ["id"] = 1, ["output_name"] = "output" }
                    },
                    ["inputs"] = new JsonArray(),
                    ["outputs"] = new JsonArray(
                        new JsonObject { ["name"] = "meme_output", ["type"] = "hyphy_results.json" },
                        new JsonObject { ["name"] = "meme_md_report", ["type"] = "markdown" }),
                    ["position"] = new JsonObject { ["left"] = 360, ["top"] = 40 },
                    ["post_job_actions"] = new JsonObject(),
                    ["workflow_outputs"] = new JsonArray(
                        new JsonObject { ["label"] = "meme_json", ["output_name"] = "meme_output" },
                        new JsonObject { ["label"] = "meme_report", ["output_name"] = "meme_md_report" })
                };
            }

            var steps = new JsonObject
            {
                ["0"] = CollectionInput(0, "Completed codon alignments", 80),
                ["1"] = CollectionInput(1, "Completed IQ-TREE gene trees", 260),
                ["2"] = StandardMemeStep(),
                ["3"] = HyphyStep(sourceSteps["12"]?.AsObject()
                    ?? throw new InvalidOperationException("The bundled workflow has no BUSTED step."), 3, 220, "input_nhx"),
                ["4"] = HyphyStep(sourceSteps["13"]?.AsObject()
                    ?? throw new InvalidOperationException("The bundled workflow has no aBSREL step."), 4, 400, "input_nhx")
            };

            return new JsonObject
            {
                ["a_galaxy_workflow"] = true,
                ["format-version"] = "0.1",
                ["name"] = "phyloDIVaS HyPhy retry from completed alignments v1",
                ["annotation"] = "Runs MEME, BUSTED and aBSREL from existing codon-alignment and IQ-TREE collections without repeating upstream analysis.",
                ["tags"] = new JsonArray("phyloDIVaS", "HyPhy", "recovery"),
                ["steps"] = steps
            }.ToJsonString();
        }

        internal static void ValidateUninterruptedSelectionWorkflow(string workflowJson)
        {
            var workflow = JsonNode.Parse(workflowJson)?.AsObject()
                ?? throw new InvalidOperationException("The Galaxy workflow definition is invalid.");
            var steps = workflow["steps"]?.AsObject()
                ?? throw new InvalidOperationException("The Galaxy workflow has no steps.");
            foreach (var step in steps.Select(pair => pair.Value).OfType<JsonObject>())
                if (step["type"]?.GetValue<string>() == "pause")
                    throw new InvalidOperationException("The downstream workflow contains a pause and cannot run unattended.");
            foreach (var id in new[] { "11", "12", "13" })
            {
                var connections = steps[id]?["input_connections"];
                if (connections?["input_file"]?["id"]?.GetValue<int>() != 9 ||
                    connections?["input_nhx"]?["id"]?.GetValue<int>() != 10 ||
                    connections?["input_nhx"]?["output_name"]?.GetValue<string>() != "treefile")
                    throw new InvalidOperationException("The downstream workflow must connect each HyPhy tool to codon alignments and IQ-TREE outputs before it can be submitted.");
            }
        }

        internal static string BuildPreparationWorkflowDefinition(string selectionWorkflowJson)
        {
            var workflow = JsonNode.Parse(selectionWorkflowJson)?.AsObject()
                ?? throw new InvalidOperationException("The bundled Galaxy workflow is not a JSON object.");
            var steps = workflow["steps"]?.AsObject()
                ?? throw new InvalidOperationException("The bundled Galaxy workflow has no steps.");
            steps.Remove("11");
            steps.Remove("12");
            steps.Remove("13");
            workflow["name"] = "phyloDIVaS alignment and IQ-TREE preparation v3";
            workflow["annotation"] =
                "Builds orthogroup protein/CDS collections, codon alignments and IQ-TREE trees. HyPhy is launched only after identifier normalization and validation.";
            workflow.Remove("uuid");
            workflow.Remove("id");
            return workflow.ToJsonString();
        }

        internal static string BuildBackgroundSelectionWorkflowDefinition(string selectionWorkflowJson)
        {
            var workflow = JsonNode.Parse(selectionWorkflowJson)?.AsObject()
                ?? throw new InvalidOperationException("The bundled Galaxy workflow is not a JSON object.");
            var steps = workflow["steps"]?.AsObject()
                ?? throw new InvalidOperationException("The bundled Galaxy workflow has no steps.");

            const string awkToolId = "toolshed.g2.bx.psu.edu/repos/bgruening/text_processing/tp_awk_tool/9.11+galaxy0";
            const string normalizeHeaders = "BEGIN { OFS = \"\\n\" }\n" +
                "/^>/ {\n" +
                "  header = substr($0, 2)\n" +
                "  if (match(header, /[A-Za-z][A-Za-z0-9]*_[0-9]+([.][0-9]+)?/)) id = substr(header, RSTART, RLENGTH)\n" +
                "  else { split(header, fields, /[[:space:]]+/); id = fields[1] }\n" +
                "  gsub(/[^A-Za-z0-9_-]/, \"_\", id)\n" +
                "  gsub(/_+/, \"_\", id)\n" +
                "  sub(/^_+/, \"\", id); sub(/_+$/, \"\", id)\n" +
                "  print \">\" id\n" +
                "  next\n" +
                "}\n" +
                "{ print }";

            static JsonObject ConnectedValue() => new() { ["__class__"] = "ConnectedValue" };

            static JsonObject HeaderNormalizerStep(
                int id,
                string name,
                int sourceId,
                string sourceOutput,
                string workflowOutput,
                int top,
                string toolId,
                string code)
            {
                var toolState = new JsonObject
                {
                    ["infile"] = ConnectedValue(),
                    ["code"] = code,
                    ["variables"] = new JsonArray(),
                    ["__page__"] = 0,
                    ["__rerun_remap_job_id__"] = null
                };
                return new JsonObject
                {
                    ["id"] = id,
                    ["type"] = "tool",
                    ["name"] = name,
                    ["label"] = null,
                    ["annotation"] = "Rewrites only FASTA identifiers to a shared HyPhy-safe accession format; sequence data are unchanged.",
                    ["content_id"] = toolId,
                    ["tool_id"] = toolId,
                    ["tool_version"] = "9.11+galaxy0",
                    ["tool_state"] = toolState.ToJsonString(),
                    ["input_connections"] = new JsonObject
                    {
                        ["infile"] = new JsonObject { ["id"] = sourceId, ["output_name"] = sourceOutput }
                    },
                    ["inputs"] = new JsonArray(),
                    ["outputs"] = new JsonArray(new JsonObject { ["name"] = "outfile", ["type"] = "data" }),
                    ["position"] = new JsonObject { ["left"] = 1280, ["top"] = top },
                    ["post_job_actions"] = new JsonObject(),
                    ["workflow_outputs"] = new JsonArray(
                        new JsonObject { ["label"] = workflowOutput, ["output_name"] = "outfile" })
                };
            }

            steps["14"] = HeaderNormalizerStep(
                14,
                "Normalize protein FASTA identifiers for Galaxy-native HyPhy",
                6,
                "output_pos",
                "hyphy_safe_protein_fastas",
                80,
                awkToolId,
                normalizeHeaders);
            steps["15"] = HeaderNormalizerStep(
                15,
                "Normalize CDS FASTA identifiers for Galaxy-native HyPhy",
                7,
                "output_pos",
                "hyphy_safe_cds_fastas",
                360,
                awkToolId,
                normalizeHeaders);

            var mafftConnections = steps["8"]?["input_connections"]?.AsObject()
                ?? throw new InvalidOperationException("The bundled workflow has no MAFFT inputs.");
            mafftConnections["input|batches_0|inputs"] = new JsonObject
            {
                ["id"] = 14,
                ["output_name"] = "outfile"
            };
            var tranalignConnections = steps["9"]?["input_connections"]?.AsObject()
                ?? throw new InvalidOperationException("The bundled workflow has no tranalign inputs.");
            tranalignConnections["input1"] = new JsonObject
            {
                ["id"] = 15,
                ["output_name"] = "outfile"
            };

            workflow["name"] = "phyloDIVaS uninterrupted downstream selection v4";
            workflow["annotation"] =
                "Normalizes matched protein/CDS identifiers inside Galaxy, then runs MAFFT, tranalign, IQ-TREE, MEME, BUSTED and aBSREL in one invocation that does not depend on the phyloDIVaS browser remaining open.";
            workflow.Remove("uuid");
            workflow.Remove("id");
            return workflow.ToJsonString();
        }

        internal static string BuildPostMafftRecoveryWorkflowDefinition(string selectionWorkflowJson)
        {
            var source = JsonNode.Parse(selectionWorkflowJson)?.AsObject()
                ?? throw new InvalidOperationException("The bundled Galaxy workflow is not a JSON object.");
            var sourceSteps = source["steps"]?.AsObject()
                ?? throw new InvalidOperationException("The bundled Galaxy workflow has no steps.");

            static JsonObject CollectionInput(int id, string name, int top) => new()
            {
                ["id"] = id,
                ["type"] = "data_collection_input",
                ["name"] = name,
                ["label"] = null,
                ["annotation"] = "",
                ["content_id"] = null,
                ["input_connections"] = new JsonObject(),
                ["inputs"] = new JsonArray(new JsonObject
                {
                    ["name"] = name,
                    ["description"] = "",
                    ["collection_type"] = "list"
                }),
                ["outputs"] = new JsonArray(),
                ["position"] = new JsonObject { ["left"] = 0, ["top"] = top },
                ["tool_state"] = "{\"collection_type\":\"list\"}",
                ["workflow_outputs"] = new JsonArray()
            };

            static JsonObject CloneTool(JsonObject sourceStep, int id, int left, int top, JsonObject connections)
            {
                var step = sourceStep.DeepClone().AsObject();
                step["id"] = id;
                step.Remove("uuid");
                step["position"] = new JsonObject { ["left"] = left, ["top"] = top };
                step["input_connections"] = connections;
                return step;
            }

            var tranalign = CloneTool(
                sourceSteps["9"]?.AsObject() ?? throw new InvalidOperationException("The bundled workflow has no tranalign step."),
                2, 340, 120,
                new JsonObject
                {
                    ["input1"] = new JsonObject { ["id"] = 1, ["output_name"] = "output" },
                    ["input2"] = new JsonObject { ["id"] = 0, ["output_name"] = "output" }
                });
            var iqtree = CloneTool(
                sourceSteps["10"]?.AsObject() ?? throw new InvalidOperationException("The bundled workflow has no IQ-TREE step."),
                3, 680, 120,
                new JsonObject
                {
                    ["general_options|s"] = new JsonObject { ["id"] = 2, ["output_name"] = "out_file1" }
                });

            JsonObject HyphyStep(string sourceId, int id, int top)
            {
                var sourceStep = sourceSteps[sourceId]?.AsObject()
                    ?? throw new InvalidOperationException($"The bundled workflow has no HyPhy step {sourceId}.");
                return CloneTool(sourceStep, id, 1020, top, new JsonObject
                {
                    ["input_file"] = new JsonObject { ["id"] = 2, ["output_name"] = "out_file1" },
                    ["input_nhx"] = new JsonObject { ["id"] = 3, ["output_name"] = "treefile" }
                });
            }

            var steps = new JsonObject
            {
                ["0"] = CollectionInput(0, "Completed MAFFT protein alignments", 80),
                ["1"] = CollectionInput(1, "Filtered CDS sequences", 260),
                ["2"] = tranalign,
                ["3"] = iqtree,
                ["4"] = HyphyStep("11", 4, 40),
                ["5"] = HyphyStep("12", 5, 220),
                ["6"] = HyphyStep("13", 6, 400)
            };

            return new JsonObject
            {
                ["a_galaxy_workflow"] = true,
                ["format-version"] = "0.1",
                ["name"] = "phyloDIVaS resume after MAFFT v1",
                ["annotation"] = "Reuses completed MAFFT and CDS collections, then runs tranalign, IQ-TREE, MEME, BUSTED and aBSREL in one Galaxy-owned invocation.",
                ["tags"] = new JsonArray("phyloDIVaS", "recovery", "HyPhy"),
                ["steps"] = steps
            }.ToJsonString();
        }

        public async Task<DownstreamRecoveryResult> ResumeDownstreamFromCompletedMafftAsync(
            string historyId,
            string? galaxyApiKey,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(historyId))
                throw new ArgumentException("A Galaxy history ID is required.", nameof(historyId));
            if (string.IsNullOrWhiteSpace(galaxyApiKey))
                throw new ArgumentException("A Galaxy API key is required for this history.", nameof(galaxyApiKey));

            using var apiScope = _galaxy.UseApiKey(galaxyApiKey);
            using var historyReadCache = _galaxy.UseHistoryReadCache();
            await _galaxy.PrimeHistoryContentsCacheAsync(historyId, ct);
            var contents = await _galaxy.ListHistoryContentsAsync(
                historyId, ct, includeHidden: true, includeDeleted: false);
            var mafftCollectionId = FindLatestHistoryCollectionId(contents, name =>
                name.Contains("MAFFT", StringComparison.OrdinalIgnoreCase));
            var cdsCollectionId = FindLatestHistoryCollectionId(contents, name =>
                !name.Contains("codon", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("HyPhy", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("Filter CDS", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("CDS by orthogroup", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("filtered CDS", StringComparison.OrdinalIgnoreCase) ||
                 (name.Contains("CDS", StringComparison.OrdinalIgnoreCase) &&
                  name.Contains("orthogroup", StringComparison.OrdinalIgnoreCase))));

            if (string.IsNullOrWhiteSpace(mafftCollectionId))
                throw new InvalidOperationException("No completed MAFFT alignment collection was found in this Galaxy history.");
            if (string.IsNullOrWhiteSpace(cdsCollectionId))
                throw new InvalidOperationException("No filtered CDS collection matching the completed MAFFT collection was found in this Galaxy history.");

            var workflowJson = BuildPostMafftRecoveryWorkflowDefinition(LoadSelectionWorkflowDefinition());
            var versioned = VersionWorkflowDefinition(workflowJson);
            var workflowId = await _galaxy.EnsureWorkflowImportedAsync(versioned.Name, versioned.Json, ct);
            var inputs = new Dictionary<string, object>
            {
                ["0"] = new { src = "hdca", id = mafftCollectionId },
                ["1"] = new { src = "hdca", id = cdsCollectionId }
            };
            var (invocationId, raw) = await _galaxy.InvokeWorkflowAsync(workflowId, historyId, inputs, ct);
            if (string.IsNullOrWhiteSpace(invocationId))
                throw new InvalidOperationException("Galaxy accepted the post-MAFFT recovery workflow but did not return an invocation ID.");

            await _galaxy.UploadTextContentAsync(
                historyId,
                $"Post-MAFFT recovery started at {DateTimeOffset.UtcNow:o}; mafft={mafftCollectionId}; cds={cdsCollectionId}; invocation={invocationId}",
                "orthogroup_workflow_invoked_post_mafft_recovery.txt",
                "txt",
                ct);
            _log.LogInformation(
                "Resumed history {HistoryId} after MAFFT with alignment collection {MafftCollection}, CDS collection {CdsCollection}, invocation {InvocationId}.",
                historyId, mafftCollectionId, cdsCollectionId, invocationId);
            return new DownstreamRecoveryResult(
                historyId, invocationId, mafftCollectionId, cdsCollectionId, workflowId, raw);
        }

        public async Task<HyphyRetryResult> RetryHyphyFromCompletedAlignmentsAsync(
            string historyId,
            string? galaxyApiKey,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(historyId))
                throw new ArgumentException("A Galaxy history ID is required.", nameof(historyId));
            if (string.IsNullOrWhiteSpace(galaxyApiKey))
                throw new ArgumentException("A Galaxy API key is required for this history.", nameof(galaxyApiKey));

            using var apiScope = _galaxy.UseApiKey(galaxyApiKey);
            using var historyReadCache = _galaxy.UseHistoryReadCache();
            await _galaxy.PrimeHistoryContentsCacheAsync(historyId, ct);
            var contents = await _galaxy.ListHistoryContentsAsync(
                historyId, ct, includeHidden: true, includeDeleted: false);

            var rawCodonCollectionId = FindLatestHistoryCollectionId(contents, name =>
                !IsGeneratedHyphyRecoveryCollection(name) &&
                (name.Contains("codon_alignments", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("codon alignment", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("tranalign", StringComparison.OrdinalIgnoreCase)));
            var rawTreeCollectionId = FindLatestHistoryCollectionId(contents, name =>
                !IsGeneratedHyphyRecoveryCollection(name) &&
                (name.Equals("gene_trees", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("gene trees", StringComparison.OrdinalIgnoreCase) ||
                 (name.Contains("IQ-TREE", StringComparison.OrdinalIgnoreCase) &&
                  !name.Contains("report", StringComparison.OrdinalIgnoreCase))));
            var iqtreeReportCollectionId = FindLatestHistoryCollectionId(contents, name =>
                !IsGeneratedHyphyRecoveryCollection(name) &&
                (name.Equals("iqtree_reports", StringComparison.OrdinalIgnoreCase) ||
                 (name.Contains("IQ-TREE", StringComparison.OrdinalIgnoreCase) &&
                  name.Contains("report", StringComparison.OrdinalIgnoreCase))));

            if (string.IsNullOrWhiteSpace(rawCodonCollectionId))
                throw new InvalidOperationException(
                    "No completed codon-alignment collection was found. The prior run must finish back-translation before HyPhy can be restarted.");
            if (string.IsNullOrWhiteSpace(rawTreeCollectionId))
                throw new InvalidOperationException(
                    "No completed IQ-TREE gene-tree collection was found. HyPhy requires one matching tree per codon alignment.");

            _log.LogInformation(
                "Preparing HyPhy retry in history {HistoryId} from raw codon collection {CodonCollection}, raw tree collection {TreeCollection}, and IQ-TREE report collection {ReportCollection}.",
                historyId, rawCodonCollectionId, rawTreeCollectionId, iqtreeReportCollectionId ?? "not found");

            var normalizedNames = GetNormalizedHyphyCollectionNames(
                rawCodonCollectionId, rawTreeCollectionId, iqtreeReportCollectionId);
            var normalizedCodonCollectionId = FindLatestHistoryCollectionId(contents, name =>
                name.Equals(normalizedNames.Codons, StringComparison.OrdinalIgnoreCase));
            var normalizedTreeCollectionId = FindLatestHistoryCollectionId(contents, name =>
                name.Equals(normalizedNames.Trees, StringComparison.OrdinalIgnoreCase));

            string codonCollectionId;
            string treeCollectionId;
            if (!string.IsNullOrWhiteSpace(normalizedCodonCollectionId) &&
                !string.IsNullOrWhiteSpace(normalizedTreeCollectionId) &&
                await NormalizedCollectionsMatchRawAsync(
                    historyId,
                    rawCodonCollectionId,
                    rawTreeCollectionId,
                    normalizedCodonCollectionId,
                    normalizedTreeCollectionId,
                    ct))
            {
                codonCollectionId = normalizedCodonCollectionId;
                treeCollectionId = normalizedTreeCollectionId;
                _log.LogInformation(
                    "Reusing existing validated HyPhy collections {AlignmentCollection} and {TreeCollection} in history {HistoryId}.",
                    codonCollectionId, treeCollectionId, historyId);
            }
            else
            {
                (codonCollectionId, treeCollectionId) = await NormalizeHyphyCollectionsAsync(
                    historyId,
                    rawCodonCollectionId,
                    rawTreeCollectionId,
                    iqtreeReportCollectionId,
                    ct);
            }

            var retryJson = BuildHyphyRetryWorkflowDefinition(LoadSelectionWorkflowDefinition());
            var versioned = VersionWorkflowDefinition(retryJson);
            var workflowId = await _galaxy.EnsureWorkflowImportedAsync(versioned.Name, versioned.Json, ct);

            await _galaxy.UploadTextContentAsync(
                historyId,
                $"HyPhy retry started at {DateTimeOffset.UtcNow:o}; alignments={codonCollectionId}; trees={treeCollectionId}",
                "orthogroup_workflow_invoked_hyphy_retry.txt",
                "txt",
                ct);

            var inputs = new Dictionary<string, object>
            {
                ["0"] = new { src = "hdca", id = codonCollectionId },
                ["1"] = new { src = "hdca", id = treeCollectionId }
            };
            var (invocationId, raw) = await _galaxy.InvokeWorkflowAsync(workflowId, historyId, inputs, ct);
            if (string.IsNullOrWhiteSpace(invocationId))
                throw new InvalidOperationException("Galaxy accepted the HyPhy retry but did not return an invocation ID.");

            _log.LogInformation(
                "Restarted HyPhy in history {HistoryId} using alignment collection {AlignmentCollection} and tree collection {TreeCollection}; invocation {InvocationId}.",
                historyId, codonCollectionId, treeCollectionId, invocationId);
            return new HyphyRetryResult(
                historyId, invocationId, codonCollectionId, treeCollectionId, workflowId, raw);
        }

        private async Task<(string CodonCollectionId, string TreeCollectionId)> NormalizeHyphyCollectionsAsync(
            string historyId,
            string rawCodonCollectionId,
            string rawTreeCollectionId,
            string? iqtreeReportCollectionId,
            CancellationToken ct)
        {
            var codonDetails = await _galaxy.GetCollectionDetailsAsync(historyId, rawCodonCollectionId, ct);
            var treeDetails = await _galaxy.GetCollectionDetailsAsync(historyId, rawTreeCollectionId, ct);
            var codonElements = GetCollectionDatasetElements(codonDetails);
            var treeElements = GetCollectionDatasetElements(treeDetails);
            var reportElements = string.IsNullOrWhiteSpace(iqtreeReportCollectionId)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : GetCollectionDatasetElements(
                    await _galaxy.GetCollectionDetailsAsync(historyId, iqtreeReportCollectionId, ct));
            _log.LogInformation(
                "HyPhy retry source collections contain {CodonCount} codon alignments and {TreeCount} gene trees ({CodonCollection}, {TreeCollection}).",
                codonElements.Count, treeElements.Count, rawCodonCollectionId, rawTreeCollectionId);
            if (codonElements.Count == 0 || treeElements.Count == 0)
                throw new InvalidOperationException("The recovered alignment or tree collection is empty or not ready.");
            if (!codonElements.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(treeElements.Keys))
                throw new InvalidOperationException(
                    $"The recovered collections do not correspond 1:1 ({codonElements.Count} alignments and {treeElements.Count} trees). " +
                    "HyPhy was not started to avoid pairing a gene with the wrong tree.");

            var normalizedCodons = new List<(string name, string datasetId)>();
            var normalizedTrees = new List<(string name, string datasetId)>();
            foreach (var elementIdentifier in codonElements.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                var fasta = await _galaxy.DownloadDatasetAsync(historyId, codonElements[elementIdentifier], ct);
                var newick = await _galaxy.DownloadDatasetAsync(historyId, treeElements[elementIdentifier], ct);
                if (string.IsNullOrWhiteSpace(newick) &&
                    reportElements.TryGetValue(elementIdentifier, out var reportDatasetId))
                {
                    var report = await _galaxy.DownloadDatasetAsync(historyId, reportDatasetId, ct);
                    newick = ExtractNewickFromIqtreeReport(report) ?? string.Empty;
                    _log.LogInformation(
                        "Recovered the Newick tree for {Orthogroup} from IQ-TREE report dataset {ReportDataset} because treefile dataset {TreeDataset} was empty.",
                        elementIdentifier, reportDatasetId, treeElements[elementIdentifier]);
                }
                _log.LogInformation(
                    "Validating HyPhy input pair {Orthogroup}: alignment dataset {AlignmentDataset} ({AlignmentLength} characters), tree dataset {TreeDataset} ({TreeLength} characters).",
                    elementIdentifier,
                    codonElements[elementIdentifier],
                    fasta.Length,
                    treeElements[elementIdentifier],
                    newick.Length);
                HyphyInputNormalizer.NormalizedInputs normalized;
                try
                {
                    normalized = HyphyInputNormalizer.NormalizeAndValidate(fasta, newick);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"HyPhy input validation failed for orthogroup '{elementIdentifier}': {ex.Message}", ex);
                }

                var safeElement = SanitizeName(elementIdentifier);
                var normalizedFastaId = await _galaxy.UploadTextContentAsync(
                    historyId,
                    normalized.Fasta,
                    $"{safeElement}.hyphy.fasta",
                    "fasta",
                    ct);
                var normalizedTreeId = await _galaxy.UploadTextContentAsync(
                    historyId,
                    normalized.Newick,
                    $"{safeElement}.hyphy.nwk",
                    "newick",
                    ct);
                await WaitForDatasetReadyAsync(historyId, normalizedFastaId, ct);
                await WaitForDatasetReadyAsync(historyId, normalizedTreeId, ct);
                normalizedCodons.Add((elementIdentifier, normalizedFastaId));
                normalizedTrees.Add((elementIdentifier, normalizedTreeId));
            }

            var normalizedCodonCollectionId = await _galaxy.CreateListCollectionAsync(
                historyId,
                GetNormalizedHyphyCollectionNames(
                    rawCodonCollectionId, rawTreeCollectionId, iqtreeReportCollectionId).Codons,
                normalizedCodons,
                ct);
            var normalizedTreeCollectionId = await _galaxy.CreateListCollectionAsync(
                historyId,
                GetNormalizedHyphyCollectionNames(
                    rawCodonCollectionId, rawTreeCollectionId, iqtreeReportCollectionId).Trees,
                normalizedTrees,
                ct);
            _log.LogInformation(
                "Normalized and validated {Count} HyPhy alignment/tree pairs in Galaxy history {HistoryId}.",
                normalizedCodons.Count, historyId);
            return (normalizedCodonCollectionId, normalizedTreeCollectionId);
        }

        private static (string Codons, string Trees) GetNormalizedHyphyCollectionNames(
            string rawCodonCollectionId,
            string rawTreeCollectionId,
            string? iqtreeReportCollectionId)
        {
            var source = Encoding.UTF8.GetBytes(
                $"{rawCodonCollectionId}|{rawTreeCollectionId}|{iqtreeReportCollectionId ?? string.Empty}");
            var fingerprint = Convert.ToHexString(SHA256.HashData(source))[..12].ToLowerInvariant();
            return (
                $"{NormalizedHyphyCodonCollectionName} [{fingerprint}]",
                $"{NormalizedHyphyTreeCollectionName} [{fingerprint}]");
        }

        internal static string? ExtractNewickFromIqtreeReport(string report)
        {
            if (string.IsNullOrWhiteSpace(report))
                return null;

            const string heading = "TREE IN NEWICK FORMAT";
            var headingIndex = report.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
            var searchStart = headingIndex >= 0 ? headingIndex + heading.Length : 0;
            var match = Regex.Match(report[searchStart..], @"\([^;]+\);", RegexOptions.Singleline);
            if (!match.Success && searchStart > 0)
                match = Regex.Match(report, @"\([^;]+\);", RegexOptions.Singleline);
            return match.Success ? match.Value.Trim() : null;
        }

        internal static bool IsGeneratedHyphyRecoveryCollection(string name) =>
            name.Contains("normalized", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("HyPhy-ready", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("HyPhy retry", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("BUSTED", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("aBSREL", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("MEME", StringComparison.OrdinalIgnoreCase);

        private async Task<bool> NormalizedCollectionsMatchRawAsync(
            string historyId,
            string rawCodonCollectionId,
            string rawTreeCollectionId,
            string normalizedCodonCollectionId,
            string normalizedTreeCollectionId,
            CancellationToken ct)
        {
            var rawCodons = GetCollectionDatasetElements(
                await _galaxy.GetCollectionDetailsAsync(historyId, rawCodonCollectionId, ct));
            var rawTrees = GetCollectionDatasetElements(
                await _galaxy.GetCollectionDetailsAsync(historyId, rawTreeCollectionId, ct));
            var normalizedCodons = GetCollectionDatasetElements(
                await _galaxy.GetCollectionDetailsAsync(historyId, normalizedCodonCollectionId, ct));
            var normalizedTrees = GetCollectionDatasetElements(
                await _galaxy.GetCollectionDetailsAsync(historyId, normalizedTreeCollectionId, ct));

            if (rawCodons.Count == 0 || rawTrees.Count == 0 ||
                normalizedCodons.Count == 0 || normalizedTrees.Count == 0)
                return false;

            var expected = rawCodons.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return expected.SetEquals(rawTrees.Keys) &&
                   expected.SetEquals(normalizedCodons.Keys) &&
                   expected.SetEquals(normalizedTrees.Keys);
        }

        private static Dictionary<string, string> GetCollectionDatasetElements(JsonElement collection)
        {
            if (collection.ValueKind != JsonValueKind.Object ||
                !collection.TryGetProperty("elements", out var elements) ||
                elements.ValueKind != JsonValueKind.Array)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in elements.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("element_identifier", out var identifierNode))
                    continue;
                var identifier = identifierNode.GetString();
                string? datasetId = null;
                if (element.TryGetProperty("object", out var objectNode) && objectNode.ValueKind == JsonValueKind.Object &&
                    objectNode.TryGetProperty("id", out var objectId))
                    datasetId = objectId.GetString();
                else if (element.TryGetProperty("id", out var elementId))
                    datasetId = elementId.GetString();
                if (!string.IsNullOrWhiteSpace(identifier) && !string.IsNullOrWhiteSpace(datasetId))
                    result[identifier] = datasetId;
            }
            return result;
        }

        private static HashSet<string> GetCollectionElementIdentifiers(JsonElement collection)
        {
            if (collection.ValueKind != JsonValueKind.Object ||
                !collection.TryGetProperty("elements", out var elements) ||
                elements.ValueKind != JsonValueKind.Array)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return elements.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.Object)
                .Select(element => element.TryGetProperty("element_identifier", out var identifier)
                    ? identifier.GetString()
                    : null)
                .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
                .Select(identifier => identifier!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Step 3 alternative: Run selection analysis on ALL orthologous groups (bulk processing)
        /// </summary>
        public async Task<List<SelectionAnalysisResult>> RunSelectionAnalysisOnAllGroupsAsync(
            string historyId,
            string orthoFinderJobId,
            List<GenomeInfo> genomes,
            string? ncbiApiKey,
            string alignment = "mafft",
            CancellationToken ct = default)
        {
            using var apiScope = UseApiKeyForHistory(historyId);

            var outputs = await GetOrthoFinderOutputsAsync(historyId, orthoFinderJobId, ct);
            var filteredId = outputs.SingleCopyDatasetId;
            if (string.IsNullOrWhiteSpace(filteredId))
                throw new InvalidOperationException("Filtered single-copy orthogroups tabular file not found; cannot start workflow.");

            var filteredContent = await _galaxy.DownloadDatasetAsync(historyId, filteredId, ct);
            var groups = ParseHierarchicalSingleCopyOrthogroups(filteredContent, genomes);
            if (groups.Count == 0)
                throw new InvalidOperationException("No complete single-copy orthogroups were found in the filtered OrthoFinder output.");
            return await RunSelectionAnalysisAsync(historyId, groups.ToList(), genomes, ncbiApiKey, alignment, ct);
        }

        /// <summary>
        /// Build protein multi-FASTA files and MAFFT alignments for each orthogroup
        /// using a Galaxy workflow and dataset collections to minimize orchestration
        /// work on the application server.
        /// </summary>

        /// <summary>
        /// Create a Galaxy list collection of ID tables, one per orthogroup.
        /// Each element is a 1-column tabular dataset with one gene ID per line.
        /// </summary>
        private async Task<string> CreateOrthogroupIdCollectionAsync(
            string historyId,
            IReadOnlyList<OrthologousGroup> groups,
            CancellationToken ct)
        {
            // Reuse existing upload if present
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind == JsonValueKind.Array)
                {
                    var existingZip = contents.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.Object)
                        .FirstOrDefault(e =>
                            e.TryGetProperty("name", out var nameProp) &&
                            string.Equals(nameProp.GetString(), "orthogroup_id_tables.zip", StringComparison.OrdinalIgnoreCase) &&
                            e.TryGetProperty("deleted", out var delProp) && delProp.ValueKind == JsonValueKind.False);

                    if (existingZip.ValueKind == JsonValueKind.Object &&
                        existingZip.TryGetProperty("id", out var existingIdProp))
                    {
                        var existingZipId = existingIdProp.GetString();
                        if (!string.IsNullOrWhiteSpace(existingZipId))
                        {
                            _log.LogInformation("Reusing existing orthogroup ID tables zip {DatasetId} in history {HistoryId}", existingZipId, historyId);
                            var unzipJob = await StartUnzipJobAsync(historyId, existingZipId, "*.tabular", ct);
                            if (!string.IsNullOrWhiteSpace(unzipJob.JobId))
                                await WaitForJobCompletionAsync(unzipJob.JobId, ct);
                            return unzipJob.CollectionId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Failed to reuse existing orthogroup_id_tables.zip; regenerating.");
            }

            int processed = 0;
            using var zipStream = new MemoryStream();
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true);

            foreach (var group in groups)
            {
                var allIds = new List<string>();

                if (group.SpeciesGenes != null)
                {
                    foreach (var kvp in group.SpeciesGenes)
                    {
                        var raw = kvp.Value;
                        if (string.IsNullOrWhiteSpace(raw))
                            continue;

                        var splitIds = raw
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(id => !string.IsNullOrWhiteSpace(id));

                        allIds.AddRange(splitIds);
                    }
                }

                var distinctIds = allIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (distinctIds.Count == 0)
                {
                    _log.LogDebug("Orthogroup {GroupId} has no gene IDs; skipping ID-table creation.", group.GroupId);
                    continue;
                }

                var idTableContent = string.Join('\n', distinctIds) + "\n";
                var entryName = $"{SanitizeName(group.GroupId)}_ids.tabular";
                var entry = zip.CreateEntry(entryName);
                await using (var writer = new StreamWriter(entry.Open()))
                {
                    await writer.WriteAsync(idTableContent);
                }

                processed++;
            }

            if (processed == 0)
                throw new InvalidOperationException("No ID tables were created for orthogroups; cannot build collection.");

            zip.Dispose();
            var zipBytes = zipStream.ToArray();
            _log.LogInformation("Uploading orthogroup ID tables zip ({Bytes} bytes, {Count} files) for collection creation", zipBytes.Length, processed);

            var zipDatasetId = await _galaxy.UploadBinaryContentAsync(historyId, zipBytes, "orthogroup_id_tables.zip", "zip", ct);

            // Unzip within Galaxy to get a collection of tabular files
            var createdUnzipJob = await StartUnzipJobAsync(historyId, zipDatasetId, "*.tabular", ct);
            if (!string.IsNullOrWhiteSpace(createdUnzipJob.JobId))
                await WaitForJobCompletionAsync(createdUnzipJob.JobId, ct);

            _log.LogInformation("Created ID-table collection via unzip {CollectionId} from zip dataset {ZipDatasetId}", createdUnzipJob.CollectionId, zipDatasetId);
            return createdUnzipJob.CollectionId;
        }

        public sealed record AlignedOrthogroupResult(
            string OrthogroupId,
            string ProteinMultiFastaId,
            string CdsMultiFastaId,
            string AlignedProteinId
        );

        private string ExtractSequenceFromFasta(string fastaContent, string geneId)
        {
            var lines = fastaContent.Split('\n');
            var sb = new StringBuilder();
            bool capturing = false;

            foreach (var line in lines)
            {
                if (line.StartsWith(">"))
                {
                    // Check if this header matches the gene ID we're looking for
                    if (line.Contains(geneId, StringComparison.OrdinalIgnoreCase))
                    {
                        capturing = true;
                        sb.AppendLine(line); // Include the header
                    }
                    else
                    {
                        // If we were capturing and hit a new header, we're done
                        if (capturing)
                            break;
                    }
                }
                else if (capturing)
                {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString().Trim();
        }

        private string CreateMultiFasta(Dictionary<string, string> sequences)
        {
            var sb = new StringBuilder();
            foreach (var kvp in sequences)
            {
                sb.Append(kvp.Value); // Sequence already includes header
                if (!kvp.Value.EndsWith("\n"))
                    sb.AppendLine();
            }
            return sb.ToString();
        }

        private async Task<SelectionAnalysisResult> RunDnDsPipelineAsync(
            string historyId, 
            List<(string Species, string DatasetId)> cdsSequences, 
            string alignment, 
            string groupId, 
            CancellationToken ct)
        {
            // Standard dN/dS pipeline adapted from GalaxyBustedPipeline

            // 1) Merge FASTA files into one multi-fasta
            string multiFastaId = await MergeFastaAsync(historyId, cdsSequences.Select(c => c.DatasetId).ToList(), ct);

            // 2) Translate CDS to amino acids
            string aaMultiId = await TranslateAsync(historyId, multiFastaId, ct);

            // 3) Align protein sequences
            string aaAlignedId = await AlignAsync(historyId, aaMultiId, alignment, ct);

            // 4) Back-translate to codon alignment
            string codonAlignedId = await AlignBackTransAsync(historyId, aaAlignedId, multiFastaId, ct);

            // 5) Run HYPHY BUSTED
            var (jobId, raw) = await RunBustedAsync(historyId, codonAlignedId, ct);

            var stepDatasets = new Dictionary<string, string>
            {
                ["multi_fasta"] = multiFastaId,
                ["aa_multi"] = aaMultiId,
                ["aa_aligned"] = aaAlignedId,
                ["codon_aligned"] = codonAlignedId,
                ["group_id"] = groupId
            };

            return new SelectionAnalysisResult(historyId, codonAlignedId, jobId, raw, stepDatasets);
        }

        private async Task<SelectionAnalysisResult> RunOrthogroupSelectionPipelineAsync(
            string historyId,
            OrthologousGroup group,
            List<GenomeInfo> genomes,
            string alignment,
            CancellationToken ct)
        {
            var proteinSequences = new List<string>();
            var cdsSequences = new List<string>();

            foreach (var genome in genomes)
            {
                if (!group.SpeciesGenes.TryGetValue(genome.Species, out var genesRaw))
                    continue;

                var geneId = genesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(geneId))
                    continue;

                proteinSequences.Add(await ExtractSequenceStrictAsync(
                    historyId, genome.ProteinDatasetId, geneId, group.GroupId, genome.Species, "protein", ct));
                cdsSequences.Add(await ExtractSequenceStrictAsync(
                    historyId, genome.CdsDatasetId, geneId, group.GroupId, genome.Species, "CDS", ct));
            }

            if (proteinSequences.Count < 3 || proteinSequences.Count != cdsSequences.Count)
                throw new InvalidOperationException($"{group.GroupId}: expected matching protein and CDS sequences for at least three species.");

            var safeGroup = SanitizeName(group.GroupId);
            var proteinMulti = await MergeFastaAsync(historyId, proteinSequences, ct);
            await WaitForDatasetReadyAsync(historyId, proteinMulti, ct);
            await ValidateFastaDatasetAsync(historyId, proteinMulti, group.GroupId, "protein input", 3, ct);
            await _galaxy.RenameHistoryContentAsync(historyId, proteinMulti, $"{safeGroup} - protein sequences", ct);

            var cdsMulti = await MergeFastaAsync(historyId, cdsSequences, ct);
            await WaitForDatasetReadyAsync(historyId, cdsMulti, ct);
            await ValidateFastaDatasetAsync(historyId, cdsMulti, group.GroupId, "CDS input", 3, ct);
            await _galaxy.RenameHistoryContentAsync(historyId, cdsMulti, $"{safeGroup} - CDS sequences", ct);
            await _galaxy.CreateListCollectionAsync(historyId, $"{safeGroup} - sequence inputs", new()
            {
                ("proteins", proteinMulti),
                ("cds", cdsMulti)
            }, ct);

            var proteinAlignment = await AlignAsync(historyId, proteinMulti, alignment, ct);
            await ValidateFastaDatasetAsync(historyId, proteinAlignment, group.GroupId, "protein alignment", 3, ct);
            await _galaxy.RenameHistoryContentAsync(historyId, proteinAlignment, $"{safeGroup} - {alignment.ToUpperInvariant()} protein alignment", ct);

            var codonAlignment = await AlignBackTransAsync(historyId, proteinAlignment, cdsMulti, ct);
            await WaitForDatasetReadyAsync(historyId, codonAlignment, ct);
            await _galaxy.RenameHistoryContentAsync(historyId, codonAlignment, $"{safeGroup} - codon alignment", ct);

            var tree = await RunGeneTreeAsync(historyId, proteinAlignment, ct);
            await _galaxy.RenameHistoryContentAsync(historyId, tree.OutputDatasetId, $"{safeGroup} - gene tree", ct);

            var busted = await RunHyphyMethodAsync(historyId, "busted", _opts.DnDsToolId, codonAlignment, tree.OutputDatasetId, ct);
            var absrel = await RunHyphyMethodAsync(historyId, "absrel", null, codonAlignment, tree.OutputDatasetId, ct);
            var meme = await RunHyphyMethodAsync(historyId, "meme", null, codonAlignment, tree.OutputDatasetId, ct);

            await RenameToolOutputAsync(historyId, busted.OutputDatasetId, $"{safeGroup} - BUSTED results", ct);
            await RenameToolOutputAsync(historyId, absrel.OutputDatasetId, $"{safeGroup} - aBSREL results", ct);
            await RenameToolOutputAsync(historyId, meme.OutputDatasetId, $"{safeGroup} - MEME results", ct);

            var steps = new Dictionary<string, string>
            {
                ["group_id"] = group.GroupId,
                ["protein_multi_fasta"] = proteinMulti,
                ["protein_alignment"] = proteinAlignment,
                ["cds_multi_fasta"] = cdsMulti,
                ["codon_aligned"] = codonAlignment,
                ["gene_tree"] = tree.OutputDatasetId,
                ["gene_tree_job"] = tree.JobId,
                ["busted_output"] = busted.OutputDatasetId,
                ["busted_job"] = busted.JobId,
                ["absrel_output"] = absrel.OutputDatasetId,
                ["absrel_job"] = absrel.JobId,
                ["meme_output"] = meme.OutputDatasetId,
                ["meme_job"] = meme.JobId
            };

            return new SelectionAnalysisResult(historyId, codonAlignment, busted.JobId, busted.Raw, steps);
        }

        private sealed record FastaRecord(string Header, string Sequence);

        private static readonly IReadOnlyDictionary<string, char> StandardCodonTable =
            new Dictionary<string, char>(StringComparer.Ordinal)
            {
                ["TTT"]='F',["TTC"]='F',["TTA"]='L',["TTG"]='L',["TCT"]='S',["TCC"]='S',["TCA"]='S',["TCG"]='S',
                ["TAT"]='Y',["TAC"]='Y',["TAA"]='*',["TAG"]='*',["TGT"]='C',["TGC"]='C',["TGA"]='*',["TGG"]='W',
                ["CTT"]='L',["CTC"]='L',["CTA"]='L',["CTG"]='L',["CCT"]='P',["CCC"]='P',["CCA"]='P',["CCG"]='P',
                ["CAT"]='H',["CAC"]='H',["CAA"]='Q',["CAG"]='Q',["CGT"]='R',["CGC"]='R',["CGA"]='R',["CGG"]='R',
                ["ATT"]='I',["ATC"]='I',["ATA"]='I',["ATG"]='M',["ACT"]='T',["ACC"]='T',["ACA"]='T',["ACG"]='T',
                ["AAT"]='N',["AAC"]='N',["AAA"]='K',["AAG"]='K',["AGT"]='S',["AGC"]='S',["AGA"]='R',["AGG"]='R',
                ["GTT"]='V',["GTC"]='V',["GTA"]='V',["GTG"]='V',["GCT"]='A',["GCC"]='A',["GCA"]='A',["GCG"]='A',
                ["GAT"]='D',["GAC"]='D',["GAA"]='E',["GAG"]='E',["GGT"]='G',["GGC"]='G',["GGA"]='G',["GGG"]='G'
            };

        private static List<FastaRecord> ParseFastaRecords(string fasta)
        {
            var records = new List<FastaRecord>();
            string? header = null;
            var sequence = new StringBuilder();
            foreach (var rawLine in fasta.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith('>'))
                {
                    if (header is not null) records.Add(new FastaRecord(header, sequence.ToString()));
                    header = line;
                    sequence.Clear();
                }
                else if (header is not null && line.Length > 0)
                {
                    sequence.Append(line);
                }
            }
            if (header is not null) records.Add(new FastaRecord(header, sequence.ToString()));
            return records;
        }

        private static string WriteFastaRecords(IEnumerable<FastaRecord> records) =>
            string.Concat(records.Select(record => $"{record.Header}\n{record.Sequence}\n"));

        private static string? HeaderTag(string header, string tag)
        {
            var match = Regex.Match(header, $@"\[{Regex.Escape(tag)}=([^\]]+)\]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private static string ProteinAccession(string header)
        {
            var match = Regex.Match(header, @"\b[A-Z]{2,}_[0-9]+(?:\.[0-9]+)?\b", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value;
            return header.TrimStart('>').Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        }

        private static FastaRecord NormalizeFastaPrimaryId(FastaRecord record, string primaryId)
        {
            var annotation = record.Header.TrimStart('>');
            var firstSpace = annotation.IndexOf(' ');
            annotation = firstSpace >= 0 ? annotation[(firstSpace + 1)..].Trim() : string.Empty;
            var header = string.IsNullOrWhiteSpace(annotation)
                ? $">{primaryId}"
                : $">{primaryId} {annotation}";
            return new FastaRecord(header, record.Sequence);
        }

        private async Task<(string ProteinDatasetId, string CdsDatasetId)> FilterLongestIsoformPairsAsync(
            string historyId, string species, string proteinDatasetId, string cdsDatasetId, CancellationToken ct)
        {
            var proteinFasta = await _galaxy.DownloadDatasetAsync(historyId, proteinDatasetId, ct);
            var cdsFasta = await _galaxy.DownloadDatasetAsync(historyId, cdsDatasetId, ct);
            var selection = SelectLongestIsoformPairs(proteinFasta, cdsFasta);
            EnsureIsoformSelectionIsUsable(selection, species);

            var safeSpecies = SanitizeName(species);
            var filteredProteinId = await _galaxy.UploadTextContentAsync(
                historyId, selection.Proteins, $"{safeSpecies} - longest protein isoform per gene", "fasta", ct);
            var filteredCdsId = await _galaxy.UploadTextContentAsync(
                historyId, selection.Cds, $"{safeSpecies} - matching CDS isoforms", "fasta", ct);
            await WaitForDatasetReadyAsync(historyId, filteredProteinId, ct);
            await WaitForDatasetReadyAsync(historyId, filteredCdsId, ct);
            _log.LogInformation("Isoform filter retained {Count} matched protein/CDS pairs for {Species} from {CdsCount} CDS records.",
                selection.MatchedPairs, species, selection.TotalCdsRecords);
            if (selection.TranslationInvalidPairs > 0)
            {
                _log.LogWarning(
                    "Excluded {Count} protein/CDS pair(s) for {Species} because the CDS did not translate to the annotated protein.",
                    selection.TranslationInvalidPairs,
                    species);
            }
            if (selection.UnmatchedProteinPairs > 0)
            {
                _log.LogWarning(
                    "Excluded {Count} CDS gene(s) for {Species} because the corresponding protein accession was absent from the NCBI protein FASTA.",
                    selection.UnmatchedProteinPairs,
                    species);
            }
            return (filteredProteinId, filteredCdsId);
        }

        internal sealed record IsoformSelection(
            string Proteins,
            string Cds,
            int MatchedPairs,
            int GenesWithIsoforms,
            int TotalCdsRecords,
            int TranslationInvalidPairs,
            int UnmatchedProteinPairs);

        internal static void EnsureIsoformSelectionIsUsable(IsoformSelection selection, string species)
        {
            if (selection.TotalCdsRecords == 0)
                throw new InvalidOperationException($"The CDS FASTA for {species} contains no records.");
            if (selection.GenesWithIsoforms == 0)
                throw new InvalidOperationException(
                    $"Isoform filtering could not find any CDS records with both [gene] (or [locus_tag]) and [protein_id] tags for {species}. " +
                    "OrthoFinder was not started with the unfiltered proteome.");
            if (selection.MatchedPairs == 0)
                throw new InvalidOperationException(
                    $"Isoform filtering found no valid matched protein/CDS pairs for {species}. " +
                    "OrthoFinder was not started with an empty proteome.");
            if (string.IsNullOrWhiteSpace(selection.Proteins) || string.IsNullOrWhiteSpace(selection.Cds))
                throw new InvalidOperationException($"Isoform filtering produced an empty FASTA dataset for {species}.");
        }

        internal static IsoformSelection SelectLongestIsoformPairs(string proteinFasta, string cdsFasta)
        {
            var proteins = ParseFastaRecords(proteinFasta);
            var cdsRecords = ParseFastaRecords(cdsFasta);

            var candidatesByGene = cdsRecords
                .Select(record => new
                {
                    Record = record,
                    Gene = HeaderTag(record.Header, "gene") ?? HeaderTag(record.Header, "locus_tag"),
                    ProteinId = HeaderTag(record.Header, "protein_id")
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Gene) && !string.IsNullOrWhiteSpace(item.ProteinId))
                .GroupBy(item => item.Gene!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var proteinByAccession = proteins
                .GroupBy(record => ProteinAccession(record.Header), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var proteinByCore = proteins
                .GroupBy(record => Regex.Replace(ProteinAccession(record.Header), @"\.\d+$", string.Empty), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var selectedCds = new List<FastaRecord>();
            var selectedProteins = new List<FastaRecord>();
            var translationInvalidPairs = 0;
            var unmatchedProteinPairs = 0;
            foreach (var geneGroup in candidatesByGene.OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var foundMatchedProtein = false;
                var selected = false;
                foreach (var item in geneGroup
                    .OrderByDescending(item => item.Record.Sequence.Count(char.IsLetter))
                    .ThenByDescending(item => item.ProteinId, StringComparer.Ordinal))
                {
                    var proteinId = item.ProteinId!;
                    if (!proteinByAccession.TryGetValue(proteinId, out var protein))
                        proteinByCore.TryGetValue(Regex.Replace(proteinId, @"\.\d+$", string.Empty), out protein);
                    if (protein is null) continue;

                    foundMatchedProtein = true;
                    if (!CdsTranslatesToProtein(item.Record.Sequence, protein.Sequence)) continue;

                    // Back-translation tools join protein and CDS records by the
                    // primary FASTA identifier, not by NCBI's [protein_id] tag.
                    var matchedProteinId = ProteinAccession(protein.Header);
                    selectedCds.Add(NormalizeFastaPrimaryId(item.Record, matchedProteinId));
                    selectedProteins.Add(NormalizeFastaPrimaryId(protein, matchedProteinId));
                    selected = true;
                    break;
                }

                if (foundMatchedProtein && !selected) translationInvalidPairs++;
                if (!foundMatchedProtein) unmatchedProteinPairs++;
            }

            return new IsoformSelection(
                WriteFastaRecords(selectedProteins), WriteFastaRecords(selectedCds),
                selectedProteins.Count, candidatesByGene.Count, cdsRecords.Count,
                translationInvalidPairs, unmatchedProteinPairs);
        }

        internal static bool CdsTranslatesToProtein(string cds, string protein)
        {
            var nucleotides = new string((cds ?? string.Empty)
                .Where(char.IsLetter)
                .Select(char.ToUpperInvariant)
                .Select(value => value == 'U' ? 'T' : value)
                .ToArray());
            var aminoAcids = new string((protein ?? string.Empty)
                .Where(value => char.IsLetter(value) || value == '*')
                .Select(char.ToUpperInvariant)
                .ToArray())
                .TrimEnd('*');

            if (nucleotides.Length == 0 || nucleotides.Length % 3 != 0 || aminoAcids.Length == 0)
                return false;

            var translated = new StringBuilder(nucleotides.Length / 3);
            for (var index = 0; index < nucleotides.Length; index += 3)
            {
                var codon = nucleotides.Substring(index, 3);
                translated.Append(StandardCodonTable.TryGetValue(codon, out var residue) ? residue : 'X');
            }

            var translatedProtein = translated.ToString().TrimEnd('*');
            if (translatedProtein.Contains('*') || translatedProtein.Length != aminoAcids.Length)
                return false;

            for (var index = 0; index < aminoAcids.Length; index++)
            {
                if (aminoAcids[index] != 'X' && translatedProtein[index] != 'X' && aminoAcids[index] != translatedProtein[index])
                    return false;
            }
            return true;
        }

        private async Task<string> ExtractSequenceStrictAsync(
            string historyId, string sourceDatasetId, string sequenceId, string groupId,
            string species, string sequenceKind, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sourceDatasetId))
                throw new InvalidOperationException($"{groupId}: {species} has no {sequenceKind} FASTA dataset.");

            const string toolId = "toolshed.g2.bx.psu.edu/repos/peterjc/seq_filter_by_id/seq_filter_by_id/0.2.9";
            var inputs = new Dictionary<string, object>
            {
                ["input_file"] = new { src = "hda", id = sourceDatasetId },
                ["filter_type"] = "list",
                ["id_list"] = sequenceId,
                ["sense"] = "pos",
                ["id_opts|columns"] = "1"
            };
            var (jobId, raw) = await _galaxy.InvokeToolAsync(historyId, toolId, inputs, ct);
            var outputId = await ResolveOutputDatasetId(historyId, raw, ct);
            await WaitForJobCompletionAsync(jobId, ct);
            var content = await _galaxy.DownloadDatasetAsync(historyId, outputId, ct);
            if (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith('>'))
            {
                // NCBI records can put the accession in a pipe-delimited ID or
                // a [protein_id=...] header field instead of the first FASTA token.
                var source = await _galaxy.DownloadDatasetAsync(historyId, sourceDatasetId, ct);
                var matchedRecord = FindFastaRecordByHeaderToken(source, sequenceId);
                if (!string.IsNullOrWhiteSpace(matchedRecord))
                {
                    outputId = await _galaxy.UploadTextContentAsync(
                        historyId, matchedRecord,
                        $"{SanitizeName(groupId)} - {SanitizeName(species)} - {sequenceKind}", "fasta", ct);
                    content = matchedRecord;
                }
            }
            if (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith('>'))
                throw new InvalidOperationException($"{groupId}: Galaxy could not find {sequenceKind} '{sequenceId}' for {species}.");

            // Back-translation pairs records by FASTA identifier. NCBI protein and CDS
            // headers can use different accessions, so assign the same stable species ID.
            var normalized = NormalizeSingleFastaRecord(content, species);
            outputId = await _galaxy.UploadTextContentAsync(
                historyId, normalized,
                $"{SanitizeName(groupId)} - {SanitizeName(species)} - {sequenceKind}", "fasta", ct);
            await _galaxy.RenameHistoryContentAsync(historyId, outputId,
                $"{SanitizeName(groupId)} - {SanitizeName(species)} - {sequenceKind}", ct);
            return outputId;
        }

        private static string NormalizeSingleFastaRecord(string fasta, string species)
        {
            var lines = fasta.Replace("\r\n", "\n").Split('\n');
            var sequence = string.Concat(lines.Skip(1).Select(line => line.Trim()))
                .Replace(" ", string.Empty);
            if (string.IsNullOrWhiteSpace(sequence))
                throw new InvalidOperationException($"Extracted FASTA record for {species} has no sequence data.");
            return $">{SanitizeName(species)}\n{sequence}\n";
        }

        private async Task ValidateFastaDatasetAsync(
            string historyId, string datasetId, string groupId, string stage,
            int minimumRecords, CancellationToken ct)
        {
            await WaitForDatasetReadyAsync(historyId, datasetId, ct);
            var content = await _galaxy.DownloadDatasetAsync(historyId, datasetId, ct);
            var records = Regex.Matches(content ?? string.Empty, @"(?m)^>").Count;
            var sequenceCharacters = Regex.Replace(content ?? string.Empty, @"(?m)^>.*$|\s", string.Empty).Length;
            if (records < minimumRecords || sequenceCharacters == 0)
                throw new InvalidOperationException(
                    $"{groupId}: Galaxy produced an empty or incomplete {stage} " +
                    $"({records} FASTA records). Back-translation was not submitted.");
        }

        private static string? FindFastaRecordByHeaderToken(string fasta, string token)
        {
            if (string.IsNullOrWhiteSpace(fasta) || string.IsNullOrWhiteSpace(token)) return null;
            var normalizedToken = token.Trim();
            var versionlessToken = Regex.Replace(normalizedToken, @"\.\d+$", string.Empty);
            var records = fasta.Replace("\r\n", "\n").Split('\n');
            StringBuilder? match = null;
            foreach (var line in records)
            {
                if (line.StartsWith('>'))
                {
                    if (match is { Length: > 0 }) return match.ToString();
                    var headerMatches = line.Contains(normalizedToken, StringComparison.OrdinalIgnoreCase) ||
                        (!versionlessToken.Equals(normalizedToken, StringComparison.Ordinal) &&
                         Regex.IsMatch(line, $@"(?<![A-Za-z0-9_]){Regex.Escape(versionlessToken)}(?:\.\d+)?(?![A-Za-z0-9_])", RegexOptions.IgnoreCase));
                    match = headerMatches
                        ? new StringBuilder().AppendLine(line)
                        : null;
                }
                else if (match is not null)
                {
                    match.AppendLine(line);
                }
            }
            return match is { Length: > 0 } ? match.ToString() : null;
        }

        private async Task<ToolRun> RunGeneTreeAsync(string historyId, string proteinAlignmentId, CancellationToken ct)
        {
            var toolId = await ResolveToolIdAsync(new[] { "fasttree" }, ct)
                ?? await ResolveToolIdAsync(new[] { "iqtree" }, ct)
                ?? throw new InvalidOperationException("No Galaxy gene-tree tool (FastTree or IQ-TREE) is installed.");
            return await RunSingleInputToolAsync(historyId, toolId, proteinAlignmentId, null, ct);
        }

        private async Task<ToolRun> RunHyphyMethodAsync(
            string historyId, string methodName, string? configuredToolId,
            string alignmentId, string treeId, CancellationToken ct)
        {
            var toolId = configuredToolId
                ?? await ResolveToolIdAsync(new[] { $"hyphy {methodName}" }, ct)
                ?? await ResolveToolIdAsync(new[] { methodName }, ct)
                ?? throw new InvalidOperationException($"Galaxy does not have the HyPhy {methodName.ToUpperInvariant()} tool installed.");
            return await RunSingleInputToolAsync(historyId, toolId, alignmentId, treeId, ct);
        }

        private async Task<ToolRun> RunSingleInputToolAsync(
            string historyId, string toolId, string primaryDatasetId, string? treeDatasetId, CancellationToken ct)
        {
            var build = await _galaxy.GetToolBuildAsync(toolId, historyId, ct);
            var inputNodes = GetInputsArrayOrThrow(build, toolId);
            var dataParams = FindDataParamsRecursive(inputNodes).Distinct().ToList();
            if (dataParams.Count == 0)
                throw new InvalidOperationException($"Galaxy tool {toolId} exposes no dataset input.");

            var inputs = new Dictionary<string, object>();
            var primaryParam = dataParams.FirstOrDefault(p =>
                p.Contains("alignment", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("input", StringComparison.OrdinalIgnoreCase)) ?? dataParams[0];
            inputs[primaryParam] = new { src = "hda", id = primaryDatasetId };

            if (!string.IsNullOrWhiteSpace(treeDatasetId))
            {
                var treeParam = dataParams.FirstOrDefault(p => p.Contains("tree", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(treeParam) && !inputs.ContainsKey(treeParam))
                    inputs[treeParam] = new { src = "hda", id = treeDatasetId };
            }

            var (jobId, raw) = await _galaxy.InvokeToolAsync(historyId, toolId, inputs, ct);
            var outputId = await ResolveOutputDatasetId(historyId, raw, ct);
            await WaitForJobCompletionAsync(jobId, ct);
            return new ToolRun(jobId, outputId, raw);
        }

        private async Task CreateSelectionOutputCollectionsAsync(
            string historyId, IReadOnlyList<SelectionAnalysisResult> results, CancellationToken ct)
        {
            var stages = new[]
            {
                (Key: "protein_multi_fasta", Name: "phyloDIVaS - orthogroup protein inputs"),
                (Key: "cds_multi_fasta", Name: "phyloDIVaS - orthogroup CDS inputs"),
                (Key: "protein_alignment", Name: "phyloDIVaS - protein alignments"),
                (Key: "codon_aligned", Name: "phyloDIVaS - codon alignments"),
                (Key: "gene_tree", Name: "phyloDIVaS - gene trees"),
                (Key: "busted_output", Name: "phyloDIVaS - BUSTED results"),
                (Key: "absrel_output", Name: "phyloDIVaS - aBSREL results"),
                (Key: "meme_output", Name: "phyloDIVaS - MEME results")
            };
            foreach (var stage in stages)
            {
                var methodPrefix = stage.Key switch
                {
                    "busted_output" => "BUSTED",
                    "absrel_output" => "aBSREL",
                    "meme_output" => "MEME",
                    _ => string.Empty
                };
                var elements = results
                    .Where(r => r.StepDatasetIds.ContainsKey(stage.Key))
                    .Select(r => ($"{methodPrefix}{(string.IsNullOrEmpty(methodPrefix) ? string.Empty : " - ")}{r.StepDatasetIds["group_id"]}", r.StepDatasetIds[stage.Key]))
                    .ToList();
                if (elements.Count > 0)
                    await _galaxy.CreateListCollectionAsync(historyId, stage.Name, elements, ct);
            }
        }

        private async Task WaitForDatasetReadyAsync(string historyId, string datasetId, CancellationToken ct)
        {
            // Never poll a request-scoped history snapshot: an upload first seen
            // as 'new' would remain cached in that state for the entire request.
            await WaitForDatasetReadyAsync(datasetId, ct, maxAttempts: 1800, delayMs: 3000);
        }

        private async Task RenameToolOutputAsync(string historyId, string datasetId, string name, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(datasetId))
                await _galaxy.RenameHistoryContentAsync(historyId, datasetId, name, ct);
        }

        private async Task<(string jobId, JsonElement raw)> RunOrthoFinderAsync(
            string historyId,
            List<string> genomeDatasetIds,
            string orthoFinderToolId,
            CancellationToken ct)
        {
            var build = await _galaxy.GetToolBuildAsync(orthoFinderToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, orthoFinderToolId);

            // Make sure datatypes are correct
            _log.LogInformation("Setting datatype to 'fasta' for {Count} protein datasets", genomeDatasetIds.Count);
            foreach (var datasetId in genomeDatasetIds)
            {
                try
                {
                    await _galaxy.UpdateDatasetDatatypeAsync(datasetId, "fasta", ct);
                }
                catch (GalaxyHttpException ghe) when (ghe.StatusCode == 400)
                {
                    _log.LogDebug(ghe, "Skipping datatype update for dataset {DatasetId} because Galaxy reported it is locked or in use.", datasetId);
                }
                catch (Exception updEx)
                {
                    _log.LogDebug(updEx, "Failed to update datatype for dataset {DatasetId}; continuing with existing datatype.", datasetId);
                }
            }

            // Build the list of dataset refs for the multi=true param
            var fastaDatasets = genomeDatasetIds
                .Select(id => (object)new Dictionary<string, object>
                {
                    ["src"] = "hda",
                    ["id"]  = id
                })
                .ToArray();

            // Use *flattened* parameter names that match Galaxy's error path
            var inputs = new Dictionary<string, object>
            {
                // --- INIT SECTION ---
                ["init|start"] = "fasta",
                ["init|input_fasta"] = fastaDatasets,
                ["init|input_type"] = "",
                ["init|search|search_program"] = "diamond",

                // --- TREES / OUTPUT SECTION ---
                // important: -og stops after orthogroups (faster + you don't need gene trees)
                ["trees|run_mode"] = "full",

                // the key part: include "orthogroups" in the output selector
                ["trees|output_selector"] = new[]
                {
                    "orthogroups",                  // Orthogroups.tsv (deprecated, but still works)
                    "species_tree",                 // <--- REQUIRED
                    "species_tree_node_labels",     // optional
                    "phylogenetic_hierarchical_orthogroups",
                    "orthologues",
                    "specs_overlap",
                    "stat_overall",
                    "stat_specs"
                },

                // inflation parameter (required)
                ["inflation"] = "1.5"
            };

            _log.LogInformation("OrthoFinder inputs JSON: {Json}", JsonSerializer.Serialize(inputs));

            return await _galaxy.InvokeToolAsync(historyId, orthoFinderToolId, inputs, ct);
        }

        /// <summary>
        /// Create a merged collection from multiple unzip output collections
        /// </summary>
        private async Task<string> CreateMergedCollectionAsync(
            string historyId, 
            string collectionName, 
            List<string> collectionIds, 
            CancellationToken ct)
        {
            // For each collection, we need to extract the actual FASTA dataset
            var datasetIds = new Dictionary<string, string>();
            
            for (int i = 0; i < collectionIds.Count; i++)
            {
                var collectionId = collectionIds[i];
                // Get the first dataset from each collection (the extracted FASTA file)
                var datasetId = await GetFirstDatasetFromCollectionAsync(collectionId, ct);
                datasetIds[$"genome_{i}"] = datasetId;
            }
            
            // Create a new collection with all the FASTA datasets
            return await _galaxy.CreateDatasetCollectionAsync(historyId, collectionName, datasetIds, ct);
        }
        
        /// <summary>
        /// Get the first dataset ID from a collection
        /// </summary>
        private async Task<string> GetFirstDatasetFromCollectionAsync(string collectionId, CancellationToken ct)
        {
            var collection = await _galaxy.GetCollectionDetailsAsync(null!, collectionId, ct);
            
            if (collection.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in elements.EnumerateArray())
                {
                    if (element.TryGetProperty("object", out var obj) && 
                        obj.TryGetProperty("id", out var id))
                    {
                        return id.GetString() ?? throw new InvalidOperationException("Dataset ID was null");
                    }
                }
            }
            
            throw new InvalidOperationException($"Could not find any datasets in collection {collectionId}");
        }

        /// <summary>
        /// Run OrthoFinder with a collection as input (collection contains outputs from unzip jobs)
        /// </summary>
        private async Task<(string jobId, JsonElement raw)> RunOrthoFinderWithCollectionAsync(
            string historyId, 
            string collectionId, 
            string orthoFinderToolId, 
            CancellationToken ct)
        {
            var build = await _galaxy.GetToolBuildAsync(orthoFinderToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, orthoFinderToolId);
            
            // Find the input parameter for the genome collection
            var collectionParam = FindCollectionInputParam(inputsNode) ?? "input_collection";
            
            var inputs = new Dictionary<string, object>
            {
                [collectionParam] = new { src = "hdca", id = collectionId }
            };

            return await _galaxy.InvokeToolAsync(historyId, orthoFinderToolId, inputs, ct);
        }

        private async Task<string> FilterFastaByIdsAsync(string historyId, string genomeDatasetId, IEnumerable<string> geneIds, string species, CancellationToken ct)
        {
            // Filter protein FASTA sequences using seq_filter_by_id (forced tool to avoid grep heuristics)
            const string seqFilterToolId = "toolshed.g2.bx.psu.edu/repos/peterjc/seq_filter_by_id/seq_filter_by_id/0.2.9";

            var idList = (geneIds ?? Array.Empty<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (idList.Count == 0)
                throw new InvalidOperationException($"No gene IDs provided to filter FASTA for {species}");

            var inputs = new Dictionary<string, object>
            {
                ["input1"] = new { src = "hda", id = genomeDatasetId },
                ["filter_type"] = "list",
                ["id_list"] = string.Join("\n", idList),
                ["sense"] = "pos",
                ["id_opts|columns"] = "1",
                ["output_choice_cond|output_choice"] = "pos" // Just positive matches (ID on list), as a single file
            };

            try
            {
                _log.LogDebug("Filtering FASTA for {Species} using {Count} gene IDs with seq_filter_by_id", species, idList.Count);
                var (jobId, raw) = await _galaxy.InvokeToolAsync(historyId, seqFilterToolId, inputs, ct);
                var outputId = await ResolveOutputDatasetId(historyId, raw, ct);
                if (!string.IsNullOrWhiteSpace(jobId))
                    await WaitForJobCompletionAsync(jobId, ct);
                return outputId;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to filter FASTA for {Species} using seq_filter_by_id (ids: {Count}). Returning original dataset.", 
                    species, idList.Count);
                return genomeDatasetId;
            }
        }

        private static IEnumerable<JsonElement> EnumerateOutputEntries(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                        yield return item;
                }
                yield break;
            }

            if (root.ValueKind != JsonValueKind.Object)
                yield break;

            if (root.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in outputs.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                        yield return item;
                }
            }

            if (root.TryGetProperty("output_collections", out var collections) && collections.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in collections.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                        yield return item;
                }
            }
        }

        private async Task<string?> FindHierarchicalOrthoGroupsOutputDatasetAsync(string historyId, JsonElement outputs, CancellationToken ct)
        {
            var entries = EnumerateOutputEntries(outputs);

            foreach (var output in entries)
            {
                var labelText = output.TryGetProperty("label", out var labelProp) ? labelProp.GetString() ?? string.Empty : string.Empty;
                var name = output.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                var description = $"{labelText} {name}".Trim();

                var looksLikeHierarchical = description.Contains("hierarchical", StringComparison.OrdinalIgnoreCase)
                    || description.Contains("orthogroup", StringComparison.OrdinalIgnoreCase)
                    || description.Contains("n0", StringComparison.OrdinalIgnoreCase);

                if (!looksLikeHierarchical)
                    continue;

                var outputType = output.TryGetProperty("output_type", out var ot) ? ot.GetString() : null;
                var entityType = output.TryGetProperty("entity_type", out var et) ? et.GetString() : null;
                var collectionType = output.TryGetProperty("collection_type", out var ctProp) ? ctProp.GetString() : null;

                var isCollection = string.Equals(outputType, "dataset_collection", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entityType, "dataset_collection", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(collectionType);

                if (!isCollection)
                {
                    if (output.TryGetProperty("id", out var datasetIdProp) && !string.IsNullOrWhiteSpace(datasetIdProp.GetString()))
                        return datasetIdProp.GetString();
                    if (output.TryGetProperty("dataset_id", out var dsId) && !string.IsNullOrWhiteSpace(dsId.GetString()))
                        return dsId.GetString();
                    continue;
                }

                string? collectionId = null;
                if (output.TryGetProperty("collection_id", out var collectionIdProp))
                    collectionId = collectionIdProp.GetString();
                if (string.IsNullOrWhiteSpace(collectionId) && output.TryGetProperty("id", out var idProp))
                    collectionId = idProp.GetString();
                if (string.IsNullOrWhiteSpace(collectionId) && output.TryGetProperty("dataset_collection", out var dsCollection) && dsCollection.ValueKind == JsonValueKind.Object && dsCollection.TryGetProperty("id", out var innerId))
                    collectionId = innerId.GetString();

                var contentType = output.TryGetProperty("history_content_type", out var hct) ? hct.GetString() : null;
                if (string.IsNullOrWhiteSpace(collectionId) || !string.Equals(contentType, "dataset_collection", StringComparison.OrdinalIgnoreCase))
                    continue;

                JsonElement collectionDetails;
                try
                {
                    collectionDetails = await _galaxy.GetCollectionDetailsAsync(historyId, collectionId, ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to fetch hierarchical orthogroup collection {CollectionId} for history {HistoryId}", collectionId, historyId);
                    continue;
                }

                var datasetId = ExtractDatasetIdFromCollection(collectionDetails, new[] { "N0", "Single copy", "single-copy" });
                if (!string.IsNullOrWhiteSpace(datasetId))
                    return datasetId;

                // If we couldn't find a direct N0 match, fall back to first dataset element in collection
                datasetId = ExtractDatasetIdFromCollection(collectionDetails, null);
                if (!string.IsNullOrWhiteSpace(datasetId))
                {
                    _log.LogInformation("Falling back to first dataset in hierarchical orthogroup collection {CollectionId} (element {DatasetId})", collectionId, datasetId);
                    return datasetId;
                }
            }

            var historyFallback = await FindHierarchicalOrthoGroupsDatasetInHistoryAsync(historyId, ct);
            if (!string.IsNullOrWhiteSpace(historyFallback))
            {
                _log.LogInformation("Resolved hierarchical orthogroup dataset from history fallback: {DatasetId}", historyFallback);
                return historyFallback;
            }

            return null;
        }

        private async Task<string?> FindHierarchicalOrthoGroupsDatasetInHistoryAsync(string historyId, CancellationToken ct)
        {
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind != JsonValueKind.Array)
                    return null;

                var items = contents.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => new
                    {
                        Element = e,
                        Name = e.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
                        Type = e.TryGetProperty("history_content_type", out var typeProp) ? typeProp.GetString() ?? string.Empty : string.Empty,
                        Hid = e.TryGetProperty("hid", out var hidProp) ? hidProp.GetInt32() : -1
                    })
                    .ToList();

                static string? ExtractDatasetFromElements(JsonElement elements, IEnumerable<string> preferred)
                {
                    string? fallback = null;
                    foreach (var el in elements.EnumerateArray())
                    {
                        var identifier = el.TryGetProperty("element_identifier", out var idProp) ? idProp.GetString() : null;
                        var name = el.TryGetProperty("element_identifier", out var id2) ? id2.GetString() : null;
                        var targetLabel = identifier ?? name ?? string.Empty;

                        string? datasetId = null;
                        if (el.TryGetProperty("object", out var obj) && obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("id", out var objId))
                            datasetId = objId.GetString();
                        else if (el.TryGetProperty("id", out var elId))
                            datasetId = elId.GetString();

                        if (string.IsNullOrWhiteSpace(datasetId))
                            continue;

                        foreach (var pref in preferred)
                        {
                            if (!string.IsNullOrWhiteSpace(targetLabel) && targetLabel.IndexOf(pref, StringComparison.OrdinalIgnoreCase) >= 0)
                                return datasetId;
                        }

                        fallback ??= datasetId;
                    }

                    return fallback;
                }

                async Task<string?> TryResolveCollectionAsync(JsonElement element)
                {
                    JsonElement? elements = null;

                    // 1) Prefer contents_url for accurate identifiers
                    if (element.TryGetProperty("contents_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                    {
                        var url = urlProp.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            try
                            {
                                var contentsJson = await _galaxy.GetCollectionContentsAsync(url!, ct);
                                if (contentsJson.ValueKind == JsonValueKind.Array)
                                    elements = contentsJson;
                                else if (contentsJson.ValueKind == JsonValueKind.Object && contentsJson.TryGetProperty("elements", out var elemsObj))
                                    elements = elemsObj;
                            }
                            catch (Exception ex)
                            {
                                _log.LogDebug(ex, "Failed to fetch collection contents via contents_url {Url}", url);
                            }
                        }
                    }

                    string? collectionId = null;
                    if (element.TryGetProperty("collection_id", out var collectionIdProp))
                        collectionId = collectionIdProp.GetString();
                    if (string.IsNullOrWhiteSpace(collectionId) && element.TryGetProperty("id", out var elementIdProp))
                        collectionId = elementIdProp.GetString();

                    // 2) Fallback to collection details
                    if (!elements.HasValue && !string.IsNullOrWhiteSpace(collectionId))
                    {
                        try
                        {
                            var details = await _galaxy.GetCollectionDetailsAsync(historyId, collectionId!, ct);
                            if (details.ValueKind == JsonValueKind.Object && details.TryGetProperty("elements", out var elems))
                                elements = elems;
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Failed to fetch hierarchical orthogroup collection {CollectionId} for history {HistoryId}", collectionId, historyId);
                        }
                    }

                    // 3) Final fallback to explicit histories/.../contents path
                    if (!elements.HasValue && !string.IsNullOrWhiteSpace(collectionId))
                    {
                        try
                        {
                            var altUrl = $"/api/histories/{Uri.EscapeDataString(historyId)}/contents/dataset_collections/{Uri.EscapeDataString(collectionId)}/contents";
                            var alt = await _galaxy.GetCollectionContentsAsync(altUrl, ct);
                            if (alt.ValueKind == JsonValueKind.Array)
                                elements = alt;
                            else if (alt.ValueKind == JsonValueKind.Object && alt.TryGetProperty("elements", out var altElems))
                                elements = altElems;
                        }
                        catch (Exception ex)
                        {
                            _log.LogDebug(ex, "Failed to fetch hierarchical orthogroup collection via history path for collection {CollectionId}", collectionId);
                        }
                    }

                    if (elements.HasValue && elements.Value.ValueKind == JsonValueKind.Array)
                    {
                        var datasetId = ExtractDatasetFromElements(elements.Value, new[] { "N0", "single copy", "single-copy" });
                        if (!string.IsNullOrWhiteSpace(datasetId))
                            return datasetId;
                    }

                    return null;
                }

                // 1) Look for the hierarchical orthogroup collection and use its N0 element
                var collectionCandidates = items
                    .Where(e => string.Equals(e.Type, "dataset_collection", StringComparison.OrdinalIgnoreCase) &&
                                e.Name.Contains("hierarchical orthogroups", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.Hid)
                    .ToList();

                foreach (var candidate in collectionCandidates)
                {
                    var datasetId = await TryResolveCollectionAsync(candidate.Element);
                    if (!string.IsNullOrWhiteSpace(datasetId))
                        return datasetId;
                }

                // 2) Fall back to any dataset that looks like N0/orthogroups in the history
                var datasetFallback = items
                    .Where(e => string.Equals(e.Type, "dataset", StringComparison.OrdinalIgnoreCase) &&
                                (e.Name.Contains("N0", StringComparison.OrdinalIgnoreCase) ||
                                 e.Name.Contains("single copy", StringComparison.OrdinalIgnoreCase) ||
                                 e.Name.Contains("single-copy", StringComparison.OrdinalIgnoreCase) ||
                                 e.Name.Contains("orthogroups", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(e => e.Hid)
                    .FirstOrDefault();

                if (datasetFallback != null && datasetFallback.Element.TryGetProperty("id", out var dataId))
                    return dataId.GetString();

                // 3) As a final fallback, reuse the richest collection (most species) if present
                var multiSpeciesCandidate = collectionCandidates
                    .Select(c => new { c.Element, SpeciesCount = CountSpeciesInCollection(c.Element) })
                    .Where(c => c.SpeciesCount > 1)
                    .OrderByDescending(c => c.SpeciesCount)
                    .FirstOrDefault();

                if (multiSpeciesCandidate != null)
                {
                    var fallbackId = await TryResolveCollectionAsync(multiSpeciesCandidate.Element);
                    if (!string.IsNullOrWhiteSpace(fallbackId))
                        return fallbackId;
                }

                return null;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to scan history {HistoryId} for hierarchical orthogroup dataset", historyId);
                return null;
            }
        }

        private async Task<string?> FindOrthoGroupsDatasetInHistoryAsync(string historyId, CancellationToken ct)
        {
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind != JsonValueKind.Array)
                    return null;

                var candidates = contents.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => new
                    {
                        Name = e.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
                        Type = e.TryGetProperty("history_content_type", out var typeProp) ? typeProp.GetString() ?? string.Empty : string.Empty,
                        Hid = e.TryGetProperty("hid", out var hidProp) ? hidProp.GetInt32() : -1,
                        Id = e.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty
                    })
                    .Where(e => string.Equals(e.Type, "dataset", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(e.Id) &&
                                (e.Name.Contains("orthogroups", StringComparison.OrdinalIgnoreCase) ||
                                 e.Name.Contains("orthogroup", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(e => e.Hid)
                    .FirstOrDefault();

                return candidates?.Id;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to scan history {HistoryId} for orthogroups dataset", historyId);
                return null;
            }
        }

        private async Task<string?> FindFilteredSingleCopyDatasetInHistoryAsync(string historyId, CancellationToken ct, bool requireV2 = false)
        {
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind != JsonValueKind.Array)
                    return null;

                var candidates = contents.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => new
                    {
                        Id = e.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                        Name = e.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
                        Type = e.TryGetProperty("history_content_type", out var typeProp) ? typeProp.GetString() ?? string.Empty : string.Empty,
                        Extension = e.TryGetProperty("extension", out var extProp) ? extProp.GetString() ?? string.Empty : string.Empty,
                        Hid = e.TryGetProperty("hid", out var hidProp) ? hidProp.GetInt32() : -1,
                        Deleted = e.TryGetProperty("deleted", out var deletedProp) && deletedProp.ValueKind == JsonValueKind.True,
                        State = e.TryGetProperty("state", out var stateProp) ? stateProp.GetString() ?? string.Empty : string.Empty
                    })
                    .Where(e =>
                        string.Equals(e.Type, "dataset", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(e.Id) &&
                        !e.Deleted &&
                        !e.State.Equals("error", StringComparison.OrdinalIgnoreCase) &&
                        !e.State.Equals("failed", StringComparison.OrdinalIgnoreCase) &&
                        (!requireV2 || e.Name.Contains("v2", StringComparison.OrdinalIgnoreCase)) &&
                        (e.Extension.Equals("tabular", StringComparison.OrdinalIgnoreCase) ||
                         e.Extension.Equals("tsv", StringComparison.OrdinalIgnoreCase)) &&
                        (e.Name.Contains("single copy", StringComparison.OrdinalIgnoreCase) ||
                         e.Name.Contains("single-copy", StringComparison.OrdinalIgnoreCase) ||
                         e.Name.Contains("filter", StringComparison.OrdinalIgnoreCase) ||
                         e.Name.Contains("filtered", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(e =>
                        e.Name.Contains("strict 1-to-1 N0", StringComparison.OrdinalIgnoreCase) ? 4 :
                        e.Name.Contains("single-copy", StringComparison.OrdinalIgnoreCase) || e.Name.Contains("single copy", StringComparison.OrdinalIgnoreCase) ? 3 :
                        e.Name.Contains("filter on dataset", StringComparison.OrdinalIgnoreCase) ? 2 : 1)
                    .ThenByDescending(e => e.Hid)
                    .ToList();

                // Galaxy histories can contain many unrelated tabular "Filter on dataset"
                // outputs. Only accept a table that is structurally a strict N0 1:1 table.
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var content = await _galaxy.DownloadDatasetAsync(historyId, candidate.Id, ct);
                        FilterN0SingleCopyRows(content, out var speciesCount, out var rowCount);
                        if (speciesCount > 0 && rowCount > 0)
                            return candidate.Id;
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "Skipping invalid filtered orthogroup candidate {DatasetId} ({Name})", candidate.Id, candidate.Name);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to scan history {HistoryId} for filtered single-copy orthogroups dataset", historyId);
                return null;
            }
        }

        private async Task<List<StrictN0DatasetCandidate>> FindStrictN0DatasetsAsync(
            string historyId,
            CancellationToken ct)
        {
            var contents = await _galaxy.ListHistoryContentsAsync(
                historyId, ct, includeHidden: true, includeDeleted: false);
            if (contents.ValueKind != JsonValueKind.Array)
                return new List<StrictN0DatasetCandidate>();

            return contents.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new StrictN0DatasetCandidate(
                    item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    item.TryGetProperty("state", out var state) ? state.GetString() ?? string.Empty : string.Empty,
                    item.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var value) ? value : -1))
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Id) &&
                    item.Name.StartsWith(StrictN0DatasetPrefix, StringComparison.OrdinalIgnoreCase) &&
                    item.Name.EndsWith("v2.tsv", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Hid)
                .ToList();
        }

        private async Task RemoveDuplicateStrictN0DatasetsAsync(
            string historyId,
            IReadOnlyCollection<StrictN0DatasetCandidate> candidates,
            string retainedDatasetId,
            CancellationToken ct)
        {
            foreach (var duplicate in candidates.Where(item =>
                         !item.Id.Equals(retainedDatasetId, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    await _galaxy.DeleteHistoryDatasetAsync(historyId, duplicate.Id, ct);
                    _log.LogInformation(
                        "Purged duplicate strict N0 dataset {DatasetId} (HID {Hid}) from history {HistoryId}; retained {RetainedDatasetId}.",
                        duplicate.Id, duplicate.Hid, historyId, retainedDatasetId);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(
                        ex,
                        "Could not purge duplicate strict N0 dataset {DatasetId} (HID {Hid}) from history {HistoryId}.",
                        duplicate.Id, duplicate.Hid, historyId);
                }
            }
        }

        public async Task ConsolidateStrictN0DatasetsAsync(string historyId, CancellationToken ct = default)
        {
            var gate = StrictN0HistoryLocks.GetOrAdd(historyId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                var candidates = await FindStrictN0DatasetsAsync(historyId, ct);
                if (candidates.Count < 2) return;

                var retained = candidates.FirstOrDefault(item =>
                                   item.State.Equals("ok", StringComparison.OrdinalIgnoreCase))
                               ?? candidates.First();
                await RemoveDuplicateStrictN0DatasetsAsync(historyId, candidates, retained.Id, ct);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<string?> FindMetadataDatasetInHistoryAsync(string historyId, CancellationToken ct)
        {
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind != JsonValueKind.Array)
                    return null;

                var candidate = contents.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => new
                    {
                        Id = e.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                        Name = e.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
                        Type = e.TryGetProperty("history_content_type", out var typeProp) ? typeProp.GetString() ?? string.Empty : string.Empty,
                        Hid = e.TryGetProperty("hid", out var hidProp) ? hidProp.GetInt32() : -1
                    })
                    .Where(e =>
                        string.Equals(e.Type, "dataset", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(e.Id) &&
                        e.Name.Contains(MetadataDatasetName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.Hid)
                    .FirstOrDefault();

                return candidate?.Id;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to scan history {HistoryId} for metadata dataset", historyId);
                return null;
            }
        }

        private static string BuildSingleCopyFilterExpression(int speciesCount)
        {
            // Columns start at c4 for the first species (c1-c3 are metadata)
            var conditions = new List<string>();
            for (int i = 0; i < speciesCount; i++)
            {
                var col = 4 + i;
                var c = $"c{col}";
                conditions.Add($"{c} != '' and ',' not in {c}");
            }
            return string.Join(" and ", conditions);
        }

        internal static int FindN0SpeciesStartColumn(IReadOnlyList<string> header)
        {
            var metadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "HOG", "Orthogroup", "OG", "Gene Tree Parent Clade"
            };
            var index = 0;
            while (index < header.Count && metadata.Contains(header[index].Trim())) index++;
            return index;
        }

        internal static string FilterN0SingleCopyRows(string content, out int speciesCount, out int rowCount)
        {
            speciesCount = 0;
            rowCount = 0;
            var lines = content.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return string.Empty;

            var header = lines[0].Split('\t');
            var speciesStart = FindN0SpeciesStartColumn(header);
            var parentCladeColumn = Array.FindIndex(header, column =>
                column.Trim().Equals("Gene Tree Parent Clade", StringComparison.OrdinalIgnoreCase));
            speciesCount = Math.Max(0, header.Length - speciesStart);
            if (speciesCount == 0) return string.Empty;

            var output = new StringBuilder().AppendLine(lines[0]);
            foreach (var line in lines.Skip(1))
            {
                var cells = line.Split('\t');
                if (cells.Length < header.Length)
                    continue;

                var belongsToN0 = parentCladeColumn >= 0
                    ? cells[parentCladeColumn].Trim().Equals("N0", StringComparison.OrdinalIgnoreCase)
                    : cells[0].Trim().StartsWith("N0", StringComparison.OrdinalIgnoreCase);
                if (!belongsToN0)
                    continue;

                var onePerSpecies = true;
                for (var column = speciesStart; column < header.Length; column++)
                {
                    var genes = cells[column].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (genes.Length != 1 || genes[0] is "-" or "0")
                    {
                        onePerSpecies = false;
                        break;
                    }
                }
                if (!onePerSpecies) continue;
                output.AppendLine(line);
                rowCount++;
            }
            return output.ToString();
        }

        private async Task<int> GetSpeciesCountForHistoryAsync(string historyId, CancellationToken ct)
        {
            try
            {
                var meta = await LoadMetadataAsync(historyId, ct);
                return meta.Genomes?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string SanitizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unknown";
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
            }
            return sb.ToString().Trim('_');
        }

        internal static string NormalizeRecoveredSpeciesName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var name = Path.GetFileNameWithoutExtension(value.Trim().Trim('\'', '"'));
            name = Regex.Replace(
                name,
                @"(?:[_\- ]+(?:proteome|protein|cds|coding[_\- ]*sequence)(?:[_\- ]+filtered)?|[_\- ]+filtered)$",
                string.Empty,
                RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"[_\-]+", " ").Trim();
            if (name.Length == 0) return string.Empty;

            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.ToLowerInvariant())
                .ToArray();
            if (words.Length == 0) return string.Empty;
            words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
            return string.Join(' ', words);
        }

        internal static string NormalizeSpeciesTreeLeafLabels(string? newick, IReadOnlyList<GenomeInfo>? genomes = null)
        {
            if (string.IsNullOrWhiteSpace(newick)) return newick ?? string.Empty;

            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var genome in genomes ?? Array.Empty<GenomeInfo>())
            {
                var display = string.IsNullOrWhiteSpace(genome.OriginalSpecies)
                    ? NormalizeRecoveredSpeciesName(genome.Species)
                    : genome.OriginalSpecies;
                foreach (var alias in new[] { genome.Species, genome.OriginalSpecies, genome.StandardizedSpecies })
                {
                    if (!string.IsNullOrWhiteSpace(alias))
                        aliases[SanitizeName(alias)] = display;
                }
            }

            return Regex.Replace(newick, @"(?<=[(,])\s*(?<label>[^():,;]+?)\s*(?=[:),;])", match =>
            {
                var raw = match.Groups["label"].Value.Trim().Trim('\'', '"');
                var normalized = NormalizeRecoveredSpeciesName(raw);
                var key = SanitizeName(normalized);
                var display = aliases.TryGetValue(key, out var mapped) ? mapped : normalized;
                return string.IsNullOrWhiteSpace(display) ? match.Value : display.Replace(' ', '_');
            });
        }

        private async Task<string> FilterHierarchicalOrthoGroupsAsync(string historyId, string n0DatasetId, int speciesCount, CancellationToken ct)
        {
            var n0 = await _galaxy.DownloadDatasetAsync(historyId, n0DatasetId, ct);
            var filtered = FilterN0SingleCopyRows(n0, out var detectedSpeciesCount, out var rowCount);
            if (detectedSpeciesCount <= 0)
                throw new InvalidOperationException("The N0 table has no recognizable species columns.");
            if (speciesCount > 0 && detectedSpeciesCount != speciesCount)
                _log.LogWarning("N0 header contains {Detected} species columns; metadata contains {Expected}. Using the N0 header.", detectedSpeciesCount, speciesCount);
            if (rowCount == 0)
                throw new InvalidOperationException("The N0 table contains no strict 1:1 orthogroups.");

            _log.LogInformation("Identified {Count} strict 1:1 N0 orthogroups across {SpeciesCount} species.", rowCount, detectedSpeciesCount);
            return await _galaxy.UploadTextContentAsync(
                historyId, filtered, $"phyloDIVaS strict 1-to-1 N0 orthogroups ({rowCount}) v2.tsv", "tabular", ct);
        }

        private async Task<(string DatasetId, bool Reused)> EnsureFilteredSingleCopyDatasetAsync(
            string historyId,
            string n0DatasetId,
            int speciesCount,
            CancellationToken ct)
        {
            var gate = StrictN0HistoryLocks.GetOrAdd(historyId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                // A canonical upload in a transient state reserves this history. Wait
                // for it instead of treating a temporary download failure as absence.
                var canonical = await FindStrictN0DatasetsAsync(historyId, ct);
                var retained = canonical.FirstOrDefault(item =>
                                   item.State.Equals("ok", StringComparison.OrdinalIgnoreCase))
                               ?? canonical.FirstOrDefault(item => item.State is
                                   "new" or "queued" or "running" or "upload" or "setting_metadata");
                if (retained is not null)
                {
                    await RemoveDuplicateStrictN0DatasetsAsync(historyId, canonical, retained.Id, ct);
                    if (!retained.State.Equals("ok", StringComparison.OrdinalIgnoreCase))
                        await WaitForDatasetReadyAsync(historyId, retained.Id, ct);

                    var content = await _galaxy.DownloadDatasetAsync(historyId, retained.Id, ct);
                    FilterN0SingleCopyRows(content, out var detectedSpeciesCount, out var rowCount);
                    if (detectedSpeciesCount > 0 && rowCount > 0)
                    {
                        _log.LogInformation(
                            "Reusing canonical strict N0 dataset {DatasetId} in history {HistoryId}.",
                            retained.Id, historyId);
                        return (retained.Id, true);
                    }

                    await _galaxy.DeleteHistoryDatasetAsync(historyId, retained.Id, ct);
                    _log.LogWarning(
                        "Removed invalid canonical strict N0 dataset {DatasetId} from history {HistoryId}.",
                        retained.Id, historyId);
                }

                var existingFilteredId = await FindFilteredSingleCopyDatasetInHistoryAsync(
                    historyId, ct, requireV2: false);
                if (!string.IsNullOrWhiteSpace(existingFilteredId))
                {
                    _log.LogInformation(
                        "Found existing filtered single-copy orthogroups dataset {DatasetId} in history {HistoryId}.",
                        existingFilteredId, historyId);
                    return (existingFilteredId, true);
                }

                _log.LogInformation(
                    "No filtered single-copy dataset found in history {HistoryId}; creating one from N0 dataset {DatasetId} for {SpeciesCount} species.",
                    historyId, n0DatasetId, speciesCount);
                var filteredDatasetId = await FilterHierarchicalOrthoGroupsAsync(
                    historyId, n0DatasetId, speciesCount, ct);
                return (filteredDatasetId, false);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<Dictionary<string, string>> FilterProteomesForSingleCopyOrthogroupsAsync(
            string historyId,
            string filteredTabularDatasetId,
            List<GenomeInfo> genomes,
            CancellationToken ct)
        {
            var outputs = new Dictionary<string, string>();
            if (genomes == null || genomes.Count == 0)
            {
                _log.LogInformation("No genomes available to filter proteomes; skipping filtered proteome generation.");
                return outputs;
            }

            const string seqFilterToolId = "toolshed.g2.bx.psu.edu/repos/peterjc/seq_filter_by_id/seq_filter_by_id/0.2.9";

            // Preload filtered tabular content once and build per-species ID lists
            var filteredContent = await _galaxy.DownloadDatasetAsync(historyId, filteredTabularDatasetId, ct);
            var lines = filteredContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                _log.LogWarning("Filtered tabular dataset {DatasetId} is empty; skipping proteome filtering.", filteredTabularDatasetId);
                return outputs;
            }

            var headerParts = lines[0].Split('\t');
            // Species columns start at index 3 (1-based column 4) in the filtered single-copy table
            var speciesIdMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (int gi = 0; gi < genomes.Count; gi++)
            {
                speciesIdMap[genomes[gi].Species] = new List<string>();
            }

            for (int li = 1; li < lines.Length; li++)
            {
                var parts = lines[li].Split('\t');
                for (int gi = 0; gi < genomes.Count; gi++)
                {
                    var colIndex = 3 + gi; // zero-based index into parts
                    if (colIndex >= parts.Length) continue;
                    var cell = parts[colIndex].Trim();
                    if (string.IsNullOrWhiteSpace(cell) || cell == "-" || cell == "0") continue;
                    var ids = cell.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!speciesIdMap.TryGetValue(genomes[gi].Species, out var list))
                        continue;
                    list.AddRange(ids);
                }
            }

            for (int i = 0; i < genomes.Count; i++)
            {
                var currentGenome = genomes[i];
                if (string.IsNullOrWhiteSpace(currentGenome.ProteinDatasetId))
                {
                    _log.LogWarning("Skipping proteome filter for {Species} because protein dataset id is missing", currentGenome.Species);
                    continue;
                }

                if (!speciesIdMap.TryGetValue(currentGenome.Species, out var idList) || idList.Count == 0)
                {
                    _log.LogWarning("Skipping proteome filter for {Species}: no gene IDs found in filtered orthogroups", currentGenome.Species);
                    continue;
                }

                // Deduplicate IDs to avoid unnecessary tool work
                var uniqueIds = idList.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var idListText = string.Join("\n", uniqueIds);

                _log.LogInformation("Filtering proteome for species {Species} using {IdCount} IDs from filtered single-copy orthogroups", currentGenome.Species, uniqueIds.Count);

                var inputs = new Dictionary<string, object>
                {
                    ["input_file"] = new { src = "hda", id = currentGenome.ProteinDatasetId },
                    ["filter_type"] = "list",
                    ["id_list"] = idListText,
                    // Output positive matches as a single file
                    ["sense"] = "pos",
                    // Some wrappers still expect this dynamic param even when using list mode; use column 1
                    ["id_opts|columns"] = "1"
                };

                try
                {
                    var (jobId, raw) = await _galaxy.InvokeToolAsync(historyId, seqFilterToolId, inputs, ct);
                    if (string.IsNullOrWhiteSpace(jobId))
                        throw new InvalidOperationException("Seq filter invocation did not return a job id.");

                    var outputId = await ResolveOutputDatasetId(historyId, raw, ct);
                    if (string.IsNullOrWhiteSpace(outputId))
                        throw new InvalidOperationException("Seq filter tool did not produce an output dataset.");

                    await WaitForJobCompletionAsync(jobId, ct);

                    var targetName = $"filtered_proteome_{SanitizeName(currentGenome.Species)}.fasta";
                    await _galaxy.RenameHistoryContentAsync(historyId, outputId, targetName, ct);
                    outputs[currentGenome.Species] = outputId;
                    _log.LogInformation("Filtered proteome for {Species} saved as {DatasetId} ({Name})", currentGenome.Species, outputId, targetName);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to filter proteome for {Species} using seq_filter_by_id (ids: {IdCount})", currentGenome.Species, uniqueIds.Count);
                }
            }

            return outputs;
        }

        private async Task<string> ReadFirstLineAsync(string historyId, string datasetId, CancellationToken ct)
        {
            var content = await _galaxy.DownloadDatasetAsync(historyId, datasetId, ct);
            if (string.IsNullOrEmpty(content)) return string.Empty;
            var idx = content.IndexOf('\n');
            return idx >= 0 ? content.Substring(0, idx) : content;
        }

        private int DeriveSpeciesCountFromHeader(string headerLine)
        {
            if (string.IsNullOrWhiteSpace(headerLine)) return 0;
            var cols = headerLine.Split('\t', StringSplitOptions.TrimEntries);
            return Math.Max(0, cols.Length - FindN0SpeciesStartColumn(cols));
        }

        private async Task<string> ExtractProteinSequenceAsync(string historyId, string proteinDatasetId, string proteinId, string species, CancellationToken ct)
        {
            // Prefer the same seq_filter_by_id approach we use for CDS extraction
            const string seqFilterToolId = "toolshed.g2.bx.psu.edu/repos/peterjc/seq_filter_by_id/seq_filter_by_id/0.2.9";
            var inputs = new Dictionary<string, object>
            {
                ["input_file"] = new { src = "hda", id = proteinDatasetId },
                ["filter_type"] = "list",
                ["id_list"] = proteinId,
                ["sense"] = "pos",
                ["id_opts|columns"] = "1"
            };

            try
            {
                _log.LogDebug("Extracting protein {ProteinId} for {Species} using seq_filter_by_id", proteinId, species);
                var (_, raw) = await _galaxy.InvokeToolAsync(historyId, seqFilterToolId, inputs, ct);
                var resolved = await ResolveOutputDatasetId(historyId, raw, ct);
                _log.LogDebug("Protein extract for {ProteinId} -> dataset {DatasetId}", proteinId, resolved);
                return resolved;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to extract protein {ProteinId} from {Species} using seq_filter_by_id", proteinId, species);
                return proteinDatasetId;
            }
        }

        private async Task<string?> BuildProteinAlignmentsForOrthogroupsAsync(
            string historyId, 
            string filteredTabularContent, 
            List<GenomeInfo> genomes, 
            string alignment, 
            CancellationToken ct,
            IReadOnlyCollection<string>? selectedOrthogroupIds = null)
        {
            if (genomes == null || genomes.Count == 0)
            {
                _log.LogWarning("No genomes available; skipping protein alignment build.");
                return null;
            }

            var lines = filteredTabularContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                _log.LogWarning("Filtered orthogroups content is empty; nothing to align.");
                return null;
            }

            var selectedOrthogroupSet = selectedOrthogroupIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hasSelection = selectedOrthogroupSet is { Count: > 0 };
            if (hasSelection)
            {
                _log.LogInformation("Preparing orthogroup ID tables for {Count} selected orthogroup(s).", selectedOrthogroupSet!.Count);
            }

            // Take one history snapshot and resolve every reusable input from it. Large
            // Galaxy histories are expensive to enumerate, so this method must not scan
            // the full history independently for every input.
            var historyContents = await _galaxy.ListHistoryContentsAsync(
                historyId, ct, includeHidden: true, includeDeleted: false);

            string? speciesMappingId = FindLatestHistoryDatasetId(
                historyContents,
                name => name.Contains("species_mapping", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("species mapping", StringComparison.OrdinalIgnoreCase));
            speciesMappingId ??= await UploadSpeciesMappingAsync(
                historyId,
                genomes.Select(g => (
                    string.IsNullOrWhiteSpace(g.OriginalSpecies) ? g.Species : g.OriginalSpecies,
                    string.IsNullOrWhiteSpace(g.StandardizedSpecies) ? SanitizeName(g.Species) : g.StandardizedSpecies
                )).ToList(),
                ct);

            var newickId = FindOrthoFinderSpeciesTreeDatasetId(historyContents);
            if (string.IsNullOrWhiteSpace(newickId))
            {
                _log.LogWarning("Could not find the OrthoFinder Newick tree in history {HistoryId}; skipping workflow invocation.", historyId);
                return null;
            }

            // Reuse the newest genome collections. They already contain the protein and
            // CDS FASTA datasets for these species and do not need to be rebuilt per run.
            string? proteomeCollectionId = FindLatestHistoryCollectionId(
                historyContents,
                name => name.Equals("protein_genomes", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Proteomes", StringComparison.OrdinalIgnoreCase));
            string? cdsCollectionId = FindLatestHistoryCollectionId(
                historyContents,
                name => name.Equals("cds_genomes", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("CDS", StringComparison.OrdinalIgnoreCase));
            try
            {
                if (string.IsNullOrWhiteSpace(proteomeCollectionId))
                {
                    var proteomeElements = genomes.ToDictionary(g => SanitizeName(g.Species), g => g.ProteinDatasetId);
                    proteomeCollectionId = await _galaxy.CreateDatasetCollectionAsync(historyId, "protein_genomes", proteomeElements, ct);
                    await _galaxy.UpdateCollectionDatatypeAsync(historyId, proteomeCollectionId, "fasta", ct);
                }

                if (string.IsNullOrWhiteSpace(cdsCollectionId))
                {
                    var cdsElements = genomes.ToDictionary(g => $"cds_{SanitizeName(g.Species)}", g => g.CdsDatasetId);
                    cdsCollectionId = await _galaxy.CreateDatasetCollectionAsync(historyId, "cds_genomes", cdsElements, ct);
                    await _galaxy.UpdateCollectionDatatypeAsync(historyId, cdsCollectionId, "fasta", ct);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to build proteome/CDS collections; aborting orthogroup workflow invocation.");
                return null;
            }

            // Reuse the newest completed "Unzip on dataset ... all files as collection"
            // when its elements exactly match the selected orthogroups.
            string? orthogroupCollectionId = hasSelection
                ? await FindPreparedOrthogroupCollectionAsync(historyId, historyContents, selectedOrthogroupSet!, ct)
                : null;
            string? zipDatasetId = null;

            // No matching prepared collection exists, so create it once.
            int processed = 0;
            using var zipStream = new MemoryStream();
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true);

            for (int li = 1; string.IsNullOrWhiteSpace(orthogroupCollectionId) && li < lines.Length; li++)
            {
                var parts = lines[li].Split('\t');
                if (parts.Length < 4) continue; // need at least orthogroup + 3 metadata columns

                var orthogroupId = !string.IsNullOrWhiteSpace(parts[0]) ? parts[0].Trim() : $"og_{li}";
                if (hasSelection && !selectedOrthogroupSet!.Contains(orthogroupId))
                {
                    continue;
                }

                // Species gene IDs begin after the first three metadata columns
                var speciesCells = parts.Skip(3).Take(genomes.Count).ToList();
                if (speciesCells.Count == 0)
                {
                    _log.LogWarning("Orthogroup {Orthogroup}: no species columns found; skipping ID table upload.", orthogroupId);
                    continue;
                }

                // One sequence ID per species, one per line in the tabular
                var linesForGroup = new List<string>(speciesCells.Count);
                bool missingSpeciesId = false;
                for (int si = 0; si < speciesCells.Count; si++)
                {
                    var cell = (speciesCells[si] ?? string.Empty).Trim();
                    var ids = cell.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .ToList();

                    if (ids.Count == 0)
                    {
                        missingSpeciesId = true;
                        break;
                    }

                    if (ids.Count > 1)
                        _log.LogDebug("Orthogroup {Orthogroup}: multiple IDs for species index {Index}; using first.", orthogroupId, si);

                    linesForGroup.Add(ids[0]);
                }

                if (missingSpeciesId || linesForGroup.Count != speciesCells.Count)
                {
                    _log.LogWarning("Orthogroup {Orthogroup}: at least one species was missing a sequence id; skipping ID table upload.", orthogroupId);
                    continue;
                }

                var idTableContent = string.Join("\n", linesForGroup);
                var name = $"{SanitizeName(orthogroupId)}.tabular";
                var entry = zip.CreateEntry(name);
                await using (var writer = new StreamWriter(entry.Open()))
                {
                    await writer.WriteAsync(idTableContent);
                }
                processed++;
                if (processed % 50 == 0)
                {
                    _log.LogInformation("Prepared {Count} orthogroup ID tables for zip upload", processed);
                }
            }

            if (string.IsNullOrWhiteSpace(orthogroupCollectionId) && processed == 0)
            {
                _log.LogWarning(hasSelection
                    ? "No orthogroup ID tables matched the selected orthogroups; skipping workflow invocation."
                    : "No orthogroup ID tables were prepared; skipping workflow invocation.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(orthogroupCollectionId))
            {
                zip.Dispose();
                var zipBytes = zipStream.ToArray();
                zipDatasetId = await _galaxy.UploadBinaryContentAsync(historyId, zipBytes, "orthogroup_ids.zip", "zip", ct);
                var unzipJob = await StartUnzipJobAsync(historyId, zipDatasetId, "*.tabular", ct);
                if (!string.IsNullOrWhiteSpace(unzipJob.JobId))
                    await WaitForJobCompletionAsync(unzipJob.JobId, ct);
                orthogroupCollectionId = unzipJob.CollectionId;
            }
            else
            {
                _log.LogInformation(
                    "Reusing prepared orthogroup collection {CollectionId} for {Count} selected group(s).",
                    orthogroupCollectionId,
                    selectedOrthogroupSet!.Count);
            }

            if (string.IsNullOrWhiteSpace(orthogroupCollectionId))
            {
                _log.LogWarning("Unzip did not produce an orthogroup collection; skipping workflow invocation.");
                return null;
            }

            // Step 3: invoke Galaxy workflow to build per-orthogroup multi-FASTAs (and HYPHY BUSTED)
            var workflowId = "d6ff2fbcecc1162e"; // default to MAFFT
            if (!string.IsNullOrWhiteSpace(alignment))
            {
                var a = alignment.Trim().ToLowerInvariant();
                if (a == "muscle") workflowId = "a61f032e7ea0cc1b";
                else if (a == "clustalw" || a == "clustal") workflowId = "3d22fd58ac5b3006";
            }
            if (string.IsNullOrWhiteSpace(speciesMappingId))
            {
                _log.LogWarning("Species mapping dataset id is missing; skipping orthogroup workflow invocation.");
                return null;
            }

            // Provide inputs keyed by both label and step index to satisfy Galaxy API expectations
            var inputs = new Dictionary<string, object>
            {
                ["proteome_fastas"] = new { src = "hdca", id = proteomeCollectionId },
                ["proteome_collection"] = new { src = "hdca", id = proteomeCollectionId },
                ["0"] = new { src = "hdca", id = proteomeCollectionId },
                ["6310435"] = new { src = "hdca", id = proteomeCollectionId },

                ["orthogroup_multifastas"] = new { src = "hdca", id = orthogroupCollectionId },
                ["orthogroup_tabulars"] = new { src = "hdca", id = orthogroupCollectionId },
                ["1"] = new { src = "hdca", id = orthogroupCollectionId },

                ["cds_genomes"] = new { src = "hdca", id = cdsCollectionId },
                ["2"] = new { src = "hdca", id = cdsCollectionId },
                ["6310436"] = new { src = "hdca", id = cdsCollectionId },

                ["species_mapping.tabular"] = new { src = "hda", id = speciesMappingId },
                ["species_mapping"] = new { src = "hda", id = speciesMappingId },
                ["3"] = new { src = "hda", id = speciesMappingId },
                
                ["newick_tree"] = new { src = "hda", id = newickId },
                ["4"] = new { src = "hda", id = newickId }
            };

            try
            {
                var (invocationId, raw) = await _galaxy.InvokeWorkflowAsync(workflowId, historyId, inputs, ct);
                _log.LogInformation("Started orthogroup multifasta workflow {WorkflowId}. Invocation: {InvocationId}. Inputs: proteomes={ProteomeCollection}, cds={CdsCollection}, orthogroups={OrthogroupCollection}, newick={NewickId}", workflowId, invocationId, proteomeCollectionId, cdsCollectionId, orthogroupCollectionId, newickId);
                await CreateOrthogroupWorkflowMarkerAsync(historyId, invocationId, ct);

                return invocationId;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to invoke orthogroup multifasta workflow {WorkflowId} with inputs proteomes={ProteomeCollection}, cds={CdsCollection}, orthogroups={OrthogroupCollection}, newick={NewickId}", workflowId, proteomeCollectionId, cdsCollectionId, orthogroupCollectionId, newickId);
                return null;
            }
        }

        private static string? FindLatestHistoryDatasetId(
            JsonElement historyContents,
            Func<string, bool> nameMatches,
            Func<string, bool>? extensionMatches = null)
        {
            if (historyContents.ValueKind != JsonValueKind.Array)
                return null;

            return historyContents.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Extension = item.TryGetProperty("extension", out var extension) ? extension.GetString() ?? string.Empty : string.Empty,
                    Hid = item.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var hidValue) ? hidValue : -1,
                    Type = item.TryGetProperty("history_content_type", out var type) ? type.GetString() : null,
                    Deleted = item.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True
                })
                .Where(item => !item.Deleted
                    && string.Equals(item.Type, "dataset", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.Id)
                    && (nameMatches(item.Name) || (extensionMatches?.Invoke(item.Extension) ?? false)))
                .OrderByDescending(item => item.Hid)
                .Select(item => item.Id)
                .FirstOrDefault();
        }

        internal static string? FindOrthoFinderSpeciesTreeDatasetId(JsonElement historyContents)
        {
            if (historyContents.ValueKind != JsonValueKind.Array)
                return null;

            return historyContents.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Extension = item.TryGetProperty("extension", out var extension) ? extension.GetString() ?? string.Empty : string.Empty,
                    Hid = item.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var hidValue) ? hidValue : -1,
                    Type = item.TryGetProperty("history_content_type", out var type) ? type.GetString() : null,
                    State = item.TryGetProperty("state", out var state) ? state.GetString() ?? string.Empty : string.Empty,
                    Deleted = item.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True,
                    Purged = item.TryGetProperty("purged", out var purged) && purged.ValueKind == JsonValueKind.True
                })
                .Where(item => !item.Deleted
                    && !item.Purged
                    && string.Equals(item.Type, "dataset", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.Id)
                    && (string.IsNullOrWhiteSpace(item.State) || item.State.Equals("ok", StringComparison.OrdinalIgnoreCase)))
                .Select(item => new
                {
                    Item = item,
                    CompactName = Regex.Replace(item.Name, @"[^a-z0-9]+", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant()
                })
                .Where(candidate =>
                    candidate.CompactName.Contains("speciestree", StringComparison.Ordinal)
                    && !candidate.CompactName.Contains("genetree", StringComparison.Ordinal)
                    && !candidate.CompactName.Contains("iqtree", StringComparison.Ordinal)
                    && !candidate.CompactName.Contains("hyphy", StringComparison.Ordinal)
                    && !candidate.CompactName.Contains("normalized", StringComparison.Ordinal))
                .Select(candidate => new
                {
                    candidate.Item,
                    Score = (candidate.CompactName.Contains("orthofinder", StringComparison.Ordinal) ? 100 : 0)
                        + (candidate.CompactName.Contains("rooted", StringComparison.Ordinal) ? 50 : 0)
                        + (candidate.Item.Extension.Equals("newick", StringComparison.OrdinalIgnoreCase) ? 20 : 0)
                })
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Item.Hid)
                .Select(candidate => candidate.Item.Id)
                .FirstOrDefault();
        }

        private static string? FindLatestHistoryCollectionId(
            JsonElement historyContents,
            Func<string, bool> nameMatches)
        {
            if (historyContents.ValueKind != JsonValueKind.Array)
                return null;

            return historyContents.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Hid = item.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var hidValue) ? hidValue : -1,
                    Type = item.TryGetProperty("history_content_type", out var type) ? type.GetString() : null,
                    Deleted = item.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True
                })
                .Where(item => !item.Deleted
                    && string.Equals(item.Type, "dataset_collection", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.Id)
                    && nameMatches(item.Name))
                .OrderByDescending(item => item.Hid)
                .Select(item => item.Id)
                .FirstOrDefault();
        }

        private async Task<string?> FindPreparedOrthogroupCollectionAsync(
            string historyId,
            JsonElement historyContents,
            IReadOnlySet<string> selectedOrthogroups,
            CancellationToken ct)
        {
            if (historyContents.ValueKind != JsonValueKind.Array || selectedOrthogroups.Count == 0)
                return null;

            var candidates = historyContents.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Hid = item.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var hidValue) ? hidValue : -1,
                    Type = item.TryGetProperty("history_content_type", out var type) ? type.GetString() : null,
                    Deleted = item.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True
                })
                .Where(item => !item.Deleted
                    && string.Equals(item.Type, "dataset_collection", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.Id)
                    && item.Name.Contains("Unzip on dataset", StringComparison.OrdinalIgnoreCase)
                    && item.Name.Contains("all files", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Hid)
                .ToList();

            foreach (var candidate in candidates)
            {
                try
                {
                    var details = await _galaxy.GetCollectionDetailsAsync(historyId, candidate.Id!, ct);
                    if (details.ValueKind != JsonValueKind.Object
                        || !details.TryGetProperty("elements", out var elements)
                        || elements.ValueKind != JsonValueKind.Array)
                        continue;

                    var groupIds = elements.EnumerateArray()
                        .Where(element => element.ValueKind == JsonValueKind.Object)
                        .Select(element => element.TryGetProperty("element_identifier", out var identifier)
                            ? NormalizeOrthogroupElementName(identifier.GetString())
                            : null)
                        .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (groupIds.SetEquals(selectedOrthogroups))
                        return candidate.Id;
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Could not inspect prepared orthogroup collection {CollectionId}", candidate.Id);
                }
            }

            return null;
        }

        private static string? NormalizeOrthogroupElementName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = Path.GetFileNameWithoutExtension(value.Trim());
            return normalized.EndsWith("_ids", StringComparison.OrdinalIgnoreCase)
                ? normalized[..^4]
                : normalized;
        }

        private async Task<string?> FindNewickTreeInHistoryAsync(string historyId, CancellationToken ct)
        {
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind != JsonValueKind.Array)
                    return null;

                return FindOrthoFinderSpeciesTreeDatasetId(contents);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to locate newick tree dataset in history {HistoryId}", historyId);
                return null;
            }
        }

        private async Task WaitForDatasetReadyAsync(string datasetId, CancellationToken ct, int maxAttempts = 40, int delayMs = 2000)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var details = await _galaxy.GetDatasetDetailsAsync(datasetId, ct);
                var state = details.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;

                if (string.Equals(state, "ok", StringComparison.OrdinalIgnoreCase))
                    return;

                if (string.Equals(state, "error", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(state, "deleted", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Dataset {datasetId} failed with state '{state}'.");
                }

                await Task.Delay(delayMs, ct);
            }

            throw new TimeoutException($"Dataset {datasetId} did not become ready within {(maxAttempts * delayMs) / 1000} seconds.");
        }

        private async Task<List<string>> FilterToReadyFastaDatasetsAsync(IEnumerable<string> datasetIds, CancellationToken ct, string kind, string? requiredFormat = null)
        {
            var ready = new List<string>();
            foreach (var id in datasetIds.Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                try
                {
                    await WaitForDatasetReadyAsync(id, ct);
                    var details = await _galaxy.GetDatasetDetailsAsync(id, ct);
                    var ext = details.TryGetProperty("extension", out var extProp) ? extProp.GetString() ?? string.Empty : string.Empty;
                    var state = details.TryGetProperty("state", out var stateProp) ? stateProp.GetString() ?? string.Empty : string.Empty;
                    var lowerExt = ext.ToLowerInvariant();
                    var lowerName = details.TryGetProperty("name", out var nameProp) ? (nameProp.GetString() ?? string.Empty).ToLowerInvariant() : string.Empty;
                    var format = details.TryGetProperty("file_ext", out var fmtProp) ? fmtProp.GetString() ?? string.Empty : string.Empty;
                    var lowerFmt = format.ToLowerInvariant();

                    if (!string.Equals(state, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        _log.LogWarning("Skipping {Kind} dataset {DatasetId} because state is {State}", kind, id, state);
                        continue;
                    }

                    if (lowerExt.Contains("zip"))
                    {
                        _log.LogWarning("Skipping {Kind} dataset {DatasetId} because it is still a zip; extraction not finished", kind, id);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(requiredFormat))
                    {
                        var requiredLower = requiredFormat.ToLowerInvariant();
                        var matchesFormat =
                            lowerFmt.Contains(requiredLower) ||
                            lowerName.Contains(requiredLower) ||
                            lowerName.EndsWith(".faa") ||
                            lowerName.EndsWith(".fa") ||
                            lowerName.EndsWith(".fna") ||
                            lowerFmt.Contains("fasta") ||
                            lowerFmt == "fa" ||
                            lowerFmt == "faa" ||
                            lowerFmt == "fna";

                        if (!matchesFormat)
                        {
                            _log.LogWarning("Skipping {Kind} dataset {DatasetId} because its format/name does not match required '{Required}' (name: {Name}, format: {Fmt})", kind, id, requiredFormat, lowerName, lowerFmt);
                            continue;
                        }
                    }

                    // Allow common FASTA extensions
                    var isFasta = lowerExt.Contains("fasta") || lowerExt == "fa" || lowerExt == "faa" || lowerExt == "fna";
                    if (!isFasta)
                    {
                        _log.LogWarning("Dataset {DatasetId} ({Kind}) has unexpected extension '{Ext}', forcing to fasta", id, kind, ext);
                    }

                    if (!isFasta)
                    {
                        try
                        {
                            await _galaxy.UpdateDatasetDatatypeAsync(id, "fasta", ct);
                        }
                        catch (GalaxyHttpException ex) when (
                            ex.StatusCode == 400 &&
                            ex.Body.Contains("used as input or output of a running job", StringComparison.OrdinalIgnoreCase))
                        {
                            _log.LogDebug(
                                "Dataset {DatasetId} is in use by a running Galaxy job; retaining its existing datatype '{Extension}'.",
                                id,
                                ext);
                        }
                        catch (Exception updEx)
                        {
                            _log.LogDebug(updEx, "Failed to update datatype for dataset {DatasetId}", id);
                        }
                    }

                    ready.Add(id);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to validate {Kind} dataset {DatasetId}", kind, id);
                }
            }
            return ready;
        }

        private async Task<bool> HasOrthogroupWorkflowMarkerAsync(string historyId, CancellationToken ct)
        {
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind != JsonValueKind.Array)
                    return false;

                return contents.EnumerateArray()
                    .Any(e =>
                        e.ValueKind == JsonValueKind.Object &&
                        e.TryGetProperty("name", out var nameProp) &&
                        (nameProp.GetString() ?? string.Empty).Contains("orthogroup_workflow_invoked", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to check workflow marker in history {HistoryId}", historyId);
                return false;
            }
        }

        private async Task<string?> GetWorkflowInvocationIdFromMarkerAsync(string historyId, CancellationToken ct)
        {
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind != JsonValueKind.Array)
                    return null;

                var marker = contents.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Where(e =>
                        e.TryGetProperty("name", out var nameProp) &&
                        (nameProp.GetString() ?? string.Empty).Contains("orthogroup_workflow_invoked", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.TryGetProperty("hid", out var hid) ? hid.GetInt32() : -1)
                    .FirstOrDefault();

                if (marker.ValueKind != JsonValueKind.Object)
                    return null;

                if (!marker.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String)
                    return null;

                var datasetId = idProp.GetString();
                if (string.IsNullOrWhiteSpace(datasetId))
                    return null;

                var content = await _galaxy.DownloadDatasetAsync(historyId, datasetId!, ct);
                var invocationId = ExtractInvocationId(content);
                return string.IsNullOrWhiteSpace(invocationId) ? null : invocationId;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Failed to read orthogroup workflow invocation marker in history {HistoryId}", historyId);
                return null;
            }
        }

        private sealed record RehydratedCollections(List<GenomeInfo> Genomes, string? ProteomeCollectionId, string? CdsCollectionId);

        private async Task<string?> FindSpeciesMappingDatasetInHistoryAsync(string historyId, CancellationToken ct)
        {
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind != JsonValueKind.Array)
                    return null;

                var candidate = contents.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => new
                    {
                        id = e.TryGetProperty("id", out var idProp) ? idProp.GetString() : null,
                        name = e.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty,
                        ext = e.TryGetProperty("extension", out var extProp) ? extProp.GetString() ?? string.Empty : string.Empty,
                        hid = e.TryGetProperty("hid", out var hidProp) ? hidProp.GetInt32() : -1,
                        type = e.TryGetProperty("history_content_type", out var hct) ? hct.GetString() ?? string.Empty : string.Empty
                    })
                    .Where(e =>
                        string.Equals(e.type, "dataset", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(e.id) &&
                        (e.name.Equals("species_mapping.tabular", StringComparison.OrdinalIgnoreCase) ||
                         e.name.Contains("species_mapping", StringComparison.OrdinalIgnoreCase) ||
                         e.name.Contains("species mapping", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(e => e.hid)
                    .FirstOrDefault();

                return candidate?.id;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to locate species mapping dataset in history {HistoryId}", historyId);
                return null;
            }
        }

        private async Task<string?> EnsureSpeciesMappingAsync(string historyId, List<GenomeInfo> genomes, CancellationToken ct)
        {
            var existing = await FindSpeciesMappingDatasetInHistoryAsync(historyId, ct);
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;

            if (genomes is null || genomes.Count == 0)
                return null;

            var mappings = genomes
                .Select(g => new
                {
                    Original = string.IsNullOrWhiteSpace(g.OriginalSpecies) ? g.Species : g.OriginalSpecies,
                    Standardized = string.IsNullOrWhiteSpace(g.StandardizedSpecies) ? SanitizeName(g.Species) : g.StandardizedSpecies
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Original))
                .Select(x => (x.Original!, x.Standardized ?? x.Original))
                .ToList();

            return await UploadSpeciesMappingAsync(historyId, mappings, ct);
        }

        private async Task<RehydratedCollections?> TryRehydrateGenomesFromCollectionsAsync(string historyId, CancellationToken ct)
        {
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind != JsonValueKind.Array)
                    return null;

                var collections = contents.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => new
                    {
                        id = e.TryGetProperty("id", out var idProp) ? idProp.GetString() : null,
                        name = e.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null,
                        type = e.TryGetProperty("history_content_type", out var hct) ? hct.GetString() : null,
                        deleted = e.TryGetProperty("deleted", out var del) && del.ValueKind == JsonValueKind.True,
                        hid = e.TryGetProperty("hid", out var hidProp) && hidProp.TryGetInt32(out var hidValue)
                            ? hidValue
                            : -1
                    })
                    .Where(e => !e.deleted && string.Equals(e.type, "dataset_collection", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Only the small species-level input collections can reconstruct the
                // genome set. Broad substring matching previously selected downstream
                // collections such as `orthogroup_cds_fastas` (potentially thousands
                // of elements), which made resume appear to hang and mixed analysis
                // generations in the recovered UI state.
                static bool IsNamed(string? value, params string[] expected) =>
                    !string.IsNullOrWhiteSpace(value) &&
                    expected.Any(name => value.Equals(name, StringComparison.OrdinalIgnoreCase));

                var proteomeEntry = collections
                    .Where(e => IsNamed(e.name, "Proteomes", "protein_genomes"))
                    .OrderByDescending(e => e.hid)
                    .FirstOrDefault();
                var cdsEntry = collections
                    .Where(e => IsNamed(e.name, "CDS", "cds_genomes"))
                    .OrderByDescending(e => e.hid)
                    .FirstOrDefault();

                if (proteomeEntry?.id is null)
                    return null;

                var proteomeDetails = await _galaxy.GetCollectionDetailsAsync(historyId, proteomeEntry.id, ct);
                if (proteomeDetails.ValueKind != JsonValueKind.Object || !proteomeDetails.TryGetProperty("elements", out var proteomeElements) || proteomeElements.ValueKind != JsonValueKind.Array)
                    return null;

                var proteomeFiles = proteomeElements
                    .EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => new
                    {
                        name = e.TryGetProperty("element_identifier", out var nameProp) ? nameProp.GetString() : null,
                        datasetId = e.TryGetProperty("object", out var obj) && obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("id", out var idProp) ? idProp.GetString() : null
                    })
                    .Where(e => !string.IsNullOrWhiteSpace(e.name) && !string.IsNullOrWhiteSpace(e.datasetId))
                    .ToList();

                if (proteomeFiles.Count is < MinSpeciesCount or > 10)
                {
                    _log.LogWarning(
                        "Ignoring collection {CollectionId} while resuming history {HistoryId}: expected a species-level proteome collection with {Min}-10 elements, found {Count}.",
                        proteomeEntry.id, historyId, MinSpeciesCount, proteomeFiles.Count);
                    return null;
                }

                var cdsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (cdsEntry?.id is not null)
                {
                    var cdsDetails = await _galaxy.GetCollectionDetailsAsync(historyId, cdsEntry.id, ct);
                    if (cdsDetails.ValueKind == JsonValueKind.Object && cdsDetails.TryGetProperty("elements", out var cdsElements) && cdsElements.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in cdsElements.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
                        {
                            var name = e.TryGetProperty("element_identifier", out var nameProp) ? nameProp.GetString() : null;
                            var id = e.TryGetProperty("object", out var obj) && obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
                            {
                                var key = SanitizeName(NormalizeRecoveredSpeciesName(name));
                                if (!string.IsNullOrWhiteSpace(key)) cdsMap[key] = id!;
                            }
                        }
                    }
                }

                if (proteomeFiles.Count == 0)
                    return null;

                var speciesNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var mappingDatasetId = await FindSpeciesMappingDatasetInHistoryAsync(historyId, ct);
                if (!string.IsNullOrWhiteSpace(mappingDatasetId))
                {
                    try
                    {
                        var mappingContent = await _galaxy.DownloadDatasetAsync(historyId, mappingDatasetId, ct);
                        foreach (var line in mappingContent.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
                        {
                            var cells = line.Split('\t');
                            if (cells.Length < 2 || string.IsNullOrWhiteSpace(cells[0]) || string.IsNullOrWhiteSpace(cells[1])) continue;
                            speciesNames[SanitizeName(cells[1])] = cells[0].Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "Could not read species mapping dataset {DatasetId}", mappingDatasetId);
                    }
                }

                var genomes = new List<GenomeInfo>();
                foreach (var file in proteomeFiles)
                {
                    var canonicalName = NormalizeRecoveredSpeciesName(file.name);
                    var speciesKey = SanitizeName(canonicalName);
                    var species = speciesNames.TryGetValue(speciesKey, out var mappedName) ? mappedName : canonicalName;
                    var proteinId = file.datasetId;
                    cdsMap.TryGetValue(speciesKey, out var cdsId);
                    if (string.IsNullOrWhiteSpace(proteinId))
                        continue;

                    var proteinUrl = BuildDatasetDownloadUrl(proteinId);
                    var cdsUrl = string.IsNullOrWhiteSpace(cdsId) ? string.Empty : BuildDatasetDownloadUrl(cdsId);

                    genomes.Add(new GenomeInfo(species, proteinUrl, proteinId, cdsUrl, cdsId ?? string.Empty)
                    {
                        OriginalSpecies = species,
                        StandardizedSpecies = speciesKey
                    });
                }

                return new RehydratedCollections(genomes, proteomeEntry.id, cdsEntry?.id);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to rehydrate genomes from existing collections in history {HistoryId}", historyId);
                return null;
            }
        }

        private async Task CreateOrthogroupWorkflowMarkerAsync(string historyId, string invocationId, CancellationToken ct)
        {
            try
            {
                var payload = $"orthogroup workflow invoked at {DateTimeOffset.UtcNow:o}, invocation: {invocationId}";
                var markerStamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                await _galaxy.UploadTextContentAsync(
                    historyId,
                    payload,
                    $"orthogroup_workflow_invoked_{markerStamp}.txt",
                    "txt",
                    ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to create workflow marker dataset for history {HistoryId}", historyId);
            }
        }

        private record HistoryJobCounts(int Running, int Queued, int Ok, int Failed, int Total);

        private async Task<HistoryJobCounts> GetLatestWorkflowJobCountsAsync(string historyId, CancellationToken ct)
        {
            var contents = await _galaxy.ListHistoryContentsAsync(
                historyId, ct, includeHidden: true, includeDeleted: false);
            if (contents.ValueKind != JsonValueKind.Array)
                return new HistoryJobCounts(0, 0, 0, 0, 0);

            var items = contents.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new
                {
                    Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    State = item.TryGetProperty("state", out var state) ? state.GetString() ?? string.Empty : string.Empty,
                    Hid = item.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var value) ? value : 0
                })
                .ToList();

            static bool NameHas(string name, params string[] terms) =>
                terms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));

            var markerItems = items
                .Where(item => NameHas(item.Name, "orthogroup_workflow_invoked"))
                .OrderByDescending(item => item.Hid)
                .ToList();
            var markerHids = markerItems
                .Select(item => item.Hid)
                .ToList();
            var latestMarkerHid = markerHids.FirstOrDefault();
            var latestPreparationHid = items
                .Where(item => NameHas(item.Name, "selected N0 orthogroups", "orthogroup_ids.zip", "selected orthogroups"))
                .Select(item => item.Hid)
                .DefaultIfEmpty(0)
                .Max();
            var latestMarkerIsHyphyRetry = markerItems.FirstOrDefault()?.Name.Contains(
                "hyphy_retry", StringComparison.OrdinalIgnoreCase) == true;
            var generationFloor = latestMarkerIsHyphyRetry
                ? latestMarkerHid
                : Math.Max(latestPreparationHid, markerHids.Skip(1).FirstOrDefault());

            var current = items.Where(item => item.Hid > generationFloor && NameHas(
                    item.Name,
                    "orthogroup_workflow_invoked",
                    "selected N0 orthogroups",
                    "orthogroup_ids.zip",
                    "mafft",
                    "back-translation",
                    "back translation",
                    "backtranslate",
                    "hyphy",
                    "busted",
                    "absrel",
                    "meme"))
                .ToList();

            static bool IsRunning(string state) => state is "running" or "waiting" or "upload" or "setting_metadata";
            static bool IsQueued(string state) => state is "new" or "queued";
            static bool IsFailed(string state) => state is "error" or "failed" or "discarded";

            return new HistoryJobCounts(
                current.Count(item => IsRunning(item.State)),
                current.Count(item => IsQueued(item.State)),
                current.Count(item => item.State.Equals("ok", StringComparison.OrdinalIgnoreCase)),
                current.Count(item => IsFailed(item.State)),
                current.Count);
        }

        // Legacy metadata refresh removed; status is inferred live from Galaxy datasets, collections, and jobs.

        public async Task<List<ResultArtifact>> GetResultArtifactsAsync(
            string historyId,
            string? galaxyApiKey,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(historyId))
                throw new ArgumentException("A Galaxy history ID is required.", nameof(historyId));
            if (string.IsNullOrWhiteSpace(galaxyApiKey))
                throw new ArgumentException("A Galaxy API key is required for this history.", nameof(galaxyApiKey));

            using var scope = _galaxy.UseApiKey(galaxyApiKey);
            using var artifactHistoryCache = _galaxy.UseHistoryReadCache();
            await _galaxy.PrimeHistoryContentsCacheAsync(historyId, ct);
            var contents = await _galaxy.ListHistoryContentsAsync(
                historyId, ct, includeHidden: true, includeDeleted: false);
            if (contents.ValueKind != JsonValueKind.Array)
                return new List<ResultArtifact>();

            static string? ClassifyCollection(string name)
            {
                var lower = name.ToLowerInvariant();
                if (lower.Contains("hyphy-ready codon") ||
                    lower.Contains("codon_align") ||
                    lower.Contains("codon align") ||
                    lower.Contains("tranalign"))
                    return "codon_alignment";
                if (lower.Contains("hyphy-ready gene tree") ||
                    lower.Contains("gene_tree") ||
                    lower.Contains("gene tree") ||
                    (lower.Contains("iq-tree") && !lower.Contains("report")))
                    return "gene_tree";
                if (lower.Contains("protein_alignment") || lower.Contains("protein alignment"))
                    return "protein_alignment";
                return null;
            }

            var latestCollections = contents.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Where(item => item.TryGetProperty("history_content_type", out var type) &&
                    string.Equals(type.GetString(), "dataset_collection", StringComparison.OrdinalIgnoreCase))
                .Select(item => new
                {
                    Item = item.Clone(),
                    Id = item.TryGetProperty("id", out var id) ? id.GetString() : null,
                    Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    Hid = item.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var value) ? value : 0
                })
                .Select(item => new
                {
                    item.Item,
                    item.Id,
                    item.Name,
                    item.Hid,
                    Kind = ClassifyCollection(item.Name)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && item.Kind is not null)
                .GroupBy(item => item.Kind!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Hid).First())
                .ToList();

            var artifacts = new List<ResultArtifact>();
            foreach (var collection in latestCollections)
            {
                JsonElement details;
                try
                {
                    details = await _galaxy.GetCollectionDetailsAsync(historyId, collection.Id!, ct);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Unable to inspect result collection {CollectionId}", collection.Id);
                    continue;
                }

                if (details.ValueKind != JsonValueKind.Object ||
                    !details.TryGetProperty("elements", out var elements) ||
                    elements.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var element in elements.EnumerateArray())
                {
                    var identifier = element.TryGetProperty("element_identifier", out var identifierNode)
                        ? identifierNode.GetString()
                        : null;
                    var objectNode = element.TryGetProperty("object", out var objectValue) &&
                        objectValue.ValueKind == JsonValueKind.Object
                            ? objectValue
                            : element;
                    var datasetId = objectNode.TryGetProperty("id", out var idNode)
                        ? idNode.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(datasetId) || string.IsNullOrWhiteSpace(identifier))
                        continue;

                    var name = objectNode.TryGetProperty("name", out var nameNode)
                        ? nameNode.GetString() ?? identifier!
                        : identifier!;
                    var extension = objectNode.TryGetProperty("extension", out var extensionNode)
                        ? extensionNode.GetString()
                        : null;
                    var state = objectNode.TryGetProperty("state", out var stateNode)
                        ? stateNode.GetString() ?? "unknown"
                        : "unknown";
                    artifacts.Add(new ResultArtifact(
                        datasetId!,
                        name,
                        identifier!,
                        collection.Kind!,
                        null,
                        extension,
                        state,
                        collection.Name));
                }
            }

            return artifacts;
        }

        private async Task<(List<HyphyOutput> Markdown, List<HyphyOutput> Json)> FindHyphyOutputsInHistoryAsync(string historyId, CancellationToken ct)
        {
            try
            {
                var contents = await _galaxy.ListHistoryContentsAsync(historyId, ct, includeHidden: true, includeDeleted: false);
                if (contents.ValueKind != JsonValueKind.Array)
                    return (new List<HyphyOutput>(), new List<HyphyOutput>());

                var markdown = new List<HyphyOutput>();
                var json = new List<HyphyOutput>();
                var collectionOutputMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void AddUnique(List<HyphyOutput> list, HyphyOutput item)
                {
                    var match = Regex.Match(item.ElementIdentifier ?? string.Empty, @"\bN\d+\.HOG\d+\b", RegexOptions.IgnoreCase);
                    if (!match.Success) return;
                    item = item with { ElementIdentifier = match.Value.ToUpperInvariant() };
                    if (list.Any(existing =>
                            existing.Id == item.Id ||
                            (!string.IsNullOrWhiteSpace(existing.ElementIdentifier) &&
                             existing.ElementIdentifier == item.ElementIdentifier &&
                             string.Equals(existing.Method, item.Method, StringComparison.OrdinalIgnoreCase))))
                        return;
                    var label = item.ElementIdentifier ?? item.Name;
                    list.Add(item with
                    {
                        Name = string.IsNullOrWhiteSpace(item.Method) ? label : $"{item.Method} - {label}"
                    });
                }

                var orderedItems = contents.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .OrderByDescending(e => e.TryGetProperty("hid", out var hid) ? hid.GetInt32() : -1)
                    .Select(e => e.Clone())
                    .ToList();

                var latestPreparationHid = orderedItems
                    .Where(e => e.TryGetProperty("name", out var nameProp) &&
                        ((nameProp.GetString() ?? string.Empty).Contains("selected N0 orthogroups", StringComparison.OrdinalIgnoreCase) ||
                         (nameProp.GetString() ?? string.Empty).Contains("selected orthogroups", StringComparison.OrdinalIgnoreCase) ||
                         (nameProp.GetString() ?? string.Empty).Contains("orthogroup_ids.zip", StringComparison.OrdinalIgnoreCase)))
                    .Select(e => e.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var value) ? value : 0)
                    .DefaultIfEmpty(0)
                    .Max();

                static string SelectionKind(string name) =>
                    name.Contains("absrel", StringComparison.OrdinalIgnoreCase) ? "absrel" :
                    name.Contains("busted", StringComparison.OrdinalIgnoreCase) ? "busted" :
                    name.Contains("meme", StringComparison.OrdinalIgnoreCase) ? "meme" : "hyphy";

                static bool IsHyphyCollection(JsonElement item)
                {
                    var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                    var namedSelection = SelectionKind(name) != "hyphy";
                    var typedSelection = item.TryGetProperty("elements_datatypes", out var types) &&
                        types.ValueKind == JsonValueKind.Array &&
                        types.EnumerateArray().Any(type => type.GetString()?.Equals("hyphy_results.json", StringComparison.OrdinalIgnoreCase) == true);
                    return namedSelection || typedSelection;
                }

                static string SelectionOutputKind(JsonElement item)
                {
                    if (item.TryGetProperty("elements_datatypes", out var types) &&
                        types.ValueKind == JsonValueKind.Array)
                    {
                        if (types.EnumerateArray().Any(type =>
                                type.GetString()?.Equals("hyphy_results.json", StringComparison.OrdinalIgnoreCase) == true))
                            return "json";
                        if (types.EnumerateArray().Any(type =>
                                type.GetString()?.Equals("markdown", StringComparison.OrdinalIgnoreCase) == true))
                            return "markdown";
                    }
                    var name = item.TryGetProperty("name", out var nameNode)
                        ? nameNode.GetString() ?? string.Empty
                        : string.Empty;
                    return name.Contains("report", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("markdown", StringComparison.OrdinalIgnoreCase)
                        ? "markdown"
                        : "json";
                }

                var selectionCollections = orderedItems
                    .Where(e =>
                        e.TryGetProperty("history_content_type", out var hct) &&
                        string.Equals(hct.GetString(), "dataset_collection", StringComparison.OrdinalIgnoreCase) &&
                        IsHyphyCollection(e))
                    .Where(e => latestPreparationHid == 0 ||
                        (e.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var value) && value > latestPreparationHid))
                    .ToList();

                // A resumed history may contain many failed/retried workflow generations.
                // Inspect only the newest result collection for each current HyPhy test.
                if (selectionCollections.Count == 0)
                {
                    selectionCollections = orderedItems
                        .Where(e =>
                            e.TryGetProperty("history_content_type", out var hct) &&
                            string.Equals(hct.GetString(), "dataset_collection", StringComparison.OrdinalIgnoreCase) &&
                            IsHyphyCollection(e))
                        .ToList();
                }
                // Retain only the latest collection per method and output type.
                // Markdown and JSON are separate outputs, not separate gene batches.
                var selectedCollectionIds = selectionCollections
                    .GroupBy(e =>
                        $"{SelectionKind(e.GetProperty("name").GetString() ?? string.Empty)}:{SelectionOutputKind(e)}")
                    .Select(group => group.First())
                    .Where(e => e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetProperty("id").GetString()!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var item in orderedItems)
                {
                    var ext = item.TryGetProperty("extension", out var extProp) ? extProp.GetString() ?? string.Empty : string.Empty;
                    var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                    var elementIdentTop = item.TryGetProperty("element_identifier", out var elemIdProp) ? elemIdProp.GetString() : null;
                    var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    var contentType = item.TryGetProperty("history_content_type", out var hct) ? hct.GetString() : "dataset";
                    var lowerName = (name ?? string.Empty).ToLowerInvariant();

                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    if (string.Equals(contentType, "dataset_collection", StringComparison.OrdinalIgnoreCase))
                    {
                        // Only inspect collections that contain hyphy outputs
                        var hasHyphyDatatype = item.TryGetProperty("elements_datatypes", out var elemTypes) &&
                                               elemTypes.ValueKind == JsonValueKind.Array &&
                                               elemTypes.EnumerateArray().Any(t => t.GetString()?.Equals("hyphy_results.json", StringComparison.OrdinalIgnoreCase) == true);
                        var hasMarkdownDatatype = item.TryGetProperty("elements_datatypes", out var markdownTypes) &&
                                                  markdownTypes.ValueKind == JsonValueKind.Array &&
                                                  markdownTypes.EnumerateArray().Any(t => t.GetString()?.Equals("markdown", StringComparison.OrdinalIgnoreCase) == true);
                        var isSelectionCollection = lowerName.Contains("busted") || lowerName.Contains("absrel") || lowerName.Contains("meme");
                        if (!hasHyphyDatatype && !isSelectionCollection)
                            continue;
                        if (!selectedCollectionIds.Contains(id))
                            continue;

                        // Dive into collection elements (list of markdown/json)
                        try
                        {
                            // Prefer contents_url for accurate element identifiers
                            JsonElement? collectionContents = null;
                            if (item.TryGetProperty("contents_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                            {
                                try { collectionContents = await _galaxy.GetCollectionContentsAsync(urlProp.GetString()!, ct); } catch { /* fall back */ }
                            }

                            JsonElement collectionElements = default;
                            if (collectionContents.HasValue)
                            {
                                if (collectionContents.Value.ValueKind == JsonValueKind.Array)
                                {
                                    collectionElements = collectionContents.Value;
                                }
                                else if (collectionContents.Value.ValueKind == JsonValueKind.Object && collectionContents.Value.TryGetProperty("elements", out var eObj))
                                {
                                    collectionElements = eObj;
                                }
                            }
                            if (collectionElements.ValueKind == JsonValueKind.Undefined || collectionElements.ValueKind == JsonValueKind.Null)
                            {
                                var details = await _galaxy.GetCollectionDetailsAsync(historyId, id, ct);
                                if (details.ValueKind == JsonValueKind.Object && details.TryGetProperty("elements", out var e))
                                    collectionElements = e;
                            }
                            // Final fallback: explicit histories/.../contents/dataset_collections/.../contents
                            if (collectionElements.ValueKind == JsonValueKind.Undefined || collectionElements.ValueKind == JsonValueKind.Null)
                            {
                                try
                                {
                                    var altUrl = $"/api/histories/{Uri.EscapeDataString(historyId)}/contents/dataset_collections/{Uri.EscapeDataString(id)}/contents";
                                    var alt = await _galaxy.GetCollectionContentsAsync(altUrl, ct);
                                    if (alt.ValueKind == JsonValueKind.Array)
                                        collectionElements = alt;
                                    else if (alt.ValueKind == JsonValueKind.Object && alt.TryGetProperty("elements", out var eAlt))
                                        collectionElements = eAlt;
                                }
                                catch { /* ignore */ }
                            }

                            if (collectionElements.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var el in collectionElements.EnumerateArray())
                                {
                                    var elId = el.TryGetProperty("object", out var obj) && obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("id", out var objId)
                                        ? objId.GetString()
                                        : el.TryGetProperty("id", out var eid) ? eid.GetString() : null;
                                    var elName = el.TryGetProperty("element_identifier", out var ident) ? ident.GetString() : null;
                                    var elExt = el.TryGetProperty("object", out var obj2) && obj2.ValueKind == JsonValueKind.Object && obj2.TryGetProperty("extension", out var objExt)
                                        ? objExt.GetString()
                                        : el.TryGetProperty("element_type", out var et) ? et.GetString() : null;

                                    if (string.IsNullOrWhiteSpace(elId))
                                        continue;

                                    // A collection's name identifies the tool and
                                    // batch, not a gene. Only a child element
                                    // identifier may identify the analyzed gene.
                                    var elementLabel = elName;
                                    if (string.IsNullOrWhiteSpace(elementLabel) &&
                                        el.TryGetProperty("object", out var childObject) &&
                                        childObject.ValueKind == JsonValueKind.Object &&
                                        childObject.TryGetProperty("name", out var childName))
                                    {
                                        elementLabel = childName.GetString();
                                    }
                                    if (string.IsNullOrWhiteSpace(elementLabel))
                                        continue;
                                    // HyPhy collection labels are not genes. A
                                    // valid child for the gene-specific view must
                                    // carry an OrthoFinder HOG identifier.
                                    var hogMatch = Regex.Match(elementLabel, @"N\d+\.HOG\d+", RegexOptions.IgnoreCase);
                                    if (!hogMatch.Success)
                                        continue;
                                    elementLabel = hogMatch.Value.ToUpperInvariant();
                                    var lowerLabel = elementLabel.ToLowerInvariant();
                                    // Skip collection metadata like README/md5 files
                                    if (lowerLabel is "readme" or "readme.md" or "md5sum" or "md5sum.txt")
                                        continue;

                                    var colLowerExt = (elExt ?? string.Empty).ToLowerInvariant();
                                    var colLooksMarkdown = colLowerExt.Contains("markdown") || lowerLabel.EndsWith(".md") || hasMarkdownDatatype;
                                    var colLooksJson = colLowerExt.Contains("hyphy_results.json") || lowerLabel.EndsWith(".json") ||
                                                       (isSelectionCollection && !colLooksMarkdown);

                                    var method = SelectionKind(name) switch
                                    {
                                        "absrel" => "aBSREL",
                                        "busted" => "BUSTED",
                                        "meme" => "MEME",
                                        _ => "HyPhy"
                                    };
                                    collectionOutputMethods.Add(method);
                                    if (colLooksMarkdown)
                                        AddUnique(markdown, new HyphyOutput(elId, elementLabel, elementLabel, method, obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("state", out var ms) ? ms.GetString() : null));
                                    else if (colLooksJson)
                                        AddUnique(json, new HyphyOutput(elId, elementLabel, elementLabel, method, obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("state", out var js) ? js.GetString() : null));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Failed to inspect collection {CollectionId} for HYPHY outputs", id);
                        }

                        continue;
                    }

                    // Galaxy exposes mapped collection outputs both through the collection and
                    // as standalone HDAs. Some large histories omit collection element metadata,
                    // so retain successful native HyPhy datasets from the current generation.
                    var state = item.TryGetProperty("state", out var stateProp)
                        ? stateProp.GetString()
                        : null;
                    var hid = item.TryGetProperty("hid", out var hidProp) && hidProp.TryGetInt32(out var hidValue)
                        ? hidValue
                        : 0;
                    var standaloneOutput = ClassifyStandaloneHyphyOutput(name, ext, state);
                    if (standaloneOutput is not null &&
                        !selectionCollections.Any(collection => string.Equals(
                            SelectionKind(collection.GetProperty("name").GetString() ?? string.Empty),
                            SelectionKind(standaloneOutput.Value.Method), StringComparison.OrdinalIgnoreCase)) &&
                        !collectionOutputMethods.Contains(standaloneOutput.Value.Method) &&
                        (latestPreparationHid == 0 || hid > latestPreparationHid))
                    {
                        var elementLabel = elementIdentTop ?? name;
                        var output = new HyphyOutput(id, name, elementLabel, standaloneOutput.Value.Method);
                        if (standaloneOutput.Value.Kind == "markdown")
                            AddUnique(markdown, output);
                        else
                            AddUnique(json, output);
                    }

                    continue;
                }

                return (markdown, json);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Unable to scan history {HistoryId} for HYPHY outputs", historyId);
                return (new List<HyphyOutput>(), new List<HyphyOutput>());
            }
        }

        private async Task<string> FilterProteinByIdTableAsync(string historyId, string proteinDatasetId, string idTableDatasetId, string species, CancellationToken ct)
        {
            const string seqFilterToolId = "toolshed.g2.bx.psu.edu/repos/peterjc/seq_filter_by_id/seq_filter_by_id/0.2.9";
            var inputs = new Dictionary<string, object>
            {
                ["input_file"] = new { src = "hda", id = proteinDatasetId },
                ["filter_type"] = "tabular",
                ["tabular_file"] = new { src = "hda", id = idTableDatasetId },
                ["column_list"] = "1",
                ["id_opts|columns"] = "1",
                ["sense"] = "pos"
            };

            try
            {
                _log.LogDebug("Filtering protein FASTA for {Species} using ID table {IdTable}", species, idTableDatasetId);
                var (jobId, raw) = await _galaxy.InvokeToolAsync(historyId, seqFilterToolId, inputs, ct);
                var outputId = await ResolveOutputDatasetId(historyId, raw, ct);
                if (!string.IsNullOrWhiteSpace(jobId))
                    await WaitForJobCompletionAsync(jobId, ct);
                return outputId;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to filter protein FASTA for {Species} using ID table {IdTable}", species, idTableDatasetId);
                return proteinDatasetId;
            }
        }

        private async Task<string> ConcatenateFastasAsync(string historyId, List<string> fastaDatasetIds, string targetName, CancellationToken ct)
        {
            var inputs = BuildConcatenateInputs(fastaDatasetIds);

            var (jobId, raw) = await _galaxy.InvokeToolAsync(historyId, "cat1", inputs, ct);
            var outputId = await ResolveOutputDatasetId(historyId, raw, ct);
            if (string.IsNullOrWhiteSpace(outputId))
                throw new InvalidOperationException("cat1 did not produce an output dataset");

            if (!string.IsNullOrWhiteSpace(jobId))
                await WaitForJobCompletionAsync(jobId, ct);

            if (!string.IsNullOrWhiteSpace(targetName))
                await _galaxy.RenameHistoryContentAsync(historyId, outputId, targetName, ct);

            return outputId;
        }

        internal static Dictionary<string, object> BuildConcatenateInputs(IEnumerable<string>? fastaDatasetIds)
        {
            var datasetIds = (fastaDatasetIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (datasetIds.Count == 0)
                throw new InvalidOperationException("No FASTA datasets provided to concatenate");

            // Galaxy's built-in cat1 tool has one required input1 dataset and a
            // repeated queries/input2 dataset parameter. Supplying an array to
            // input1 is rejected because input1 itself is not a multi-data input.
            var inputs = new Dictionary<string, object>
            {
                ["input1"] = new { src = "hda", id = datasetIds[0] }
            };
            for (var index = 1; index < datasetIds.Count; index++)
            {
                inputs[$"queries_{index - 1}|input2"] = new { src = "hda", id = datasetIds[index] };
            }

            return inputs;
        }

        private string? ExtractDatasetIdFromCollection(JsonElement collectionDetails, IEnumerable<string>? preferredIdentifiers)
        {
            if (!collectionDetails.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
                return null;

            var preferred = preferredIdentifiers?.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).ToList() ?? new List<string>();
            string? fallbackId = null;

            foreach (var element in elements.EnumerateArray())
            {
                var identifier = element.TryGetProperty("element_identifier", out var idProp) ? idProp.GetString() : null;
                if (!element.TryGetProperty("object", out var obj) || obj.ValueKind != JsonValueKind.Object)
                    continue;

                string? datasetId = null;
                if (obj.TryGetProperty("id", out var dsIdProp))
                    datasetId = dsIdProp.GetString();
                else if (obj.TryGetProperty("dataset_id", out var dsAltProp))
                    datasetId = dsAltProp.GetString();

                if (string.IsNullOrWhiteSpace(datasetId))
                    continue;

                if (preferred.Count > 0 && !string.IsNullOrWhiteSpace(identifier))
                {
                    foreach (var candidate in preferred)
                    {
                        if (identifier.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                            identifier.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return datasetId;
                        }
                    }
                }

                fallbackId ??= datasetId;
            }

            return fallbackId;
        }

        private string? FindOrthoGroupsOutputDataset(JsonElement outputs)
        {
            // Look for the orthologous groups output file
            if (outputs.ValueKind == JsonValueKind.Array)
            {
                foreach (var output in outputs.EnumerateArray())
                {
                    if (output.TryGetProperty("label", out var label) && 
                        label.GetString()?.Contains("orthogroups") == true &&
                        label.GetString()?.Contains("tsv") == true)
                    {
                        if (output.TryGetProperty("id", out var id))
                        {
                            return id.GetString();
                        }
                    }
                }
            }
            return null;
        }

        private IReadOnlyList<OrthologousGroup> ParseOrthologousGroups(string content)
        {
            var groups = new List<OrthologousGroup>();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length >= 2)
                {
                    var groupId = parts[0].Trim();
                    var geneList = parts[1].Trim();
                    
                    // Parse species-specific genes (format: Species1: gene1, gene2 Species2: gene3, gene4)
                    var speciesGenes = ParseSpeciesGenes(geneList);
                    var allGenes = speciesGenes.Values.SelectMany(genes => genes).ToList();
                    
                    if (allGenes.Count >= 3) // Only include groups with genes from at least 3 species
                    {
                        groups.Add(new OrthologousGroup(
                            groupId, 
                            $"Orthogroup {groupId}", 
                            allGenes,
                            speciesGenes.ToDictionary(kv => kv.Key, kv => string.Join(",", kv.Value))
                        ));
                    }
                }
            }

            return groups;
        }

        private Dictionary<string, List<string>> ParseSpeciesGenes(string geneList)
        {
            var result = new Dictionary<string, List<string>>();
            
            // Simple parser for "Species1: gene1, gene2 Species2: gene3" format
            var speciesParts = geneList.Split(' ');
            string? currentSpecies = null;
            
            foreach (var part in speciesParts)
            {
                if (part.Contains(':'))
                {
                    var speciesAndGene = part.Split(':');
                    currentSpecies = speciesAndGene[0].Trim();
                    if (!result.ContainsKey(currentSpecies))
                        result[currentSpecies] = new List<string>();
                    
                    if (speciesAndGene.Length > 1 && !string.IsNullOrWhiteSpace(speciesAndGene[1]))
                    {
                        result[currentSpecies].Add(speciesAndGene[1].Trim(',').Trim());
                    }
                }
                else if (currentSpecies != null && !string.IsNullOrWhiteSpace(part))
                {
                    result[currentSpecies].Add(part.Trim(',').Trim());
                }
            }
            
            return result;
        }

        private string? FindCollectionInputParam(JsonElement inputsNode)
        {
            foreach (var p in FlattenInputs(inputsNode))
            {
                if (p.TryGetProperty("type", out var t) && 
                    (t.GetString() == "data_collection" || t.GetString() == "collection") && 
                    p.TryGetProperty("name", out var n))
                {
                    return n.GetString();
                }
            }
            return null;
        }

        // Reuse helper methods from GalaxyBustedPipeline
        private async Task<string?> ResolveToolIdAsync(string[] tokens, CancellationToken ct)
        {
            var results = await _galaxy.SearchToolsAsync(string.Join(' ', tokens), ct);
            var first = results.FirstOrDefault();
            return first == default ? null : first.Id;
        }

        private static IEnumerable<JsonElement> FlattenInputs(JsonElement inputsNode)
        {
            if (inputsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in inputsNode.EnumerateArray())
                {
                    yield return p;
                    if (p.TryGetProperty("inputs", out var child) && child.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in FlattenInputs(child)) yield return c;
                    }
                    if (p.TryGetProperty("cases", out var cases) && cases.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cs in cases.EnumerateArray())
                        {
                            if (cs.TryGetProperty("inputs", out var ci) && ci.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var c in FlattenInputs(ci)) yield return c;
                            }
                        }
                    }
                }
            }
        }

        private static JsonElement GetInputsArrayOrThrow(JsonElement build, string toolId)
        {
            if (build.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"Tool build for '{toolId}' did not return an object (got {build.ValueKind}). Check tool id and permissions.");
            if (!build.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"Tool '{toolId}' did not expose an inputs array. Response: {build.ToString()?.Substring(0, Math.Min(400, build.ToString()?.Length ?? 0))}");
            return inputs;
        }

        private async Task<string> ResolveOutputDatasetId(string historyId, JsonElement raw, CancellationToken ct)
        {
            try
            {
                if (raw.ValueKind == JsonValueKind.Object)
                {
                    if (raw.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array && outs.GetArrayLength() > 0)
                        return outs[0].GetProperty("id").GetString()!;
                    if (raw.TryGetProperty("output_collections", out var oc) && oc.ValueKind == JsonValueKind.Array && oc.GetArrayLength() > 0)
                        return oc[0].GetProperty("id").GetString()!;
                }
            }
            catch { /* fall through to history scan */ }

            var hist = await _galaxy.ListHistoryContentsAsync(historyId, ct);
            if (hist.ValueKind == JsonValueKind.Array)
            {
                var latest = hist.EnumerateArray()
                    .Where(e => e.TryGetProperty("history_content_type", out var hct) && hct.GetString() == "dataset")
                    .OrderByDescending(e => e.TryGetProperty("hid", out var hid) ? hid.GetInt32() : -1)
                    .FirstOrDefault();
                if (latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty("id", out var idProp))
                    return idProp.GetString()!;
            }
            throw new InvalidOperationException("Could not resolve output dataset id from tool response or history");
        }

        // Include other helper methods from GalaxyBustedPipeline for merge, translate, align, etc.
        private async Task<string> MergeFastaAsync(string historyId, List<string> datasetIds, CancellationToken ct)
        {
            // Force the Galaxy fasta_merge_files_and_filter_unique_sequences tool
            var mergeToolId = "toolshed.g2.bx.psu.edu/repos/galaxyp/fasta_merge_files_and_filter_unique_sequences/fasta_merge_files_and_filter_unique_sequences/1.2.0";

            datasetIds = datasetIds.Distinct().ToList();
            var build = await _galaxy.GetToolBuildAsync(mergeToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, mergeToolId);

            var inputs = new Dictionary<string, object>
            {
                // The tool accepts multiple inputs via "input1" (repeat parameter)
                ["input_fasta"] = datasetIds
                    .Select(id => new { src = "hda", id })
                    .ToArray(),

                // Explicitly merge into ONE output FASTA
                ["run_mode"] = "merge_all",

                // Disable deduplication
                ["unique_filter"] = "no",

                // Default accession parser (must match wrapper exactly)
                ["accession_parser"] = "^>([^ ]+).*$"
            };

            var (_, raw) = await _galaxy.InvokeToolAsync(historyId, mergeToolId, inputs, ct);
            return await ResolveOutputDatasetId(historyId, raw, ct);
        }

        private async Task<string> TranslateAsync(string historyId, string fastaId, CancellationToken ct)
        {
            var translateToolId = _opts.TranslateToolId
                ?? await ResolveToolIdAsync(new[] { "emboss_transeq" }, ct)
                ?? await ResolveToolIdAsync(new[] { "transeq" }, ct);
            if (translateToolId is null) throw new InvalidOperationException("Translate tool not found (transeq)");
            
            var build = await _galaxy.GetToolBuildAsync(translateToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, translateToolId);
            var paramId = FindSingleDataParamRecursive(inputsNode) ?? "input";
            var inputs = new Dictionary<string, object> { [paramId] = new { src = "hda", id = fastaId } };
            var (_, raw) = await _galaxy.InvokeToolAsync(historyId, translateToolId, inputs, ct);
            return await ResolveOutputDatasetId(historyId, raw, ct);
        }

        // Prefer the requested Galaxy tool 'translate_nucleotides' to produce amino acid FASTA from nucleotide input
        private async Task<string> TranslateNucleotidesAsync(string historyId, string fastaId, CancellationToken ct)
        {
            var toolId = await ResolveToolIdAsync(new[] { "translate_nucleotides" }, ct)
                        ?? _opts.TranslateToolId
                        ?? await ResolveToolIdAsync(new[] { "emboss_transeq" }, ct)
                        ?? await ResolveToolIdAsync(new[] { "transeq" }, ct);

            if (toolId is null)
                throw new InvalidOperationException("Translate tool not found (translate_nucleotides/transeq)");

            var build = await _galaxy.GetToolBuildAsync(toolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, toolId);
            var paramId = FindSingleDataParamRecursive(inputsNode) ?? "input";
            var inputs = new Dictionary<string, object> { [paramId] = new { src = "hda", id = fastaId } };

            // Some translate tools may accept frame/genetic code; rely on defaults for now
            var (_, raw) = await _galaxy.InvokeToolAsync(historyId, toolId, inputs, ct);
            return await ResolveOutputDatasetId(historyId, raw, ct);
        }

        private async Task<string> AlignAsync(string historyId, string aaMultiId, string alignment, CancellationToken ct)
        {
            alignment = (alignment ?? "mafft").Trim().ToLowerInvariant();
            switch (alignment)
            {
                case "muscle":
                    return await MuscleAsync(historyId, aaMultiId, ct);
                case "mafft":
                default:
                    return await MafftAsync(historyId, aaMultiId, ct);
            }
        }

        private async Task<string> MafftAsync(string historyId, string aaMultiId, CancellationToken ct)
        {
            var mafftToolId = _opts.MafftToolId ?? await ResolveToolIdAsync(new[] { "mafft" }, ct);
            if (mafftToolId is null) throw new InvalidOperationException("MAFFT tool not found");
            var build = await _galaxy.GetToolBuildAsync(mafftToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, mafftToolId);
            var paramId = FindSingleDataParamRecursive(inputsNode) ?? "input";
            var inputs = new Dictionary<string, object> { [paramId] = new { src = "hda", id = aaMultiId } };
            var (jobId, raw) = await _galaxy.InvokeToolAsync(historyId, mafftToolId, inputs, ct);
            var outputId = await ResolveOutputDatasetId(historyId, raw, ct);
            await WaitForJobCompletionAsync(jobId, ct);
            await WaitForDatasetReadyAsync(historyId, outputId, ct);
            return outputId;
        }

        private async Task<string> MuscleAsync(string historyId, string aaMultiId, CancellationToken ct)
        {
            var muscleToolId = _opts.MuscleToolId ?? await ResolveToolIdAsync(new[] { "muscle" }, ct);
            if (muscleToolId is null) throw new InvalidOperationException("MUSCLE tool not found");
            var build = await _galaxy.GetToolBuildAsync(muscleToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, muscleToolId);
            var paramId = FindSingleDataParamRecursive(inputsNode) ?? "input";
            var inputs = new Dictionary<string, object> { [paramId] = new { src = "hda", id = aaMultiId } };
            var (jobId, raw) = await _galaxy.InvokeToolAsync(historyId, muscleToolId, inputs, ct);
            var outputId = await ResolveOutputDatasetId(historyId, raw, ct);
            await WaitForJobCompletionAsync(jobId, ct);
            await WaitForDatasetReadyAsync(historyId, outputId, ct);
            return outputId;
        }

        private async Task<string> AlignBackTransAsync(string historyId, string aaAlignedId, string ntMultiId, CancellationToken ct)
        {
            var abtId = _opts.AlignBackTransToolId;
            JsonElement build = default;
            if (!string.IsNullOrWhiteSpace(abtId))
            {
                try
                {
                    build = await _galaxy.GetToolBuildAsync(abtId, historyId, ct);
                }
                catch (GalaxyHttpException ex) when (ex.StatusCode == 404)
                {
                    _log.LogWarning("Configured back-translation tool {ToolId} is unavailable; trying installed alternatives.", abtId);
                    abtId = null;
                }
            }

            if (string.IsNullOrWhiteSpace(abtId))
            {
                abtId = await ResolveToolIdAsync(new[] { "align_back_trans" }, ct);
                if (!string.IsNullOrWhiteSpace(abtId))
                    build = await _galaxy.GetToolBuildAsync(abtId, historyId, ct);
            }

            if (string.IsNullOrWhiteSpace(abtId))
            {
                var tranalignId = await ResolveToolIdAsync(new[] { "tranalign100", "tranalign" }, ct);
                if (string.IsNullOrWhiteSpace(tranalignId))
                    throw new InvalidOperationException("No supported codon back-translation tool is installed (align_back_trans or EMBOSS tranalign).");

                var tranalignInputs = new Dictionary<string, object>
                {
                    ["input1"] = new { src = "hda", id = ntMultiId },
                    ["input2"] = new { src = "hda", id = aaAlignedId },
                    ["table"] = "0",
                    ["out_format1"] = "fasta"
                };
                var (tranalignJobId, tranalignRaw) = await _galaxy.InvokeToolAsync(historyId, tranalignId, tranalignInputs, ct);
                var tranalignOutputId = await ResolveOutputDatasetId(historyId, tranalignRaw, ct);
                await WaitForJobCompletionAsync(tranalignJobId, ct);
                await WaitForDatasetReadyAsync(historyId, tranalignOutputId, ct);
                return tranalignOutputId;
            }

            var inputsNode = GetInputsArrayOrThrow(build, abtId);
            var dataParams = FindDataParamsRecursive(inputsNode).Distinct().ToList();
            if (dataParams.Count < 2) throw new InvalidOperationException("align_back_trans expected two data params");
            var proteinParam = dataParams.FirstOrDefault(p => p.Equals("prot_align", StringComparison.OrdinalIgnoreCase))
                ?? dataParams.FirstOrDefault(p =>
                p.Contains("prot", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("align", StringComparison.OrdinalIgnoreCase));
            var nucleotideParam = dataParams.FirstOrDefault(p => p.Equals("nuc_file", StringComparison.OrdinalIgnoreCase))
                ?? dataParams.FirstOrDefault(p =>
                p.Contains("nuc", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("cds", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("dna", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(proteinParam) || string.IsNullOrWhiteSpace(nucleotideParam) ||
                string.Equals(proteinParam, nucleotideParam, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Could not identify align_back_trans protein and nucleotide inputs. Galaxy exposed: {string.Join(", ", dataParams)}");
            var inputs = new Dictionary<string, object>
            {
                [proteinParam] = new { src = "hda", id = aaAlignedId },
                [nucleotideParam] = new { src = "hda", id = ntMultiId },
                ["table"] = "1",
            };
            var (jobId, raw) = await _galaxy.InvokeToolAsync(historyId, abtId, inputs, ct);
            var outputId = await ResolveOutputDatasetId(historyId, raw, ct);
            await WaitForJobCompletionAsync(jobId, ct);
            await WaitForDatasetReadyAsync(historyId, outputId, ct);
            return outputId;
        }

        private async Task<(string jobId, JsonElement raw)> RunBustedAsync(string historyId, string codonAlignedId, CancellationToken ct)
        {
            var build = await _galaxy.GetToolBuildAsync(_opts.DnDsToolId!, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, _opts.DnDsToolId!);
            var param = FindDataParamsRecursive(inputsNode).FirstOrDefault() ?? "input_file";
            var inputs = new Dictionary<string, object> { [param] = new { src = "hda", id = codonAlignedId } };
            return await _galaxy.InvokeToolAsync(historyId, _opts.DnDsToolId!, inputs, ct);
        }

        private static string? FindSingleDataParamRecursive(JsonElement inputsNode)
        {
            foreach (var p in FlattenInputs(inputsNode))
            {
                if (p.TryGetProperty("type", out var t) && t.GetString() == "data" && p.TryGetProperty("name", out var n))
                    return n.GetString();
            }
            return null;
        }

        private static IEnumerable<string> FindDataParamsRecursive(JsonElement inputsNode)
        {
            foreach (var p in FlattenInputs(inputsNode))
            {
                if (p.TryGetProperty("type", out var t) && t.GetString() == "data" && p.TryGetProperty("name", out var n))
                    yield return n.GetString()!;
            }
        }

        private async Task WaitForJobCompletionAsync(string jobId, CancellationToken ct)
        {
            _log.LogInformation("Waiting for Galaxy job {JobId} to complete...", jobId);

            // Allow long-running genome downloads/unzips; 90 minutes helps avoid premature timeouts on slow links.
            var timeout = TimeSpan.FromMinutes(90);
            var startTime = DateTime.UtcNow;
            var pollInterval = TimeSpan.FromSeconds(5);  // Poll every 5 seconds to reduce load
            
            while (DateTime.UtcNow - startTime < timeout)
            {
                try
                {
                    var jobDetails = await _galaxy.GetJobAsync(jobId, ct);
                    
                    if (jobDetails.TryGetProperty("state", out var stateProperty))
                    {
                        var state = stateProperty.GetString();
                        _log.LogInformation("Job {JobId} state: {State}", jobId, state);
                        
                        if (state == "ok")
                        {
                            _log.LogInformation("Job {JobId} completed successfully", jobId);
                            return;
                        }
                        else if (state == "error" || state == "deleted" || state == "failed")
                        {
                            var errorMsg = "Job failed with state: " + state;
                            if (jobDetails.TryGetProperty("stderr", out var stderr))
                            {
                                errorMsg += "\nStderr: " + stderr.GetString();
                            }
                            _log.LogError("Job {JobId} failed: {Error}", jobId, errorMsg);
                            throw new InvalidOperationException(errorMsg);
                        }
                        // If state is "new", "queued", "running", etc., continue polling
                    }
                }
                catch (Exception ex) when (ex is not InvalidOperationException)
                {
                    _log.LogWarning(ex, "Error checking job {JobId} status, will retry", jobId);
                }
                
                await Task.Delay(pollInterval, ct);
            }
            
            throw new TimeoutException($"Job {jobId} did not complete within {timeout.TotalMinutes} minutes");
        }

        private async Task EnsureProteinAlignmentsForGroupsAsync(
            string historyId,
            IReadOnlyList<OrthologousGroup> groups,
            List<GenomeInfo> genomes,
            string alignment,
            CancellationToken ct)
        {
            if (groups is null || groups.Count == 0) return;
            if (genomes is null || genomes.Count == 0)
            {
                _log.LogWarning("No genomes supplied; skipping pre-alignment for orthogroups.");
                return;
            }

            foreach (var group in groups)
            {
                try
                {
                    var proteinDatasetIds = new List<string>();
                    foreach (var (species, genesCsv) in group.SpeciesGenes)
                    {
                        var genome = genomes.FirstOrDefault(g =>
                            string.Equals(g.Species, species, StringComparison.OrdinalIgnoreCase));
                        if (genome is null || string.IsNullOrWhiteSpace(genome.ProteinDatasetId))
                            continue;

                        var geneIds = genesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var geneId in geneIds)
                        {
                            var proteinId = await ExtractProteinSequenceAsync(historyId, genome.ProteinDatasetId, geneId, genome.Species, ct);
                            proteinDatasetIds.Add(proteinId);
                        }
                    }

                    if (proteinDatasetIds.Count < 3)
                    {
                        _log.LogWarning("Skipping pre-alignment for orthogroup {GroupId}: only {Count} protein sequences found", group.GroupId, proteinDatasetIds.Count);
                        continue;
                    }

                    // Merge to multifasta then align
                    var mergedId = await MergeFastaAsync(historyId, proteinDatasetIds, ct);
                    var alignedId = await AlignAsync(historyId, mergedId, alignment, ct);
                    var targetName = $"aligned_proteins_{SanitizeName(group.GroupId)}.fasta";
                    await _galaxy.RenameHistoryContentAsync(historyId, alignedId, targetName, ct);
                    _log.LogInformation("Created aligned protein multifasta for {GroupId}: {Dataset}", group.GroupId, alignedId);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to pre-align proteins for orthogroup {GroupId}", group.GroupId);
                }
            }
        }

        private IReadOnlyList<OrthologousGroup> ParseHierarchicalSingleCopyOrthogroups(string content, IReadOnlyList<GenomeInfo>? genomes = null)
        {
            var groups = new List<OrthologousGroup>();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return groups;

            // OrthoFinder N0 header: HOG, OG, Gene Tree Parent Clade, Species1...
            var header = lines[0].Trim().Split('\t');
            var speciesStart = FindN0SpeciesStartColumn(header);
            var headerSpeciesNames = header.Skip(speciesStart).ToArray();
            var speciesNames = headerSpeciesNames;
            string[]? metaSpeciesByIndex = (genomes is not null && genomes.Count == headerSpeciesNames.Length)
                ? genomes.Select(g => g.Species).ToArray()
                : null;

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Trim().Split('\t');
                if (parts.Length < header.Length) continue;

                var groupId = parts[0].Trim();
                var speciesToGene = new Dictionary<string, string>();
                var allGeneIds = new List<string>();

                bool isSingleCopy = true;

                for (int i = speciesStart; i < header.Length; i++)
                {
                    var speciesIndex = i - speciesStart;
                    var species = speciesNames[speciesIndex];
                    var cell = i < parts.Length ? parts[i].Trim() : "";

                    // must have exactly one gene for every species
                    if (string.IsNullOrEmpty(cell) || cell == "-" || cell == "0")
                    {
                        isSingleCopy = false;
                        break;
                    }

                    var genes = cell
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(g => g.Trim())
                        .Where(g => g.Length > 0)
                        .ToList();

                    if (genes.Count != 1)
                    {
                        isSingleCopy = false;
                        break;
                    }

                    var geneId = genes[0];

                    // Map using header species name
                    speciesToGene[species] = geneId;
                    // Also map using metadata species name in the same column (if different)
                    if (metaSpeciesByIndex is not null && metaSpeciesByIndex.Length > speciesIndex)
                    {
                        var metaSpecies = metaSpeciesByIndex[speciesIndex];
                        if (!string.Equals(metaSpecies, species, StringComparison.OrdinalIgnoreCase))
                            speciesToGene[metaSpecies] = geneId;
                    }

                    allGeneIds.Add(geneId);
                }

                // Keep only groups with one gene in every species
                if (isSingleCopy && allGeneIds.Count == speciesNames.Length)
                {
                    groups.Add(new OrthologousGroup(
                        groupId,
                        $"Orthogroup {groupId}",
                        allGeneIds,
                        speciesToGene.ToDictionary(kv => kv.Key, kv => kv.Value)
                    ));
                }
            }

            return groups;
        }

        public async Task<string> InvokeGenomeAnalysisWorkflowAsync(
            string historyId,
            string cdsCollectionId,
            string proteomeCollectionId,
            string orthogroupCollectionId,
            string newickId,
            List<GenomeInfo> genomes,
            string? speciesMappingId,
            CancellationToken ct)
        {
            using var apiScope = UseApiKeyForHistory(historyId);

            var workflowId = "a61f032e7ea0cc1b";

            if (string.IsNullOrWhiteSpace(proteomeCollectionId) ||
                string.IsNullOrWhiteSpace(cdsCollectionId) ||
                string.IsNullOrWhiteSpace(orthogroupCollectionId))
            {
                throw new InvalidOperationException("Missing required inputs for Final workflow: proteome/cds/orthogroup collection ids are required.");
            }

            var mappingId = speciesMappingId;
            if (string.IsNullOrWhiteSpace(mappingId))
            {
                mappingId = await EnsureSpeciesMappingAsync(historyId, genomes, ct);
            }
            if (string.IsNullOrWhiteSpace(mappingId))
                throw new InvalidOperationException("Species mapping dataset not found or could not be created.");
            
            // Inputs based on prompt description:
            // "a collection with all of the cds genomes, a collection with all of the proteome fastas, the species mapping tabular file, and a collection with all of the orthogroup tabular files."
            // We use the labels that match the workflow inputs.
            var inputs = new Dictionary<string, object>
            {
                // Proteomes
                ["proteome_fastas"] = new { src = "hdca", id = proteomeCollectionId },
                ["proteome_collection"] = new { src = "hdca", id = proteomeCollectionId },
                ["0"] = new { src = "hdca", id = proteomeCollectionId },
                ["6310435"] = new { src = "hdca", id = proteomeCollectionId },

                // Orthogroup tabulars
                ["orthogroup_multifastas"] = new { src = "hdca", id = orthogroupCollectionId },
                ["orthogroup_tabulars"] = new { src = "hdca", id = orthogroupCollectionId },
                ["1"] = new { src = "hdca", id = orthogroupCollectionId },

                // CDS
                ["cds_genomes"] = new { src = "hdca", id = cdsCollectionId },
                ["2"] = new { src = "hdca", id = cdsCollectionId },
                ["6310436"] = new { src = "hdca", id = cdsCollectionId },

                // Newick tree
                ["newick_tree"] = new { src = "hda", id = newickId },
                ["4"] = new { src = "hda", id = newickId }
            };

            // Species mapping (required by workflow)
            inputs["species_mapping.tabular"] = new { src = "hda", id = mappingId };
            inputs["species_mapping"] = new { src = "hda", id = mappingId };
            inputs["3"] = new { src = "hda", id = mappingId };

            _log.LogInformation("Invoking genome analysis workflow {WorkflowId} for history {HistoryId}", workflowId, historyId);
            
            try 
            {
                var (invocationId, _) = await _galaxy.InvokeWorkflowAsync(workflowId, historyId, inputs, ct);
                _log.LogInformation("Workflow invoked successfully: {InvocationId}", invocationId);
                return invocationId;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to invoke workflow {WorkflowId}", workflowId);
                throw;
            }
        }

        private int CountSpeciesInCollection(JsonElement collectionElement)
        {
            // This is a heuristic to guess how many species are in a collection
            // based on the number of elements it contains.
            if (collectionElement.TryGetProperty("element_count", out var countProp))
            {
                return countProp.GetInt32();
            }
            
            // If element_count is not available, we might need to fetch details, 
            // but for now we'll return 0 to be safe.
            return 0;
        }
    }

    public sealed class GenomeLookupException : Exception
    {
        public IReadOnlyList<GenomeLookupFailure> Failures { get; }

        public GenomeLookupException(IReadOnlyList<GenomeLookupFailure> failures)
            : base(BuildMessage(failures))
        {
            Failures = failures;
        }

        private static string BuildMessage(IReadOnlyList<GenomeLookupFailure> failures)
        {
            if (failures is null || failures.Count == 0)
                return "Genome lookup failed.";

            if (failures.Count == 1)
            {
                var failure = failures[0];
                return $"No genome found for species {failure.Species}: {failure.Reason}";
            }

            var speciesList = string.Join(", ", failures.Select(f => f.Species));
            return $"Genomes were not found for {failures.Count} species: {speciesList}";
        }
    }

    

    public sealed record GenomeLookupFailure(string Species, string Reason, string? Suggestion);
}
