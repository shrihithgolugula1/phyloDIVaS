using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text;
using System.Linq;
using CapstoneServices.Galaxy;
using CapstoneServices.NCBI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AnalysisAPI.Services;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;

namespace AnalysisAPI.Controllers
{
    [ApiController]
    [Route("api/dnds")]
    public sealed class DnDsController : ControllerBase
    {
        private readonly GalaxyClient _galaxy;
        private readonly NcbiClient _ncbi;
        private readonly GalaxyBustedPipeline _pipeline;
        private readonly GalaxyOrthoFinderPipeline _orthoPipeline;
        private readonly ILogger<DnDsController> _log;
        private readonly GalaxyOptions _gopts;
        private readonly ProgressSessionManager _progressSessionManager;
        private static readonly ConcurrentDictionary<string, byte> ActiveSelectionSubmissions = new(StringComparer.OrdinalIgnoreCase);

        public DnDsController(GalaxyClient galaxy, NcbiClient ncbi, GalaxyBustedPipeline pipeline, GalaxyOrthoFinderPipeline orthoPipeline, ILogger<DnDsController> log, Microsoft.Extensions.Options.IOptions<GalaxyOptions> gopts, ProgressSessionManager progressSessionManager)
        { _galaxy = galaxy; _ncbi = ncbi; _pipeline = pipeline; _orthoPipeline = orthoPipeline; _log = log; _gopts = gopts.Value; _progressSessionManager = progressSessionManager; }

        public sealed class HyphyTableResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; } = true;
            [JsonPropertyName("data")]
            public object? Data { get; set; }
        }

        [HttpPost("start")] // POST api/dnds/start
        public async Task<IActionResult> Start([FromBody] StartRequest req, CancellationToken ct)
        {
            // CDS candidate-based workflow removed
            return StatusCode(410, new { error = "The gene-based CDS candidate workflow has been removed." });
        }

        // Removed: candidates endpoint (CDS candidate listing)

        [HttpGet("species/suggest")] // GET api/dnds/species/suggest
        public async Task<IActionResult> SuggestSpecies([FromQuery] string query, [FromQuery] string? apiKey, [FromQuery] int limit = 25, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
                return Ok(new { success = true, suggestions = Array.Empty<object>() });

            var suggestions = await _ncbi.SuggestSpeciesAsync(query, apiKey, Math.Clamp(limit, 1, 250), ct);
            return Ok(new { success = true, suggestions });
        }

        private sealed record GalaxyHistoryStatusItem(string? Id, string Name, string State, int Hid, string ContentType);

        private static string? FindFailedDatasetId(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray())
                {
                    var found = FindFailedDatasetId(child);
                    if (!string.IsNullOrWhiteSpace(found)) return found;
                }
                return null;
            }

            if (node.ValueKind != JsonValueKind.Object) return null;

            var state = node.TryGetProperty("state", out var stateNode)
                ? stateNode.GetString()
                : null;
            var id = node.TryGetProperty("id", out var idNode) && idNode.ValueKind == JsonValueKind.String
                ? idNode.GetString()
                : null;
            var contentType = node.TryGetProperty("history_content_type", out var typeNode)
                ? typeNode.GetString()
                : null;
            var src = node.TryGetProperty("src", out var srcNode) ? srcNode.GetString() : null;
            var isFailed = state is "error" or "failed" or "discarded";
            var isDataset = string.Equals(contentType, "dataset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(src, "hda", StringComparison.OrdinalIgnoreCase)
                || node.TryGetProperty("creating_job", out _);
            if (isFailed && isDataset && !string.IsNullOrWhiteSpace(id)) return id;

            // Galaxy collection elements commonly wrap the HDA in an `object` field.
            if (node.TryGetProperty("object", out var objectNode))
            {
                var found = FindFailedDatasetId(objectNode);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
            if (node.TryGetProperty("elements", out var elementsNode))
            {
                var found = FindFailedDatasetId(elementsNode);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }

            return null;
        }

        private async Task<string?> GetFailedDatasetDiagnosticAsync(
            string datasetId,
            string itemName,
            CancellationToken ct)
        {
            try
            {
                var dataset = await _galaxy.GetDatasetDetailsAsync(datasetId, ct);
                var jobId = dataset.TryGetProperty("creating_job", out var creatingJob)
                    ? creatingJob.GetString()
                    : dataset.TryGetProperty("job_id", out var jobNode) ? jobNode.GetString() : null;
                if (string.IsNullOrWhiteSpace(jobId)) return null;

                var job = await _galaxy.GetFullJobAsync(jobId, ct);
                var toolId = job.TryGetProperty("tool_id", out var toolNode)
                    ? toolNode.GetString()
                    : null;
                var diagnosticParts = new List<string>();
                foreach (var propertyName in new[]
                {
                    "tool_stderr", "job_stderr", "stderr", "tool_stdout", "job_stdout", "stdout", "info"
                })
                {
                    if (job.TryGetProperty(propertyName, out var outputNode) && outputNode.ValueKind == JsonValueKind.String)
                        AddDiagnosticText(diagnosticParts, outputNode.GetString());
                }

                if (job.TryGetProperty("job_messages", out var messagesNode))
                    AddDiagnosticText(diagnosticParts, messagesNode.ToString());
                if (dataset.TryGetProperty("misc_info", out var miscInfoNode))
                    AddDiagnosticText(diagnosticParts, miscInfoNode.GetString());

                try
                {
                    var inputs = await _galaxy.GetJobInputsAsync(jobId, ct);
                    var parameters = await _galaxy.GetJobParametersDisplayAsync(jobId, ct);
                    _log.LogInformation(
                        "Failed Galaxy job {JobId} input associations: {Inputs}; parameters: {Parameters}",
                        jobId,
                        TrimDiagnosticJson(inputs),
                        TrimDiagnosticJson(parameters));

                    var alignmentId = FindJobInputDatasetId(inputs, "input_file");
                    var treeId = FindJobInputDatasetId(inputs, "input_nhx");
                    if (!string.IsNullOrWhiteSpace(alignmentId) && !string.IsNullOrWhiteSpace(treeId))
                    {
                        var alignment = await _galaxy.DownloadDatasetAsync(string.Empty, alignmentId, ct);
                        var tree = await _galaxy.DownloadDatasetAsync(string.Empty, treeId, ct);
                        try
                        {
                            var normalized = HyphyInputNormalizer.NormalizeAndValidate(alignment, tree);
                            var sequenceLength = normalized.Fasta
                                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                .FirstOrDefault(line => !line.StartsWith('>'))?.Length ?? 0;
                            _log.LogInformation(
                                "Validated failed HyPhy job {JobId} inputs in memory: {TaxonCount} matching taxa, {SequenceLength} aligned nucleotides per taxon.",
                                jobId,
                                normalized.Identifiers.Count,
                                sequenceLength);
                        }
                        catch (Exception validationException)
                        {
                            AddDiagnosticText(diagnosticParts, $"HyPhy input validation: {validationException.Message}");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogDebug(ex, "Could not retrieve Galaxy inputs/parameters for job {JobId}", jobId);
                }

                try
                {
                    var console = await _galaxy.GetJobConsoleOutputAsync(jobId, ct);
                    foreach (var propertyName in new[] { "stderr", "stdout" })
                    {
                        if (console.TryGetProperty(propertyName, out var outputNode) && outputNode.ValueKind == JsonValueKind.String)
                            AddDiagnosticText(diagnosticParts, outputNode.GetString());
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogDebug(ex, "Could not retrieve Galaxy console output for job {JobId}", jobId);
                }

                try
                {
                    var outputs = await _galaxy.GetJobOutputsAsync(jobId, ct);
                    foreach (var candidateId in FindDiagnosticOutputDatasetIds(outputs).Take(4))
                    {
                        var content = await _galaxy.DownloadDatasetAsync(string.Empty, candidateId, ct);
                        AddDiagnosticText(diagnosticParts, content);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogDebug(ex, "Could not retrieve diagnostic outputs for Galaxy job {JobId}", jobId);
                }

                var diagnostic = string.Join(" | ", diagnosticParts.Distinct(StringComparer.Ordinal));
                if (string.IsNullOrWhiteSpace(diagnostic)) return null;

                diagnostic = Regex.Replace(diagnostic, @"\s+", " ").Trim();
                if (diagnostic.Length > 1200)
                    diagnostic = diagnostic[^1200..];
                _log.LogWarning(
                    "Galaxy item {ItemName} failed in tool {ToolId}, job {JobId}: {Diagnostic}",
                    itemName,
                    toolId ?? "unknown",
                    jobId,
                    diagnostic);
                return diagnostic;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Could not retrieve Galaxy failure details for dataset {DatasetId}", datasetId);
                return null;
            }
        }

        private static void AddDiagnosticText(ICollection<string> diagnostics, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var normalized = Regex.Replace(value, @"\s+", " ").Trim();
            if (!diagnostics.Contains(normalized)) diagnostics.Add(normalized);
        }

        private static string TrimDiagnosticJson(JsonElement value)
        {
            var json = value.GetRawText();
            return json.Length <= 4000 ? json : json[..4000] + "...";
        }

        private static string? FindJobInputDatasetId(JsonElement inputs, string inputName)
        {
            if (inputs.ValueKind != JsonValueKind.Array) return null;
            foreach (var association in inputs.EnumerateArray())
            {
                if (association.ValueKind != JsonValueKind.Object ||
                    !association.TryGetProperty("name", out var nameNode) ||
                    !string.Equals(nameNode.GetString(), inputName, StringComparison.Ordinal) ||
                    !association.TryGetProperty("dataset", out var datasetNode) ||
                    datasetNode.ValueKind != JsonValueKind.Object ||
                    !datasetNode.TryGetProperty("id", out var idNode))
                    continue;
                return idNode.GetString();
            }
            return null;
        }

        private static IEnumerable<string> FindDiagnosticOutputDatasetIds(JsonElement root)
        {
            var queue = new Queue<(JsonElement Node, string Context)>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            queue.Enqueue((root, string.Empty));

            var inspected = 0;
            while (queue.Count > 0 && inspected++ < 512)
            {
                var (node, context) = queue.Dequeue();
                if (node.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in node.EnumerateArray()) queue.Enqueue((child, context));
                    continue;
                }
                if (node.ValueKind != JsonValueKind.Object) continue;

                var descriptor = context;
                foreach (var propertyName in new[] { "name", "label", "output_name" })
                {
                    if (node.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                        descriptor += " " + property.GetString();
                }

                if (Regex.IsMatch(descriptor, "error|stderr|log", RegexOptions.IgnoreCase)
                    && node.TryGetProperty("id", out var idNode)
                    && idNode.ValueKind == JsonValueKind.String)
                {
                    var id = idNode.GetString();
                    if (!string.IsNullOrWhiteSpace(id) && ids.Add(id)) yield return id;
                }

                foreach (var property in node.EnumerateObject())
                    queue.Enqueue((property.Value, $"{context} {property.Name}"));
            }
        }

        private async Task<string?> GetFailedGalaxyItemDiagnosticAsync(
            string historyId,
            GalaxyHistoryStatusItem item,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(item.Id)) return null;

            if (string.Equals(item.ContentType, "dataset", StringComparison.OrdinalIgnoreCase))
                return await GetFailedDatasetDiagnosticAsync(item.Id, item.Name, ct);

            if (!string.Equals(item.ContentType, "dataset_collection", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                var collection = await _galaxy.GetCollectionDetailsAsync(historyId, item.Id, ct);
                var failedDatasetId = FindFailedDatasetId(collection);
                return string.IsNullOrWhiteSpace(failedDatasetId)
                    ? null
                    : await GetFailedDatasetDiagnosticAsync(failedDatasetId, item.Name, ct);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Could not inspect failed Galaxy collection {CollectionId}", item.Id);
                return null;
            }
        }

        [HttpGet("species/details")]
        public async Task<IActionResult> GetSpeciesDetails([FromQuery] string accession, [FromQuery] string? apiKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(accession)) return BadRequest(new { error = "accession is required" });
            var details = await _ncbi.GetAssemblyPublicationDetailsAsync(accession, apiKey, ct);
            return details is null ? NotFound(new { error = "NCBI assembly metadata was not found." }) : Ok(new { success = true, details });
        }

        [HttpPost("species/tree")]
        public async Task<IActionResult> GetSpeciesTree([FromBody] SpeciesTreeRequest req, CancellationToken ct = default)
        {
            if (req.Species is null || req.Species.Count < 2)
                return BadRequest(new { error = "At least two species are required." });
            var result = await _ncbi.GetReferenceSpeciesTreeAsync(req.Species, req.NcbiApiKey, ct);
            return string.IsNullOrWhiteSpace(result.Newick)
                ? UnprocessableEntity(new { error = "NCBI Taxonomy could not resolve at least two selected species.", result.MissingSpecies })
                : Ok(new { success = true, tree = result });
        }

        [HttpPost("species/tree/times")]
        public async Task<IActionResult> GetSpeciesTreeTimes([FromBody] SpeciesTreeTimesRequest req, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(req.Newick) || req.LeafTaxIds.Count < 2)
                return BadRequest(new { error = "A Newick tree and at least two leaf TaxIDs are required." });
            var estimates = await _ncbi.GetTimeTreeEstimatesAsync(req.Newick, req.LeafTaxIds, ct);
            return Ok(new { success = true, divergenceTimes = estimates });
        }

        [HttpPost("galaxy/validate-key")]
        public async Task<IActionResult> ValidateGalaxyApiKey([FromBody] GalaxyApiKeyRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.GalaxyApiKey))
                return BadRequest(new { valid = false, error = "Enter a Galaxy API key." });
            try
            {
                await _galaxy.ValidateApiKeyAsync(req.GalaxyApiKey, ct);
                return Ok(new { valid = true });
            }
            catch (GalaxyHttpException ex) when (ex.StatusCode is 401 or 403)
            {
                return Unauthorized(new { valid = false, error = "Galaxy rejected this API key." });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Galaxy API key validation failed");
                return StatusCode(502, new { valid = false, error = "Galaxy could not validate the API key right now." });
            }
        }

        [HttpPost("genome-analysis/start")] // POST api/dnds/genome-analysis/start
        public async Task<IActionResult> StartGenomeAnalysis([FromBody] GenomeAnalysisRequest req, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.GalaxyApiKey))
                    return BadRequest(new { error = "A valid Galaxy API key is required to start an analysis." });
                await _galaxy.ValidateApiKeyAsync(req.GalaxyApiKey, ct);

                var species = (req.Species ?? new List<string>())
                    .Select(value => value?.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (species.Count < GalaxyOrthoFinderPipeline.MinSpeciesCount ||
                    species.Count > GalaxyOrthoFinderPipeline.MaxSpeciesCount)
                {
                    return BadRequest(new
                    {
                        error = $"Genome analysis requires between {GalaxyOrthoFinderPipeline.MinSpeciesCount} and {GalaxyOrthoFinderPipeline.MaxSpeciesCount} distinct species"
                    });
                }

                // Create a progress session for streaming updates
                var sessionId = Guid.NewGuid().ToString();
                var progressReporter = _progressSessionManager.CreateSession(sessionId);

                // Start the analysis in the background. Use a long-lived token so work continues even if the HTTP request is canceled.
                var backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(new CancellationTokenSource(TimeSpan.FromHours(12)).Token);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _orthoPipeline.StartGenomeAnalysisAsync(species, req.NcbiApiKey, req.GalaxyApiKey, backgroundCts.Token, progressReporter);
                        progressReporter.Complete();
                    }
                    catch (OperationCanceledException oce)
                    {
                        _log.LogWarning(oce, "Genome analysis canceled for session {SessionId}", sessionId);
                        progressReporter.ReportProgress("Canceled", "Analysis was canceled by the server.", null, null, new { error = oce.Message });
                        progressReporter.Complete();
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Error during genome analysis for session {SessionId}", sessionId);
                        progressReporter.ReportProgress("Error", $"Analysis failed: {ex.Message}", null, null, new { error = ex.Message });
                        progressReporter.Complete();
                    }
                    finally
                    {
                        // Clean up session after 5 minutes
                        await Task.Delay(TimeSpan.FromMinutes(5));
                        _progressSessionManager.RemoveSession(sessionId);
                    }
                }, CancellationToken.None);

                // Return the session ID immediately for the client to connect to SSE
                return Ok(new
                {
                    success = true,
                    sessionId = sessionId
                });
            }
            catch (GalaxyHttpException ex) when (ex.StatusCode is 401 or 403)
            {
                return Unauthorized(new { error = "Galaxy rejected this API key." });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error starting genome analysis");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("genome-analysis/continue-preparation")]
        public async Task<IActionResult> ContinueGenomePreparation(
            [FromBody] AttachGenomeAnalysisRequest req,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.HistoryId))
                return BadRequest(new { error = "historyId is required" });
            if (string.IsNullOrWhiteSpace(req.GalaxyApiKey))
                return BadRequest(new { error = "A valid Galaxy API key is required." });

            try
            {
                await _galaxy.ValidateApiKeyAsync(req.GalaxyApiKey, ct);
                using (var apiScope = _galaxy.UseApiKey(req.GalaxyApiKey))
                    await _galaxy.GetHistoryDetailsAsync(req.HistoryId, ct);

                var sessionId = Guid.NewGuid().ToString();
                var progressReporter = _progressSessionManager.CreateSession(sessionId);
                var backgroundCts = new CancellationTokenSource(TimeSpan.FromHours(12));
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _orthoPipeline.ContinuePreparedGenomeAnalysisAsync(
                            req.HistoryId, req.GalaxyApiKey, backgroundCts.Token, progressReporter);
                        progressReporter.Complete();
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Unable to continue prepared genome analysis in history {HistoryId}", req.HistoryId);
                        progressReporter.ReportProgress(
                            "Error", $"Unable to continue to OrthoFinder: {ex.Message}", null, null,
                            new { historyId = req.HistoryId, error = ex.Message });
                        progressReporter.Complete();
                    }
                    finally
                    {
                        backgroundCts.Dispose();
                        await Task.Delay(TimeSpan.FromMinutes(5));
                        _progressSessionManager.RemoveSession(sessionId);
                    }
                }, CancellationToken.None);

                return Ok(new { success = true, sessionId, historyId = req.HistoryId });
            }
            catch (GalaxyHttpException ex) when (ex.StatusCode is 401 or 403)
            {
                return Unauthorized(new { error = "Galaxy rejected this API key or it cannot access this history." });
            }
        }

        [HttpGet("hyphy/markdown/{historyId}/{datasetId}")]
        public async Task<IActionResult> GetHyphyMarkdownTable(
            string historyId,
            string datasetId,
            [FromHeader(Name = "X-Galaxy-Api-Key")] string? galaxyApiKey,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(historyId) || string.IsNullOrWhiteSpace(datasetId))
                return BadRequest(new { error = "historyId and datasetId are required" });

            try
            {
                using var scope = _galaxy.UseApiKey(galaxyApiKey);
                var markdown = await _galaxy.DownloadDatasetAsync(historyId, datasetId, ct);
                var jsonText = MarkdigMarkdownTableParser.MarkdownTableToJson(markdown ?? string.Empty);
                var data = JsonSerializer.Deserialize<object>(jsonText);
                return Ok(new HyphyTableResponse { Success = true, Data = data });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to parse HyPhy markdown for history {HistoryId}, dataset {DatasetId}", historyId, datasetId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("genome-analysis/{historyId}/result-artifacts")]
        public async Task<IActionResult> GetGenomeAnalysisResultArtifacts(
            string historyId,
            [FromHeader(Name = "X-Galaxy-Api-Key")] string? galaxyApiKey,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(historyId))
                return BadRequest(new { error = "historyId is required" });
            if (string.IsNullOrWhiteSpace(galaxyApiKey))
                return Unauthorized(new { error = "A Galaxy API key is required to list result artifacts." });

            try
            {
                var artifacts = await _orthoPipeline.GetResultArtifactsAsync(historyId, galaxyApiKey, ct);
                return Ok(new { success = true, artifacts });
            }
            catch (GalaxyHttpException ghe)
            {
                var statusCode = ghe.StatusCode is 403 or 404 ? ghe.StatusCode : 502;
                return StatusCode(statusCode, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unable to list result artifacts for Galaxy history {HistoryId}", historyId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("genome-analysis/{historyId}/hyphy-outputs")]
        public async Task<IActionResult> GetGenomeAnalysisHyphyOutputs(
            string historyId,
            [FromHeader(Name = "X-Galaxy-Api-Key")] string? galaxyApiKey,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(historyId))
                return BadRequest(new { error = "historyId is required" });
            if (string.IsNullOrWhiteSpace(galaxyApiKey))
                return Unauthorized(new { error = "A Galaxy API key is required to load completed results." });

            try
            {
                var outputs = await _orthoPipeline.GetHyphyOutputsAsync(historyId, galaxyApiKey, ct);
                var methods = new[] { "BUSTED", "aBSREL", "MEME" };
                var methodGeneIds = methods.ToDictionary(method => method, method => outputs.Json
                    .Where(output => string.Equals(output.Method, method, StringComparison.OrdinalIgnoreCase))
                    .Select(output => output.ElementIdentifier ?? string.Empty)
                    .Where(id => Regex.IsMatch(id, @"^N\d+\.HOG\d+$", RegexOptions.IgnoreCase))
                    .Select(id => id.ToUpperInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));
                var methodCounts = methodGeneIds.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
                var collectionGeneIds = methodGeneIds.Values.SelectMany(ids => ids).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return Ok(new
                {
                    success = true,
                    hyphyMarkdownOutputs = outputs.Markdown,
                    hyphyJsonOutputs = outputs.Json,
                    methodCounts,
                    geneCount = collectionGeneIds.Count,
                    collectionGeneIds,
                    methodGeneIds,
                    collectionCountsMatch = collectionGeneIds.Count > 0 && methodGeneIds.Values.All(ids => ids.SetEquals(collectionGeneIds))
                });
            }
            catch (GalaxyHttpException ghe)
            {
                var statusCode = ghe.StatusCode is 403 or 404 ? ghe.StatusCode : 502;
                return StatusCode(statusCode, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unable to load HyPhy outputs for Galaxy history {HistoryId}", historyId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("genome-analysis/attach")] // POST api/dnds/genome-analysis/attach
        public async Task<IActionResult> AttachGenomeAnalysis([FromBody] AttachGenomeAnalysisRequest req, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.HistoryId))
                    return BadRequest(new { error = "historyId is required to resume an analysis" });

                var result = await _orthoPipeline.ResumeGenomeAnalysisAsync(req.HistoryId, req.OrthoFinderJobId, req.GalaxyApiKey, ct);

                var genomes = (result.Genomes ?? new List<GalaxyOrthoFinderPipeline.GenomeInfo>()).ToList();
                var hasJob = !string.IsNullOrWhiteSpace(result.OrthoFinderJobId);

                var stage = (result.Status ?? "initializing").ToLowerInvariant();
                var exposeHyphyResults = stage == "workflow_complete";
                var (statusMessage, statusProgress) = stage switch
                {
                    "workflow_complete" => (result.StatusDetail ?? "Galaxy workflow completed.", 100),
                    "workflow_failed" => (result.StatusDetail ?? "Galaxy workflow failed. Review the failed jobs in Galaxy.", 100),
                    "workflow_running" => (result.StatusDetail ?? "Galaxy workflow is running for orthogroup analysis...", 90),
                    "orthogroups_ready" => (result.StatusDetail ?? "Filtered orthogroup tables available.", 80),
                    "orthofinder_complete" => (result.StatusDetail ?? "OrthoFinder completed. Select orthogroups to analyze.", 70),
                    "orthofinder_running" => (result.StatusDetail ?? "OrthoFinder is running in Galaxy...", 60),
                    "unzipped" => (result.StatusDetail ?? "Genomes uploaded and extracted.", 50),
                    "uploading" => (result.StatusDetail ?? $"Galaxy has downloaded {genomes.Count} genome(s) and is preparing FASTA files...", 30),
                    "initializing" => (result.StatusDetail ?? "Galaxy is creating the history and downloading genomes...", 10),
                    _ => (result.StatusDetail ?? "Galaxy history detected. Monitoring jobs...", 40)
                };

                return Ok(new
                {
                    success = true,
                    historyId = result.HistoryId,
                    orthoFinderJobId = hasJob ? result.OrthoFinderJobId : null,
                    genomes = genomes.Select(g => new
                    {
                        species = g.Species,
                        proteinUrl = g.ProteinUrl,
                        proteinDatasetId = g.ProteinDatasetId,
                        cdsUrl = g.CdsUrl,
                        cdsDatasetId = g.CdsDatasetId
                    }).ToList(),
                    status = stage,
                    statusMessage,
                    statusProgress,
                    workflowInvocationId = result.WorkflowInvocationId,
                    workflowStatus = result.Status,
                    workflowStatusDetail = result.StatusDetail,
                    hyphyMarkdownOutputs = exposeHyphyResults
                        ? result.HyphyMarkdownOutputs ?? new List<GalaxyOrthoFinderPipeline.HyphyOutput>()
                        : new List<GalaxyOrthoFinderPipeline.HyphyOutput>(),
                    hyphyJsonOutputs = exposeHyphyResults
                        ? result.HyphyJsonOutputs ?? new List<GalaxyOrthoFinderPipeline.HyphyOutput>()
                        : new List<GalaxyOrthoFinderPipeline.HyphyOutput>()
                });
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(ghe, "Galaxy error attaching to history {HistoryId}", req.HistoryId);
                var statusCode = ghe.StatusCode is 403 or 404 ? ghe.StatusCode : 502;
                var message = ghe.StatusCode switch
                {
                    403 => "The Galaxy API key cannot access this history. Confirm that the key and history are from the same Galaxy server.",
                    404 => "Galaxy could not find this history ID. Make sure you copied the history ID, not a dataset or job ID.",
                    _ => ghe.Message
                };
                return StatusCode(statusCode, new { error = message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error attaching to history {HistoryId}", req.HistoryId);
                return StatusCode(500, new { error = ex.Message, detail = "If this is an older history, rerun once to write metadata or ensure the metadata dataset 'dnds-genome-analysis.json' is visible (unhide) in Galaxy." });
            }
        }

        [HttpGet("genome-analysis/progress/{sessionId}")] // GET api/dnds/genome-analysis/progress/{sessionId}
        public async Task StreamProgress(string sessionId, CancellationToken ct)
        {
            var progressReporter = _progressSessionManager.GetSession(sessionId);
            if (progressReporter is null)
            {
                Response.StatusCode = 404;
                await Response.WriteAsync("Session not found", ct);
                return;
            }

            Response.Headers.Append("Content-Type", "text/event-stream");
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no"); // Disable buffering for nginx/proxies

            try
            {
                // Send initial connection confirmation
                await Response.WriteAsync(": connected\n\n", ct);
                await Response.Body.FlushAsync(ct);

                await foreach (var update in progressReporter.GetUpdatesAsync(ct))
                {
                    var json = JsonSerializer.Serialize(update, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                    });
                    await Response.WriteAsync($"data: {json}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected, this is normal
                _log.LogDebug("SSE client disconnected for session {SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error streaming progress for session {SessionId}", sessionId);
            }
        }

        [HttpGet("orthologous-groups/{historyId}/{orthoFinderJobId}")] // GET api/dnds/orthologous-groups/{historyId}/{jobId}
        public async Task<IActionResult> GetOrthologousGroups(
            string historyId,
            string orthoFinderJobId,
            [FromQuery] List<string>? selectedGroupIds,
            [FromHeader(Name = "X-Galaxy-Api-Key")] string? galaxyApiKey,
            CancellationToken ct)
        {
            try
            {
                using var scope = _galaxy.UseApiKey(galaxyApiKey);
                var normalizedSelection = selectedGroupIds?
                    .SelectMany(val => (val ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var groups = await _orthoPipeline.GetOrthologousGroupsAsync(historyId, orthoFinderJobId, normalizedSelection, ct);
                
                return Ok(new
                {
                    success = true,
                    groups = groups.Select(g => new
                    {
                        groupId = g.GroupId,
                        groupName = g.GroupName,
                        geneIds = g.GeneIds,
                        speciesGenes = g.SpeciesGenes,
                        speciesCount = g.SpeciesGenes.Count
                    })
                });
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(ghe, "Galaxy error getting orthologous groups");
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error getting orthologous groups");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("orthofinder/outputs/{historyId}/{orthoFinderJobId}")]
        public async Task<IActionResult> GetOrthoFinderOutputs(
            string historyId,
            string orthoFinderJobId,
            [FromHeader(Name = "X-Galaxy-Api-Key")] string? galaxyApiKey,
            CancellationToken ct)
        {
            try
            {
                using var scope = _galaxy.UseApiKey(galaxyApiKey);
                var outputs = await _orthoPipeline.GetOrthoFinderOutputsAsync(historyId, orthoFinderJobId, ct);
                return Ok(new
                {
                    success = true,
                    historyId = outputs.HistoryId,
                    orthoFinderJobId = outputs.OrthoFinderJobId,
                    speciesTreeDatasetId = outputs.SpeciesTreeDatasetId,
                    speciesTreeNewick = outputs.SpeciesTreeNewick,
                    orthogroupsDatasetId = outputs.OrthogroupsDatasetId,
                    singleCopyDatasetId = outputs.SingleCopyDatasetId,
                    outputs = outputs.Outputs.Select(o => new
                    {
                        o.Label,
                        o.DatasetId,
                        o.Description,
                        o.SuggestedExtension
                    })
                });
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(ghe, "Galaxy error getting OrthoFinder outputs for history {HistoryId}", historyId);
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error getting OrthoFinder outputs for history {HistoryId}", historyId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("orthofinder/output/{datasetId}/download")]
        public async Task<IActionResult> DownloadOrthoFinderOutput(
            string datasetId,
            [FromQuery] string? filename,
            [FromQuery] string? galaxyApiKey,
            [FromHeader(Name = "X-Galaxy-Api-Key")] string? galaxyApiKeyHeader,
            CancellationToken ct)
        {
            try
            {
                using var scope = _galaxy.UseApiKey(galaxyApiKeyHeader ?? galaxyApiKey);
                var (content, contentType) = await _galaxy.DownloadDatasetBytesAsync(datasetId, ct);
                var safeFileName = string.IsNullOrWhiteSpace(filename) ? $"{datasetId}.txt" : filename;
                return File(content, contentType, safeFileName);
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(ghe, "Galaxy error downloading dataset {DatasetId}", datasetId);
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error downloading dataset {DatasetId}", datasetId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        public sealed class ZipDownloadRequest
        {
            [JsonPropertyName("datasetIds")]
            public List<string> DatasetIds { get; set; } = new();
            [JsonPropertyName("datasets")]
            public List<ZipDatasetRequest> Datasets { get; set; } = new();
            [JsonPropertyName("generatedFiles")]
            public List<ZipGeneratedFileRequest> GeneratedFiles { get; set; } = new();
            [JsonPropertyName("filename")]
            public string? Filename { get; set; }
            [JsonPropertyName("galaxyApiKey")]
            public string? GalaxyApiKey { get; set; }
        }

        public sealed class ZipDatasetRequest
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;
            [JsonPropertyName("filename")]
            public string? Filename { get; set; }
        }

        public sealed class ZipGeneratedFileRequest
        {
            [JsonPropertyName("filename")]
            public string Filename { get; set; } = string.Empty;
            [JsonPropertyName("content")]
            public string Content { get; set; } = string.Empty;
        }

        [HttpPost("orthofinder/output/zip")]
        public async Task<IActionResult> DownloadOrthoFinderOutputsZip([FromBody] ZipDownloadRequest req, CancellationToken ct)
        {
            if (req is null ||
                ((req.DatasetIds?.Count ?? 0) == 0 &&
                 (req.Datasets?.Count ?? 0) == 0 &&
                 (req.GeneratedFiles?.Count ?? 0) == 0))
                return BadRequest(new { error = "At least one Galaxy dataset or generated file is required." });

            try
            {
                using var scope = _galaxy.UseApiKey(req.GalaxyApiKey);
                await using var ms = new MemoryStream();
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var requestedDatasets = (req.Datasets ?? new List<ZipDatasetRequest>())
                        .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                        .ToList();
                    foreach (var id in (req.DatasetIds ?? new List<string>()).Where(d => !string.IsNullOrWhiteSpace(d)))
                    {
                        if (!requestedDatasets.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                            requestedDatasets.Add(new ZipDatasetRequest { Id = id });
                    }

                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var dataset in requestedDatasets)
                    {
                        try
                        {
                            var (bytes, contentType) = await _galaxy.DownloadDatasetBytesAsync(dataset.Id, ct);
                            var requestedName = string.IsNullOrWhiteSpace(dataset.Filename)
                                ? $"raw/{dataset.Id}{MimeToExtension(contentType)}"
                                : dataset.Filename!;
                            var entryName = UniqueZipEntryName(SafeZipEntryName(requestedName), usedNames);
                            var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                            await using var entryStream = entry.Open();
                            await entryStream.WriteAsync(bytes, 0, bytes.Length, ct);
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Failed to download dataset {DatasetId} for zip bundle", dataset.Id);
                        }
                    }

                    const int maxGeneratedFileBytes = 25_000_000;
                    const int maxGeneratedBundleBytes = 75_000_000;
                    var generatedSize = 0;
                    foreach (var generated in (req.GeneratedFiles ?? new List<ZipGeneratedFileRequest>())
                        .Where(file => !string.IsNullOrWhiteSpace(file.Filename)))
                    {
                        var bytes = Encoding.UTF8.GetBytes(generated.Content ?? string.Empty);
                        generatedSize += bytes.Length;
                        if (bytes.Length > maxGeneratedFileBytes || generatedSize > maxGeneratedBundleBytes)
                            throw new InvalidOperationException("Generated analysis bundle files exceed the 75 MB safety limit.");
                        var entryName = UniqueZipEntryName(SafeZipEntryName(generated.Filename), usedNames);
                        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                        await using var entryStream = entry.Open();
                        await entryStream.WriteAsync(bytes, 0, bytes.Length, ct);
                    }
                }

                ms.Position = 0;
                var fileName = string.IsNullOrWhiteSpace(req.Filename) ? "hyphy_outputs.zip" : req.Filename;
                return File(ms.ToArray(), "application/zip", fileName);
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(
                    ghe,
                    "Galaxy error bundling datasets {Count}",
                    (req.DatasetIds?.Count ?? 0) + (req.Datasets?.Count ?? 0));
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                _log.LogError(
                    ex,
                    "Unhandled error bundling datasets {Count}",
                    (req.DatasetIds?.Count ?? 0) + (req.Datasets?.Count ?? 0));
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private static string SafeZipEntryName(string value)
        {
            var parts = (value ?? string.Empty)
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => part != "." && part != "..")
                .Select(part => Regex.Replace(part, @"[^A-Za-z0-9._ -]", "_"))
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Take(4)
                .ToList();
            return parts.Count > 0 ? string.Join('/', parts) : "result.txt";
        }

        private static string UniqueZipEntryName(string requested, HashSet<string> used)
        {
            if (used.Add(requested)) return requested;
            var extension = Path.GetExtension(requested);
            var stem = requested[..^extension.Length];
            for (var index = 2; ; index++)
            {
                var candidate = $"{stem}_{index}{extension}";
                if (used.Add(candidate)) return candidate;
            }
        }

        private static string MimeToExtension(string contentType)
        {
            return contentType switch
            {
                "text/markdown" => ".md",
                "application/json" => ".json",
                "text/plain" => ".txt",
                _ => ".dat"
            };
        }

        [HttpGet("orthogroup/{historyId}/{orthogroupId}/genes")]
        public async Task<IActionResult> GetOrthogroupGenes(
            string historyId,
            string orthogroupId,
            [FromHeader(Name = "X-Galaxy-Api-Key")] string? galaxyApiKey,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(historyId) || string.IsNullOrWhiteSpace(orthogroupId))
                return BadRequest(new { error = "historyId and orthogroupId are required" });

            try
            {
                using var scope = _galaxy.UseApiKey(galaxyApiKey);
                var speciesGenes = await _orthoPipeline.GetSingleCopyGeneIdsAsync(historyId, orthogroupId, ct);
                return Ok(new { success = true, orthogroupId, speciesGenes, sequences = speciesGenes });
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(ghe, "Galaxy error fetching orthogroup genes for {OrthogroupId} in history {HistoryId}", orthogroupId, historyId);
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error fetching orthogroup genes for {OrthogroupId} in history {HistoryId}", orthogroupId, historyId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("orthologous-group/analyze")] // POST api/dnds/orthologous-group/analyze
        public async Task<IActionResult> AnalyzeOrthologousGroup([FromBody] OrthoGroupAnalysisRequest req, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.HistoryId) || req.OrthoGroup is null || req.Genomes is null)
                    return BadRequest(new { error = "historyId, orthoGroup, and genomes required" });

                using var apiScope = _galaxy.UseApiKey(req.GalaxyApiKey);
                using var submissionCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                var submissionToken = submissionCts.Token;

                // Convert request data to pipeline types
                var orthoGroup = new GalaxyOrthoFinderPipeline.OrthologousGroup(
                    req.OrthoGroup.GroupId,
                    req.OrthoGroup.GroupName,
                    req.OrthoGroup.GeneIds,
                    req.OrthoGroup.SpeciesGenes
                );

                var genomes = await ResolveGenomesAsync(req.HistoryId, req.Genomes, submissionToken);

                // Run analysis on the single selected group
                var results = await _orthoPipeline.RunSelectionAnalysisAsync(
                    req.HistoryId, 
                    new List<GalaxyOrthoFinderPipeline.OrthologousGroup> { orthoGroup }, 
                    genomes, 
                    req.NcbiApiKey, 
                    req.Alignment ?? "mafft", 
                    submissionToken);

                if (results.Count == 0)
                {
                    return BadRequest(new { error = "Failed to process orthologous group - insufficient sequences or processing error" });
                }

                var result = results[0]; // Single group result

                return Ok(new
                {
                    success = true,
                    historyId = result.HistoryId,
                    codonAlignedDatasetId = result.CodonAlignedDatasetId,
                    jobId = result.BustedJobId,
                    invocation = result.InvocationRaw,
                    stepDatasets = result.StepDatasetIds
                });
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(ghe, "Galaxy error during orthologous group analysis");
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error analyzing orthologous group");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("orthologous-groups/analyze-multiple")] // POST api/dnds/orthologous-groups/analyze-multiple
        public async Task<IActionResult> AnalyzeMultipleOrthologousGroups([FromBody] MultipleOrthoGroupAnalysisRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.HistoryId) || req.OrthoGroups is null || req.Genomes is null)
                return BadRequest(new { error = "historyId, orthoGroups, and genomes required" });

            if (!ActiveSelectionSubmissions.TryAdd(req.HistoryId, 0))
                return Conflict(new { error = "A selection-analysis submission is already being processed for this Galaxy history. Wait for it to finish before submitting again." });

            try
            {
                using var apiScope = _galaxy.UseApiKey(req.GalaxyApiKey);
                using var submissionCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                var submissionToken = submissionCts.Token;

                // Convert request data to pipeline types
                var orthoGroups = req.OrthoGroups.Select(og => new GalaxyOrthoFinderPipeline.OrthologousGroup(
                    og.GroupId, og.GroupName, og.GeneIds, og.SpeciesGenes)).ToList();

                var genomes = await ResolveGenomesAsync(req.HistoryId, req.Genomes, submissionToken);

                // Run analysis on all selected groups
                var results = await _orthoPipeline.RunSelectionAnalysisAsync(
                    req.HistoryId, orthoGroups, genomes, req.NcbiApiKey, req.Alignment ?? "mafft", submissionToken);

                return Ok(new
                {
                    success = true,
                    totalGroups = req.OrthoGroups.Count,
                    successfulAnalyses = results.Count,
                    results = results.Select(r => new
                    {
                        historyId = r.HistoryId,
                        codonAlignedDatasetId = r.CodonAlignedDatasetId,
                        jobId = r.BustedJobId,
                        groupId = r.StepDatasetIds.TryGetValue("group_id", out var gid) ? gid : "unknown",
                        stepDatasets = r.StepDatasetIds
                    })
                });
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(ghe, "Galaxy error during multiple orthologous group analysis");
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error analyzing multiple orthologous groups");
                return StatusCode(500, new { error = ex.Message });
            }
            finally
            {
                ActiveSelectionSubmissions.TryRemove(req.HistoryId, out _);
            }
        }

        [HttpPost("orthologous-groups/annotations")]
        public async Task<IActionResult> GetOrthogroupAnnotations([FromBody] OrthogroupAnnotationRequest req, CancellationToken ct)
        {
            var accessions = (req.Accessions ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(1000)
                .ToList();
            if (accessions.Count == 0) return Ok(new { success = true, annotations = new Dictionary<string, object>() });

            var annotations = await _ncbi.GetProteinAnnotationsAsync(accessions, req.NcbiApiKey, ct);
            return Ok(new
            {
                success = true,
                annotations = annotations.ToDictionary(
                    item => item.Key,
                    item => new
                    {
                        accession = item.Value.Accession,
                        name = item.Value.Name,
                        organism = item.Value.Organism,
                        sourceUrl = item.Value.SourceUrl
                    },
                    StringComparer.OrdinalIgnoreCase)
            });
        }

        [HttpPost("genome-analysis/retry-hyphy")]
        public async Task<IActionResult> RetryHyphyFromCompletedAlignments(
            [FromBody] RetryHyphyRequest req,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.HistoryId))
                return BadRequest(new { error = "historyId is required" });
            if (string.IsNullOrWhiteSpace(req.GalaxyApiKey))
                return BadRequest(new { error = "A Galaxy API key is required and is used only for this request." });
            if (!ActiveSelectionSubmissions.TryAdd(req.HistoryId, 0))
                return Conflict(new { error = "A downstream submission is already being processed for this Galaxy history." });

            try
            {
                using var submissionCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                try
                {
                    var result = await _orthoPipeline.RetryHyphyFromCompletedAlignmentsAsync(
                        req.HistoryId, req.GalaxyApiKey, submissionCts.Token);
                    return Ok(new
                    {
                        success = true,
                        resumedFrom = "completed_codon_alignments",
                        historyId = result.HistoryId,
                        invocationId = result.InvocationId,
                        workflowId = result.WorkflowId,
                        codonAlignmentCollectionId = result.CodonAlignmentCollectionId,
                        geneTreeCollectionId = result.GeneTreeCollectionId
                    });
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("No completed codon-alignment collection", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("No completed IQ-TREE gene-tree collection", StringComparison.OrdinalIgnoreCase))
                {
                    var recovery = await _orthoPipeline.ResumeDownstreamFromCompletedMafftAsync(
                        req.HistoryId, req.GalaxyApiKey, submissionCts.Token);
                    return Ok(new
                    {
                        success = true,
                        resumedFrom = "completed_mafft",
                        historyId = recovery.HistoryId,
                        invocationId = recovery.InvocationId,
                        workflowId = recovery.WorkflowId,
                        mafftCollectionId = recovery.MafftCollectionId,
                        cdsCollectionId = recovery.CdsCollectionId
                    });
                }
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(ghe, "Galaxy error restarting HyPhy for history {HistoryId}", req.HistoryId);
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unable to restart HyPhy for history {HistoryId}", req.HistoryId);
                return StatusCode(500, new { error = ex.Message });
            }
            finally
            {
                ActiveSelectionSubmissions.TryRemove(req.HistoryId, out _);
            }
        }

        [HttpPost("orthologous-groups/analyze-all")] // POST api/dnds/orthologous-groups/analyze-all
        public async Task<IActionResult> AnalyzeAllOrthologousGroups([FromBody] AnalyzeAllGroupsRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.HistoryId) || string.IsNullOrWhiteSpace(req.OrthoFinderJobId) || req.Genomes is null)
                return BadRequest(new { error = "historyId, orthoFinderJobId, and genomes required" });

            if (!ActiveSelectionSubmissions.TryAdd(req.HistoryId, 0))
                return Conflict(new { error = "A selection-analysis submission is already being processed for this Galaxy history. Wait for it to finish before submitting again." });

            try
            {
                using var apiScope = _galaxy.UseApiKey(req.GalaxyApiKey);
                using var submissionCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                var submissionToken = submissionCts.Token;

                var genomes = await ResolveGenomesAsync(req.HistoryId, req.Genomes, submissionToken);

                // Run analysis on ALL orthologous groups
                var results = await _orthoPipeline.RunSelectionAnalysisOnAllGroupsAsync(
                    req.HistoryId, req.OrthoFinderJobId, genomes, req.NcbiApiKey, req.Alignment ?? "mafft", submissionToken);

                return Ok(new
                {
                    success = true,
                    totalAnalyses = results.Count,
                    // Every group belongs to the same Galaxy collection workflow.
                    // Return one descriptor so an all-gene run does not send thousands
                    // of duplicate invocation records back to the browser.
                    results = results.Take(1).Select(r => new
                    {
                        historyId = r.HistoryId,
                        codonAlignedDatasetId = r.CodonAlignedDatasetId,
                        jobId = r.BustedJobId,
                        groupId = r.StepDatasetIds.TryGetValue("group_id", out var gid) ? gid : "unknown",
                        stepDatasets = r.StepDatasetIds
                    })
                });
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(ghe, "Galaxy error during analysis of all orthologous groups");
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error analyzing all orthologous groups");
                return StatusCode(500, new { error = ex.Message });
            }
            finally
            {
                ActiveSelectionSubmissions.TryRemove(req.HistoryId, out _);
            }
        }

        [HttpGet("job/{id}")]
        public async Task<IActionResult> Job(
            string id,
            [FromHeader(Name = "X-Galaxy-Api-Key")] string? galaxyApiKey,
            CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id) || id.Length < 16)
                    return Ok(new { id, state = "ok" });

                using var scope = _galaxy.UseApiKey(galaxyApiKey);
                var job = await _galaxy.GetJobAsync(id, ct);
                return Ok(job);
            }
            catch (GalaxyHttpException ghe)
            {
                _log.LogError(ghe, "Galaxy error getting job {JobId}", id);
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
        }

        // Verify NCBI has at least one genome/assembly for the species
        // GET api/dnds/verify-genome?species=Homo%20sapiens&apiKey=...
        [HttpGet("verify-genome")] 
        public async Task<IActionResult> VerifyGenome([FromQuery] string species, [FromQuery(Name = "apiKey")] string? apiKey, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(species))
                    return BadRequest(new { error = "species is required" });

                var urls = await _ncbi.GetGenomeDownloadUrlAsync(species, apiKey, ct);
                if (urls is not { } genomeUrls || string.IsNullOrWhiteSpace(genomeUrls.ProteinFaaUrl))
                    return Ok(new { found = false, genomeUrl = (string?)null });

                return Ok(new { found = true, genomeUrl = genomeUrls.ProteinFaaUrl });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error verifying genome for species {Species}", species);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Diagnostics: verify Galaxy tool build returns an object with inputs
        [HttpGet("diag/toolbuild")] // GET api/dnds/diag/toolbuild
        public async Task<IActionResult> ToolBuild(CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_gopts.DnDsToolId))
                    return BadRequest(new { error = "DnDsToolId not configured" });
                var build = await _galaxy.GetToolBuildAsync(_gopts.DnDsToolId!, null, ct);

                bool hasInputs = false;
                if (build.ValueKind == JsonValueKind.Object &&
                    build.TryGetProperty("inputs", out var inputs) &&
                    inputs.ValueKind == JsonValueKind.Array)
                {
                    hasInputs = true;
                }
                return Ok(new { toolId = _gopts.DnDsToolId, kind = build.ValueKind.ToString(), hasInputs, build });
            }
            catch (GalaxyHttpException ghe)
            {
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Diagnostics: create/delete a history to test auth and base URL
        [HttpPost("diag/smoke")] // POST api/dnds/diag/smoke
        public async Task<IActionResult> Smoke(CancellationToken ct)
        {
            try
            {
                var id = await _galaxy.CreateHistoryAsync("diag-smoke", ct);
                return Ok(new { historyId = id });
            }
            catch (GalaxyHttpException ghe)
            {
                return StatusCode(502, new { error = ghe.Message, status = ghe.StatusCode, reason = ghe.Reason, body = ghe.Body });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private static GalaxyOrthoFinderPipeline.GenomeInfo GenomeDataToInfo(GenomeData genome)
        {
            if (genome is null) throw new ArgumentNullException(nameof(genome));
            var proteinUrl = string.IsNullOrWhiteSpace(genome.ProteinUrl) ? genome.GenomeUrl : genome.ProteinUrl;
            var proteinDatasetId = string.IsNullOrWhiteSpace(genome.ProteinDatasetId) ? genome.DatasetId : genome.ProteinDatasetId;

            // Allow missing URLs when we have dataset ids (recovered from Galaxy)
            if (string.IsNullOrWhiteSpace(proteinDatasetId))
                throw new InvalidOperationException($"Genome entry for {genome.Species} is missing protein FASTA dataset id.");
            if (string.IsNullOrWhiteSpace(genome.CdsDatasetId))
                throw new InvalidOperationException($"Genome entry for {genome.Species} is missing CDS FASTA dataset id.");

            proteinUrl ??= string.Empty;
            genome.CdsUrl ??= string.Empty;
            return new GalaxyOrthoFinderPipeline.GenomeInfo(
                genome.Species,
                proteinUrl,
                proteinDatasetId,
                genome.CdsUrl,
                genome.CdsDatasetId);
        }

        public sealed class StartRequest
        {
            public string Gene { get; set; } = string.Empty;
            public List<string> Species { get; set; } = new();
            public string? NcbiApiKey { get; set; }
            public string Alignment { get; set; } = "mafft"; // "mafft" or "muscle"
            public Dictionary<string, string>? SelectedIds { get; set; } // species -> chosen nuccore uid
        }

        // Removed: CandidatesRequest (CDS candidate listing)

        public sealed class GenomeAnalysisRequest
        {
            [JsonPropertyName("species")]
            public List<string> Species { get; set; } = new();
            
            [JsonPropertyName("ncbiApiKey")]
            public string? NcbiApiKey { get; set; }

            [JsonPropertyName("galaxyApiKey")]
            public string? GalaxyApiKey { get; set; }
        }

        private static string NormalizeSpecies(string species) =>
            (species ?? string.Empty).Trim().Replace(" ", "_").ToLowerInvariant();

        private async Task<List<GalaxyOrthoFinderPipeline.GenomeInfo>> ResolveGenomesAsync(string historyId, List<GenomeData> genomes, CancellationToken ct)
        {
            genomes ??= new List<GenomeData>();

            var needsRehydrate = genomes.Any(g =>
                string.IsNullOrWhiteSpace(g.ProteinDatasetId) ||
                string.IsNullOrWhiteSpace(g.CdsDatasetId));

            Dictionary<string, GalaxyOrthoFinderPipeline.GenomeInfo>? rehydratedMap = null;
            if (needsRehydrate)
            {
                try
                {
                    var rehydrated = await _orthoPipeline.GetGenomesFromHistoryAsync(historyId, ct);
                    rehydratedMap = rehydrated
                        .GroupBy(g => NormalizeSpecies(g.StandardizedSpecies ?? g.Species))
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to rehydrate genome metadata from history {HistoryId}", historyId);
                }
            }

            foreach (var g in genomes)
            {
                if (rehydratedMap == null)
                    continue;

                var key = NormalizeSpecies(g.Species);
                if (rehydratedMap.TryGetValue(key, out var info))
                {
                    g.ProteinDatasetId ??= info.ProteinDatasetId;
                    g.CdsDatasetId ??= info.CdsDatasetId;
                    g.ProteinUrl ??= info.ProteinUrl;
                    g.CdsUrl ??= info.CdsUrl;
                }
            }

            return genomes.Select(GenomeDataToInfo).ToList();
        }

        public sealed class AttachGenomeAnalysisRequest
        {
            public string HistoryId { get; set; } = string.Empty;
            public string? OrthoFinderJobId { get; set; }
            [JsonPropertyName("galaxyApiKey")]
            public string? GalaxyApiKey { get; set; }
        }

        [HttpPost("genome-analysis/cancel")]
        public async Task<IActionResult> CancelGenomeAnalysis([FromBody] CancelGenomeAnalysisRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.HistoryId) || string.IsNullOrWhiteSpace(req.GalaxyApiKey))
                return BadRequest(new { error = "historyId and galaxyApiKey are required" });

            using var scope = _galaxy.UseApiKey(req.GalaxyApiKey);
            var contents = await _galaxy.ListHistoryContentsAsync(req.HistoryId, ct, includeHidden: true, includeDeleted: false);
            var activeJobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in contents.EnumerateArray())
            {
                var state = item.TryGetProperty("state", out var stateNode) ? stateNode.GetString() : null;
                if (state is not ("new" or "queued" or "running" or "waiting" or "upload")) continue;
                if (item.TryGetProperty("job_source_id", out var jobNode) && !string.IsNullOrWhiteSpace(jobNode.GetString()))
                    activeJobIds.Add(jobNode.GetString()!);
            }

            var canceled = new List<string>();
            var failed = new List<object>();
            foreach (var jobId in activeJobIds)
            {
                try
                {
                    await _galaxy.CancelJobAsync(jobId, ct);
                    canceled.Add(jobId);
                }
                catch (Exception ex)
                {
                    failed.Add(new { jobId, error = ex.Message });
                }
            }

            ActiveSelectionSubmissions.TryRemove(req.HistoryId, out _);
            return Ok(new { success = failed.Count == 0, canceled, failed });
        }

        public sealed class GalaxyApiKeyRequest
        {
            [JsonPropertyName("galaxyApiKey")]
            public string? GalaxyApiKey { get; set; }
        }

        [HttpPost("genome-analysis/status")]
        public async Task<IActionResult> InspectGenomeAnalysis([FromBody] AttachGenomeAnalysisRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.HistoryId))
                return BadRequest(new { error = "historyId is required" });

            try
            {
                using var scope = _galaxy.UseApiKey(req.GalaxyApiKey);
                using var historyReadCache = _galaxy.UseHistoryReadCache();
                using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                statusCts.CancelAfter(TimeSpan.FromSeconds(45));
                await _galaxy.PrimeHistoryContentsCacheAsync(req.HistoryId, statusCts.Token);
                var contents = await _galaxy.ListHistoryContentsAsync(
                    req.HistoryId,
                    statusCts.Token,
                    includeHidden: true,
                    includeDeleted: false);

                var items = contents.ValueKind == JsonValueKind.Array
                    ? contents.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.Object)
                        .Select(item => new GalaxyHistoryStatusItem(
                            item.TryGetProperty("id", out var id) ? id.GetString() : null,
                            item.TryGetProperty("name", out var name) ? name.GetString() ?? "Unnamed Galaxy item" : "Unnamed Galaxy item",
                            item.TryGetProperty("state", out var state) ? state.GetString() ?? "unknown" : "unknown",
                            item.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var hidValue) ? hidValue : 0,
                            item.TryGetProperty("history_content_type", out var type) ? type.GetString() ?? "dataset" : "dataset"))
                        .OrderByDescending(item => item.Hid)
                        .ToList()
                    : new List<GalaxyHistoryStatusItem>();

                static bool IsActive(string state) => state is "new" or "queued" or "running" or "waiting" or "upload" or "setting_metadata";
                static bool IsFailed(string state) => state is "error" or "failed" or "discarded";
                static bool NameHas(string name, params string[] terms) => terms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));

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
                var latestSelectedOrthogroupItem = items
                    .Where(item => NameHas(item.Name, "selected N0 orthogroups"))
                    .OrderByDescending(item => item.Hid)
                    .FirstOrDefault();
                int? selectedOrthogroupCount = null;
                if (latestSelectedOrthogroupItem is not null)
                {
                    var countMatch = Regex.Match(
                        latestSelectedOrthogroupItem.Name,
                        @"selected\s+N0\s+orthogroups\s*\((?<count>[\d,]+)\)",
                        RegexOptions.IgnoreCase);
                    if (countMatch.Success && int.TryParse(
                            countMatch.Groups["count"].Value.Replace(",", string.Empty),
                            out var parsedCount))
                    {
                        selectedOrthogroupCount = parsedCount;
                    }
                }
                int? strictOrthogroupCount = null;
                var latestStrictOrthogroupItem = items
                    .Where(item => NameHas(item.Name,
                        "strict 1-to-1 N0 orthogroups",
                        "filtered single-copy",
                        "single-copy orthogroup"))
                    .OrderByDescending(item => item.Hid)
                    .FirstOrDefault();
                if (latestStrictOrthogroupItem is not null)
                {
                    var countMatch = Regex.Match(
                        latestStrictOrthogroupItem.Name,
                        @"\((?<count>[\d,]+)\)",
                        RegexOptions.IgnoreCase);
                    if (countMatch.Success && int.TryParse(
                            countMatch.Groups["count"].Value.Replace(",", string.Empty),
                            out var parsedCount))
                    {
                        strictOrthogroupCount = parsedCount;
                    }
                }
                var latestMarkerIsHyphyRetry = markerItems.FirstOrDefault()?.Name.Contains(
                    "hyphy_retry", StringComparison.OrdinalIgnoreCase) == true;
                var generationFloor = latestMarkerIsHyphyRetry
                    ? latestMarkerHid
                    : Math.Max(latestPreparationHid, markerHids.Skip(1).FirstOrDefault());
                var currentWorkflowItems = items.Where(item => item.Hid > generationFloor).ToList();

                var hyphyItems = currentWorkflowItems.Where(item => NameHas(item.Name, "hyphy", "busted", "absrel", "meme")).ToList();
                var downstreamItems = currentWorkflowItems.Where(item => NameHas(
                    item.Name,
                    "mafft",
                    "tranalign",
                    "back-translation",
                    "back translation",
                    "backtranslate",
                    "align_back_trans",
                    "back-translat",
                    "iq-tree",
                    "orthogroup_workflow_invoked")).ToList();
                var workflowPreparationItems = currentWorkflowItems.Where(item => NameHas(
                    item.Name,
                    "orthogroup_ids.zip",
                    "orthogroup ids",
                    "selected N0 orthogroups",
                    "selected orthogroups",
                    "orthogroup_workflow_invoked")).ToList();
                var codonCollections = currentWorkflowItems.Where(item =>
                    item.ContentType.Equals("dataset_collection", StringComparison.OrdinalIgnoreCase) &&
                    NameHas(item.Name, "codon_alignments", "codon alignment", "tranalign", "back-translat", "back translat", "align_back_trans")).ToList();
                var treeCollections = currentWorkflowItems.Where(item =>
                    item.ContentType.Equals("dataset_collection", StringComparison.OrdinalIgnoreCase) &&
                    NameHas(item.Name, "gene_trees", "gene trees", "iq-tree") &&
                    !NameHas(item.Name, "report")).ToList();
                var mafftCollections = items.Where(item =>
                    item.ContentType.Equals("dataset_collection", StringComparison.OrdinalIgnoreCase) &&
                    NameHas(item.Name, "mafft")).ToList();
                var filteredItems = items.Where(item => NameHas(
                    item.Name,
                    "filtered single-copy",
                    "filtered_single_copy",
                    "single copy orthogroup",
                    "single-copy orthogroup",
                    "strict 1-to-1 N0 orthogroups",
                    "filter on dataset")).ToList();
                var orthoOutputItems = items.Where(item => NameHas(item.Name, "hierarchical orthogroup", "orthogroups.tsv", "orthofinder", "species_tree")).ToList();
                var preparedItems = items.Where(item => NameHas(
                    item.Name,
                    "proteome",
                    "protein fasta",
                    "_cds",
                    "coding sequence",
                    "unzip on",
                    "longest protein isoform",
                    "matching cds isoforms")).ToList();

                // Galaxy history summaries do not consistently expose running_jobs
                // and queued_jobs. Dataset and collection states are the authoritative
                // source for this UI because they also include uploads and metadata work.
                var running = items.Count(item => item.State is "running" or "upload" or "setting_metadata");
                var queued = items.Count(item => item.State is "new" or "queued" or "waiting");

                var stage = "initializing";
                var detail = "Galaxy history found; waiting for genome datasets.";
                var failedDownstreamItems = downstreamItems.Where(item => IsFailed(item.State)).ToList();
                var failedHyphyItems = hyphyItems.Where(item => IsFailed(item.State)).ToList();
                if (failedDownstreamItems.Count > 0 || failedHyphyItems.Count > 0)
                {
                    stage = "workflow_failed";
                    var failedItem = failedDownstreamItems.Concat(failedHyphyItems)
                        // A failed upstream job causes dependent collection elements to
                        // become discarded. Report the originating error, not the newest
                        // downstream consequence.
                        .OrderBy(item => item.State is "error" or "failed" ? 0 : 1)
                        .ThenBy(item => item.Hid)
                        .First();
                    var diagnostic = await GetFailedGalaxyItemDiagnosticAsync(req.HistoryId, failedItem, statusCts.Token);
                    detail = $"Galaxy stopped because '{failedItem.Name}' failed."
                        + (string.IsNullOrWhiteSpace(diagnostic) ? string.Empty : $" Galaxy reported: {diagnostic}");
                }
                else if (hyphyItems.Any(item => IsActive(item.State)))
                {
                    stage = "workflow_running";
                    detail = "HyPhy selection tests are active in Galaxy.";
                }
                else if (hyphyItems.Any(item => string.Equals(item.State, "ok", StringComparison.OrdinalIgnoreCase)))
                {
                    stage = "workflow_complete";
                    detail = "HyPhy BUSTED output datasets are complete.";
                }
                else if (codonCollections.Any(item => item.State.Equals("ok", StringComparison.OrdinalIgnoreCase)) &&
                         treeCollections.Any(item => item.State.Equals("ok", StringComparison.OrdinalIgnoreCase)) &&
                         !downstreamItems.Any(item => IsActive(item.State)))
                {
                    stage = "hyphy_ready";
                    detail = "Codon alignments and IQ-TREE gene trees are complete. No selection tests are active; resume HyPhy to validate these inputs and submit the tests.";
                }
                else if (latestMarkerIsHyphyRetry)
                {
                    stage = "workflow_running";
                    detail = "Galaxy accepted the HyPhy-only retry and is preparing MEME, BUSTED and aBSREL collection jobs.";
                }
                else if (mafftCollections.Any(item => item.State.Equals("ok", StringComparison.OrdinalIgnoreCase)) &&
                         !downstreamItems.Any(item => item.State.Equals("ok", StringComparison.OrdinalIgnoreCase) && NameHas(item.Name, "iq-tree")) &&
                         !downstreamItems.Any(item => IsActive(item.State)))
                {
                    stage = "post_mafft_ready";
                    detail = "The MAFFT collection is complete, but back-translation and IQ-TREE were not submitted. The downstream workflow can be resumed without repeating MAFFT.";
                }
                else if (downstreamItems.Any(item => IsActive(item.State)))
                {
                    stage = "workflow_running";
                    detail = downstreamItems.Any(item => IsActive(item.State) && NameHas(item.Name, "iq-tree"))
                        ? "IQ-TREE gene-tree inference is active in Galaxy."
                        : "Alignment or back-translation jobs are active in Galaxy.";
                }
                else if (downstreamItems.Any(item => item.State.Equals("ok", StringComparison.OrdinalIgnoreCase) && NameHas(item.Name, "iq-tree")))
                {
                    stage = "hyphy_ready";
                    detail = "IQ-TREE outputs are complete and no downstream jobs are active. HyPhy tests have not started; resume HyPhy to validate the existing alignments and gene trees and submit the selection tests.";
                }
                else if (downstreamItems.Count > 0)
                {
                    stage = "workflow_pending";
                    detail = "Existing downstream outputs were found, but no alignment, tree-inference, or HyPhy jobs are active. Review the latest Galaxy outputs before continuing.";
                }
                else if (workflowPreparationItems.Count > 0)
                {
                    var preparationFailed = workflowPreparationItems.Any(item => IsFailed(item.State));
                    var preparationActive = workflowPreparationItems.Any(item => IsActive(item.State));
                    stage = preparationFailed
                        ? "workflow_failed"
                        : preparationActive ? "workflow_preparing" : "workflow_pending";
                    detail = preparationFailed
                        ? "Galaxy failed while preparing the selected orthogroups for alignment."
                        : preparationActive
                            ? "Galaxy is preparing the selected orthogroups and genome collections for the alignment workflow."
                            : "Selected orthogroups are prepared; waiting for the alignment workflow to be invoked.";
                }
                else if (filteredItems.Count > 0)
                {
                    stage = "orthogroups_ready";
                    detail = "Filtered orthogroup datasets are available for selection.";
                }
                else if (orthoOutputItems.Count > 0)
                {
                    var orthoActive = orthoOutputItems.Any(item => IsActive(item.State));
                    stage = orthoActive ? "orthofinder_running" : "orthofinder_complete";
                    detail = orthoActive ? "OrthoFinder datasets are still processing." : "OrthoFinder output datasets are complete.";
                }
                else if (preparedItems.Count > 0)
                {
                    var activePreparation = preparedItems.Where(item => IsActive(item.State)).ToList();
                    var failedPreparation = preparedItems.Where(item => IsFailed(item.State)).ToList();
                    if (failedPreparation.Count > 0)
                    {
                        stage = "genome_preparation_failed";
                        var failedItem = failedPreparation.OrderBy(item => item.Hid).First();
                        var diagnostic = await GetFailedGalaxyItemDiagnosticAsync(req.HistoryId, failedItem, statusCts.Token);
                        detail = $"Galaxy stopped while preparing '{failedItem.Name}'."
                            + (string.IsNullOrWhiteSpace(diagnostic) ? string.Empty : $" Galaxy reported: {diagnostic}");
                    }
                    else if (activePreparation.Count > 0)
                    {
                        stage = "uploading";
                        detail = $"Galaxy is downloading, extracting, or filtering genome FASTA files ({activePreparation.Count} active item(s)).";
                    }
                    else
                    {
                        stage = "unzipped";
                        detail = "Genome FASTA datasets are prepared; phyloDIVaS is validating inputs before submitting OrthoFinder.";
                    }
                }

                var currentFailures = failedDownstreamItems.Concat(failedHyphyItems).ToList();
                if (ActiveSelectionSubmissions.ContainsKey(req.HistoryId) &&
                    stage is "hyphy_ready" or "post_mafft_ready" or "workflow_pending")
                {
                    stage = "workflow_preparing";
                    detail = "phyloDIVaS is preparing a downstream submission using the existing Galaxy outputs. HyPhy jobs have not yet been confirmed by Galaxy. A second submission is blocked until preparation finishes.";
                }
                var evidence = items
                    .Where(item => IsActive(item.State)
                        || NameHas(item.Name, "hyphy", "busted", "mafft", "back", "orthogroup", "orthofinder", "proteome", "protein_genomes", "cds", "unzip", "isoform"))
                    .OrderBy(item => item.ContentType.Equals("dataset_collection", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenByDescending(item => item.Hid)
                    .Take(50)
                    .Select(item => new { item.Id, item.Name, item.State, item.Hid, item.ContentType })
                    .ToList();

                return Ok(new
                {
                    success = true,
                    historyId = req.HistoryId,
                    historyName = (string?)null,
                    stage,
                    detail,
                    checkedAt = DateTimeOffset.UtcNow,
                    strictOrthogroupCount,
                    selectedOrthogroupCount,
                    jobs = new { running, queued, failed = currentFailures.Count },
                    evidence
                });
            }
            catch (GalaxyHttpException ghe)
            {
                var statusCode = ghe.StatusCode is 403 or 404 ? ghe.StatusCode : 502;
                return StatusCode(statusCode, new { error = ghe.StatusCode == 403
                    ? "The Galaxy API key cannot access this history. Confirm that both values come from the same Galaxy account and server."
                    : ghe.Message });
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning("Galaxy status inspection timed out for history {HistoryId}", req.HistoryId);
                return StatusCode(503, new
                {
                    error = "Galaxy did not answer the status request in time. The analysis has not been marked as failed; retry shortly."
                });
            }
        }

        public sealed class SpeciesTreeRequest
        {
            public List<string> Species { get; set; } = new();
            public string? NcbiApiKey { get; set; }
        }

        public sealed class SpeciesTreeTimesRequest
        {
            public string Newick { get; set; } = string.Empty;
            public Dictionary<string, string> LeafTaxIds { get; set; } = new();
        }

        public sealed class OrthoGroupAnalysisRequest
        {
            public string HistoryId { get; set; } = string.Empty;
            public OrthoGroupData OrthoGroup { get; set; } = new();
            public List<GenomeData> Genomes { get; set; } = new();
            public string? GalaxyApiKey { get; set; }
            public string? NcbiApiKey { get; set; }
            public string Alignment { get; set; } = "mafft";
        }

        public sealed class MultipleOrthoGroupAnalysisRequest
        {
            public string HistoryId { get; set; } = string.Empty;
            public List<OrthoGroupData> OrthoGroups { get; set; } = new();
            public List<GenomeData> Genomes { get; set; } = new();
            public string? GalaxyApiKey { get; set; }
            public string? NcbiApiKey { get; set; }
            public string Alignment { get; set; } = "mafft";
        }

        public sealed class CancelGenomeAnalysisRequest
        {
            public string HistoryId { get; set; } = string.Empty;
            public string? GalaxyApiKey { get; set; }
        }

        public sealed class RetryHyphyRequest
        {
            public string HistoryId { get; set; } = string.Empty;
            public string? GalaxyApiKey { get; set; }
        }

        public sealed class AnalyzeAllGroupsRequest
        {
            public string HistoryId { get; set; } = string.Empty;
            public string OrthoFinderJobId { get; set; } = string.Empty;
            public List<GenomeData> Genomes { get; set; } = new();
            public string? GalaxyApiKey { get; set; }
            public string? NcbiApiKey { get; set; }
            public string Alignment { get; set; } = "mafft";
        }

        public sealed class OrthoGroupData
        {
            public string GroupId { get; set; } = string.Empty;
            public string GroupName { get; set; } = string.Empty;
            public List<string> GeneIds { get; set; } = new();
            public Dictionary<string, string> SpeciesGenes { get; set; } = new();
        }

        public sealed class OrthogroupAnnotationRequest
        {
            public List<string> Accessions { get; set; } = new();
            public string? NcbiApiKey { get; set; }
        }

        public sealed class GenomeData
        {
            public string Species { get; set; } = string.Empty;
            public string GenomeUrl { get; set; } = string.Empty; // legacy protein FASTA link
            public string DatasetId { get; set; } = string.Empty; // legacy protein dataset id
            public string? ProteinUrl { get; set; }
            public string? ProteinDatasetId { get; set; }
            public string? CdsUrl { get; set; }
            public string? CdsDatasetId { get; set; }
        }
    }
}
