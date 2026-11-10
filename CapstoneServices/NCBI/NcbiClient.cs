using System.Net;
using System.Text.Json;
using System.Web;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.RegularExpressions;


namespace CapstoneServices.NCBI
{
    public sealed class NcbiClient
    {
        private readonly HttpClient _http;
        private readonly string? _defaultApiKey;
        private readonly ILogger<NcbiClient> _log;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
        private static readonly SemaphoreSlim NoKeyRateGate = new(1, 1);
        private static DateTimeOffset _lastNoKeyRequestUtc = DateTimeOffset.MinValue;
        private static readonly TimeSpan NoKeyMinInterval = TimeSpan.FromMilliseconds(350);
        public sealed record SpeciesSuggestion(
            string Scientific,
            string Common,
            string TaxId,
            string Accession,
            string AssemblyName,
            string AssemblyLevel,
            bool IsReference,
            bool HasAnnotation,
            string? AnnotationName,
            long? ScaffoldN50,
            long? ContigN50,
            double? BuscoComplete,
            string? Submitter,
            string? SubmissionDate,
            string? ReleaseDate,
            string? BioProjectAccession,
            string? BioProjectTitle,
            string? Description);

        public sealed record AssemblyPublicationDetails(
            string Accession,
            string? Submitter,
            string? SubmissionDate,
            string? ReleaseDate,
            string? BioProjectAccession,
            string? BioProjectTitle,
            string? Institution,
            IReadOnlyList<string> Contributors);

        public sealed record ReferenceSpeciesTreeResult(
            string Newick,
            IReadOnlyList<string> ResolvedSpecies,
            IReadOnlyList<string> MissingSpecies,
            string Method,
            string Source,
            string RootTaxId,
            IReadOnlyDictionary<string, string> LeafTaxIds,
            IReadOnlyList<string> InternalTaxIds,
            IReadOnlyList<DivergenceTimeEstimate>? DivergenceTimes = null,
            double? MaxDivergenceMa = null,
            double SaturationWarningThresholdMa = DsSaturationWarningThresholdMa);
        public sealed record DivergenceTimeEstimate(
            string CladeKey,
            string SpeciesA,
            string SpeciesB,
            double AgeMa,
            double? LowMa,
            double? HighMa,
            int StudyCount,
            string SourceUrl);
        public sealed record TaxonomyPath(string ScientificName, string TaxId, IReadOnlyList<string> Path);
        public sealed record ProteinAnnotation(string Accession, string Name, string? Organism, string SourceUrl);

        private sealed record TaxonomyLineage(string ScientificName, string TaxId, List<string> Path);
        private sealed class TaxonomyTreeNode
        {
            public TaxonomyTreeNode(string taxId) => TaxId = taxId;
            public string TaxId { get; }
            public SortedDictionary<string, TaxonomyTreeNode> Children { get; } = new(StringComparer.Ordinal);
            public List<string> SelectedLabels { get; } = new();
        }
        private sealed class NewickNode
        {
            public string? Label { get; init; }
            public List<NewickNode> Children { get; } = new();
        }

        private readonly ConcurrentDictionary<string, TaxonomyLineage> _taxonomyLineageCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DivergenceTimeEstimate> _timeTreeCache = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ReferenceSpeciesTreeResult> _referenceSubtreeCache = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ProteinAnnotation> _proteinAnnotationCache = new(StringComparer.OrdinalIgnoreCase);

        public NcbiClient(HttpClient http, IOptions<NcbiOptions> options, ILogger<NcbiClient> log)
        {
            _http = http;
            _defaultApiKey = options.Value.ApiKey?.Trim();
            _log = log;
        }

        private const string Base = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils";
        public const double DsSaturationWarningThresholdMa = 50;

        public async Task<IReadOnlyDictionary<string, ProteinAnnotation>> GetProteinAnnotationsAsync(
            IEnumerable<string> accessions,
            string? apiKey,
            CancellationToken ct)
        {
            var requested = accessions
                .Select(value => value?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(1000)
                .ToList();
            var results = new Dictionary<string, ProteinAnnotation>(StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            foreach (var accession in requested)
            {
                if (_proteinAnnotationCache.TryGetValue(accession, out var cached)) results[accession] = cached;
                else missing.Add(accession);
            }

            var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? _defaultApiKey : apiKey.Trim();
            var throttle = string.IsNullOrWhiteSpace(effectiveApiKey);
            foreach (var batch in missing.Chunk(200))
            {
                var url = $"{Base}/esummary.fcgi?db=protein&retmode=json&id={Uri.EscapeDataString(string.Join(',', batch))}" +
                          (!string.IsNullOrWhiteSpace(effectiveApiKey) ? $"&api_key={effectiveApiKey}" : string.Empty);
                using var response = await GetAsyncWithThrottleAsync(url, throttle, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _log.LogWarning("NCBI protein annotation request failed with {Status}", response.StatusCode);
                    continue;
                }

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                    continue;
                if (!result.TryGetProperty("uids", out var uids) || uids.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var uid in uids.EnumerateArray())
                {
                    var uidValue = uid.GetString();
                    if (string.IsNullOrWhiteSpace(uidValue) || !result.TryGetProperty(uidValue, out var summary)) continue;
                    var accession = GetString(summary, "accessionversion") ?? GetString(summary, "caption");
                    var title = GetString(summary, "title");
                    if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(title)) continue;
                    var organism = GetString(summary, "organism");
                    var displayName = Regex.Replace(title, @"\s*\[[^\]]+\]\s*$", string.Empty).Trim();
                    displayName = Regex.Replace(displayName, @"^LOW QUALITY PROTEIN:\s*", string.Empty, RegexOptions.IgnoreCase);
                    var annotation = new ProteinAnnotation(
                        accession,
                        displayName,
                        organism,
                        $"https://www.ncbi.nlm.nih.gov/protein/{Uri.EscapeDataString(accession)}");
                    _proteinAnnotationCache[accession] = annotation;
                    _proteinAnnotationCache[accession.Split('.')[0]] = annotation;
                    results[accession] = annotation;
                    results[accession.Split('.')[0]] = annotation;
                }
            }

            return results;
        }

        private async Task<string?> ResolveTaxIdAsync(string species, string? apiKey, bool throttle, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(species)) return null;

            species = species
                .Replace("[Organism]", "", StringComparison.OrdinalIgnoreCase)
                .Replace("AND (cds)", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            var term = HttpUtility.UrlEncode(species);

            var url = $"{Base}/esearch.fcgi?db=taxonomy&retmode=json&retmax=1&term={term}" +
                      (apiKey is { Length: > 0 } ? $"&api_key={apiKey}" : string.Empty);

            Console.WriteLine($"[NCBI DEBUG] Encoded species: '{species}'");
            Console.WriteLine($"[NCBI DEBUG] Final URL: {url}");

            using var resp = await GetAsyncWithThrottleAsync(url, throttle, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var s = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("esearchresult", out var er) &&
                er.TryGetProperty("idlist", out var ids) &&
                ids.ValueKind == JsonValueKind.Array &&
                ids.GetArrayLength() > 0)
            {
                var id = ids[0].GetString();
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }

            return null;
        }


        /// <summary>
        /// Get the protein (.faa) download URL for the entire genome of a species using the NCBI Datasets API.
        /// </summary>
        public async Task<(string ProteinFaaUrl, string CdsFnaUrl)?> GetGenomeDownloadUrlAsync(
            string species, string? apiKey = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(species))
            {
                _log.LogWarning("GetGenomeDownloadUrlAsync called with empty species name");
                return (null, null);
            }

            var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? _defaultApiKey : apiKey.Trim();
            var throttle = string.IsNullOrWhiteSpace(effectiveApiKey);

            _log.LogInformation("Fetching genome download URLs for species: {Species}", species);

            try
            {
                // Step 1: Get latest assembly accession for the species
                var encodedSpecies = Uri.EscapeDataString(species);
                var reportUrl = $"https://api.ncbi.nlm.nih.gov/datasets/v2alpha/genome/dataset_report";

                if (!string.IsNullOrWhiteSpace(effectiveApiKey))
                    reportUrl += $"?api_key={effectiveApiKey}";

                // Prepare POST request body - NCBI Datasets API expects specific structure
                var requestBody = new
                {
                    taxons = new[] { species },
                    filters = new
                    {
                        reference_only = false,
                        assembly_source = "refseq"
                    }
                };
                var jsonContent = JsonSerializer.Serialize(requestBody, JsonOpts);
                var httpContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                _log.LogDebug("Requesting NCBI dataset report (POST): {ReportUrl} with body: {Body}", reportUrl, jsonContent);

                using var reportResp = await PostAsyncWithThrottleAsync(reportUrl, httpContent, throttle, ct);
                
                if (!reportResp.IsSuccessStatusCode)
                {
                    var statusCode = (int)reportResp.StatusCode;
                    var reasonPhrase = reportResp.ReasonPhrase ?? "Unknown";
                    
                    _log.LogWarning(
                        "NCBI dataset report request failed for species '{Species}'. " +
                        "Status: {StatusCode} {ReasonPhrase}. URL: {Url}",
                        species, statusCode, reasonPhrase, reportUrl);

                    var errorGuidance = statusCode switch
                    {
                        404 => "Species not found in NCBI database. Verify the species name is correct.",
                        429 => "NCBI API rate limit exceeded. Consider providing an API key or waiting before retrying.",
                        503 => "NCBI service temporarily unavailable. Try again later.",
                        403 => "Access forbidden. Check if your API key is valid.",
                        _ => $"HTTP {statusCode} error from NCBI."
                    };

                    _log.LogWarning("Error guidance: {Guidance}", errorGuidance);
                    return (null, null);
                }

                var json = await reportResp.Content.ReadAsStringAsync(ct);
                _log.LogDebug("Received dataset report JSON ({Length} chars)", json.Length);
                
                // Log first 1000 chars of response to help debug structure issues
                if (_log.IsEnabled(LogLevel.Debug))
                {
                    var preview = json.Length > 1000 ? json.Substring(0, 1000) + "..." : json;
                    _log.LogDebug("NCBI Response preview: {JsonPreview}", preview);
                }

                var accession = ExtractLatestRefSeqAccession(json);
                
                if (string.IsNullOrWhiteSpace(accession))
                {
                    _log.LogWarning(
                        "No RefSeq accession found for species '{Species}'. " +
                        "This species may not have a reference genome assembly in NCBI, or the dataset report is empty. " +
                        "Verify the species name matches NCBI taxonomy exactly.",
                        species);
                    return (null, null);
                }

                _log.LogInformation("Found RefSeq accession {Accession} for species '{Species}'", accession, species);

                // Step 2: Build SEPARATE download URLs

                // → Entire genome in PROTEIN FASTA format (.faa)
                var proteinUrl =
                    $"https://api.ncbi.nlm.nih.gov/datasets/v2alpha/genome/accession/{accession}/download" +
                    "?include_annotation_type=PROT_FASTA";

                // → Entire genome in CDS FASTA format (.fna)
                var cdsUrl =
                    $"https://api.ncbi.nlm.nih.gov/datasets/v2alpha/genome/accession/{accession}/download" +
                    "?include_annotation_type=CDS_FASTA";

                if (!string.IsNullOrWhiteSpace(effectiveApiKey))
                {
                    proteinUrl += $"&api_key={effectiveApiKey}";
                    cdsUrl += $"&api_key={effectiveApiKey}";
                }

                _log.LogInformation(
                    "Successfully generated download URLs for '{Species}' (accession: {Accession})",
                    species, accession);

                // Step 3: Return the two URLs (no HEAD requests needed)
                return (proteinUrl, cdsUrl);
            }
            catch (TaskCanceledException tcex) when (tcex.InnerException is TimeoutException)
            {
                _log.LogError(
                    tcex,
                    "Timeout while fetching genome data for species '{Species}'. " +
                    "NCBI may be slow or unresponsive. Try again or use an API key for better performance.",
                    species);
                return (null, null);
            }
            catch (HttpRequestException hex)
            {
                _log.LogError(
                    hex,
                    "Network error while contacting NCBI for species '{Species}'. " +
                    "Check your internet connection or NCBI service status. Error: {Message}",
                    species, hex.Message);
                return (null, null);
            }
            catch (Exception ex)
            {
                _log.LogError(
                    ex,
                    "Unexpected error while fetching genome download URLs for species '{Species}': {Message}",
                    species, ex.Message);
                return (null, null);
            }
        }

        private string? ExtractLatestRefSeqAccession(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Try to get the reports array - it might be at different paths
                if (root.TryGetProperty("reports", out var reports) && reports.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asm in reports.EnumerateArray())
                    {
                        if (asm.TryGetProperty("accession", out var acc) && acc.ValueKind == JsonValueKind.String)
                        {
                            var accessionValue = acc.GetString();
                            if (!string.IsNullOrWhiteSpace(accessionValue))
                            {
                                _log.LogDebug("Extracted accession: {Accession}", accessionValue);
                                return accessionValue;
                            }
                        }
                    }
                }

                // Log the actual JSON structure for debugging
                _log.LogWarning("Could not find 'reports' array in NCBI response. JSON structure: {Json}", 
                    json.Length > 500 ? json.Substring(0, 500) + "..." : json);
                
                return null;
            }
            catch (JsonException jex)
            {
                _log.LogError(jex, "Failed to parse JSON response from NCBI: {Message}", jex.Message);
                return null;
            }
        }

        public async Task<List<SpeciesSuggestion>> SuggestSpeciesAsync(
            string query,
            string? apiKey = null,
            int limit = 25,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
                return new List<SpeciesSuggestion>();

            var normalizedQuery = query.Trim();
            var queryTerms = await ResolveSuggestionTaxaAsync(normalizedQuery, apiKey, ct);
            queryTerms.Insert(0, normalizedQuery);
            queryTerms = queryTerms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var bestBySpecies = new Dictionary<string, SpeciesSuggestion>(StringComparer.OrdinalIgnoreCase);

            var json = await FetchGenomeDatasetReportAsync(queryTerms, apiKey, limit, ct);
            var parsedSuggestions = ParseSpeciesSuggestions(json, limit);
            var supportedTaxIds = await GetSupportedAnalysisTaxIdsAsync(
                parsedSuggestions.Select(suggestion => suggestion.TaxId), apiKey, ct);

            foreach (var suggestion in parsedSuggestions)
            {
                if (!supportedTaxIds.Contains(suggestion.TaxId))
                    continue;

                if (!bestBySpecies.TryGetValue(suggestion.Scientific, out var existing) || RankSuggestion(suggestion) > RankSuggestion(existing))
                    bestBySpecies[suggestion.Scientific] = suggestion;
            }

            return bestBySpecies.Values
                .OrderByDescending(s => RankSearchRelevance(s, normalizedQuery))
                .Take(Math.Clamp(limit, 1, 250))
                .ToList();
        }

        /// <summary>
        /// Resolves selected taxa with NCBI, builds their minimum taxonomy subtree, and
        /// calibrates its nodes with published TimeTree divergence estimates.
        /// </summary>
        public async Task<ReferenceSpeciesTreeResult> GetReferenceSpeciesTreeAsync(
            IReadOnlyCollection<string> species,
            string? apiKey = null,
            CancellationToken ct = default)
        {
            var names = species.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();
            var lineages = new Dictionary<string, TaxonomyLineage>(StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? _defaultApiKey : apiKey.Trim();
            var throttle = string.IsNullOrWhiteSpace(effectiveApiKey);

            foreach (var name in names)
            {
                var cacheKey = name.Trim().ToLowerInvariant();
                if (!_taxonomyLineageCache.TryGetValue(cacheKey, out var resolved))
                {
                    var taxId = await ResolveTaxIdAsync(name, effectiveApiKey, throttle, ct);
                    if (string.IsNullOrWhiteSpace(taxId))
                    {
                        missing.Add(name);
                        continue;
                    }

                    var url = $"https://api.ncbi.nlm.nih.gov/datasets/v2alpha/taxonomy/taxon/{Uri.EscapeDataString(taxId)}/dataset_report";
                    if (!string.IsNullOrWhiteSpace(effectiveApiKey)) url += $"?api_key={Uri.EscapeDataString(effectiveApiKey)}";
                    using var response = await GetAsyncWithThrottleAsync(url, throttle, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        missing.Add(name);
                        continue;
                    }

                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    if (!document.RootElement.TryGetProperty("reports", out var reports) || reports.GetArrayLength() == 0 ||
                        !reports[0].TryGetProperty("taxonomy", out var taxonomy))
                    {
                        missing.Add(name);
                        continue;
                    }

                    var path = new List<string>();
                    if (taxonomy.TryGetProperty("parents", out var parents) && parents.ValueKind == JsonValueKind.Array)
                        path.AddRange(parents.EnumerateArray().Select(parent => parent.GetRawText()));
                    path.Add(taxId);
                    var currentName = taxonomy.TryGetProperty("current_scientific_name", out var currentNameObject)
                        ? GetString(currentNameObject, "name")
                        : null;
                    resolved = new TaxonomyLineage(currentName ?? name, taxId, path);
                    _taxonomyLineageCache[cacheKey] = resolved;
                    _taxonomyLineageCache.TryAdd(taxId, resolved);
                }
                lineages[resolved.ScientificName] = resolved;
            }

            if (lineages.Count < 2)
                return new ReferenceSpeciesTreeResult(string.Empty, lineages.Keys.ToList(), missing, "Rooted induced reference subtree", "NCBI Taxonomy", string.Empty, new Dictionary<string, string>(), Array.Empty<string>());

            var cacheId = string.Join("|", lineages.Values.Select(value => value.TaxId).OrderBy(value => value, StringComparer.Ordinal));
            if (_referenceSubtreeCache.TryGetValue(cacheId, out var cached))
                return cached with { MissingSpecies = missing };

            var result = BuildReferenceTreeFromLineages(lineages.Values
                .Select(value => new TaxonomyPath(value.ScientificName, value.TaxId, value.Path))
                .ToList(), missing);
            var divergenceTimes = await GetTimeTreeEstimatesAsync(result.Newick, result.LeafTaxIds, ct);
            var maxDivergenceMa = await GetTimeTreeMrcaAgeAsync(result.LeafTaxIds.Values, ct);
            result = result with
            {
                Method = "TimeTree-calibrated minimum taxonomy subtree",
                Source = "TimeTree of Life divergence estimates; topology seeded from NCBI Taxonomy",
                DivergenceTimes = divergenceTimes,
                MaxDivergenceMa = maxDivergenceMa ?? divergenceTimes.Select(value => (double?)value.AgeMa).Max()
            };
            _referenceSubtreeCache[cacheId] = result;
            return result;
        }

        public async Task<IReadOnlyList<DivergenceTimeEstimate>> GetTimeTreeEstimatesAsync(
            string newick,
            IReadOnlyDictionary<string, string> leafTaxIds,
            CancellationToken ct)
        {
            try
            {
                var position = 0;
                var root = ParseNewickNode(newick, ref position);
                var taxIdByLabel = leafTaxIds.ToDictionary(
                    value => value.Key.Replace(' ', '_'),
                    value => value.Value,
                    StringComparer.Ordinal);
                var requests = new List<(string CladeKey, string LabelA, string LabelB, string TaxIdA, string TaxIdB)>();

                CollectTimeTreeRequests(root, taxIdByLabel, requests);
                var estimates = await Task.WhenAll(requests.Select(request => GetTimeTreeEstimateAsync(request, ct)));
                return estimates.Where(value => value is not null).Cast<DivergenceTimeEstimate>().ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogDebug(ex, "TimeTree enrichment was unavailable; returning the reference topology without dates.");
                return Array.Empty<DivergenceTimeEstimate>();
            }
        }

        private static List<string> CollectTimeTreeRequests(
            NewickNode node,
            IReadOnlyDictionary<string, string> taxIdByLabel,
            ICollection<(string CladeKey, string LabelA, string LabelB, string TaxIdA, string TaxIdB)> requests)
        {
            if (node.Children.Count == 0)
                return string.IsNullOrWhiteSpace(node.Label) ? new List<string>() : new List<string> { node.Label };

            var childLeaves = node.Children.Select(child => CollectTimeTreeRequests(child, taxIdByLabel, requests)).ToList();
            var leaves = childLeaves.SelectMany(value => value).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList();
            var representatives = childLeaves.Where(value => value.Count > 0).Select(value => value[0]).Take(2).ToList();
            if (representatives.Count == 2 && taxIdByLabel.TryGetValue(representatives[0], out var taxIdA) && taxIdByLabel.TryGetValue(representatives[1], out var taxIdB))
                requests.Add((string.Join('|', leaves), representatives[0], representatives[1], taxIdA, taxIdB));
            return leaves;
        }

        private async Task<DivergenceTimeEstimate?> GetTimeTreeEstimateAsync(
            (string CladeKey, string LabelA, string LabelB, string TaxIdA, string TaxIdB) request,
            CancellationToken ct)
        {
            var pairKey = string.CompareOrdinal(request.TaxIdA, request.TaxIdB) < 0
                ? $"{request.TaxIdA}|{request.TaxIdB}"
                : $"{request.TaxIdB}|{request.TaxIdA}";
            if (_timeTreeCache.TryGetValue(pairKey, out var cached))
                return cached with { CladeKey = request.CladeKey };

            var sourceUrl = $"https://api.timetree.org/pairwise/{Uri.EscapeDataString(request.TaxIdA)}/{Uri.EscapeDataString(request.TaxIdB)}/json";
            using var response = await _http.GetAsync(sourceUrl, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var root = document.RootElement;
            var age = GetDouble(root, "sum_median_time") ?? GetDouble(root, "sum_simple_mol_time");
            if (!age.HasValue || age <= 0) return null;
            var studies = GetObject(root, "studies");
            var estimate = new DivergenceTimeEstimate(
                request.CladeKey,
                request.LabelA.Replace('_', ' '),
                request.LabelB.Replace('_', ' '),
                age.Value,
                studies.HasValue ? GetDouble(studies.Value, "precomputed_ci_low") : null,
                studies.HasValue ? GetDouble(studies.Value, "precomputed_ci_high") : null,
                (int)(GetLong(root, "all_total") ?? 0),
                sourceUrl);
            _timeTreeCache[pairKey] = estimate;
            return estimate;
        }

        private async Task<double?> GetTimeTreeMrcaAgeAsync(IEnumerable<string> taxIds, CancellationToken ct)
        {
            var ids = taxIds.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            if (ids.Count < 2) return null;
            try
            {
                var joinedIds = Uri.EscapeDataString(string.Join('+', ids));
                using var response = await _http.GetAsync($"https://api.timetree.org/mrca/id/{joinedIds}/json", ct);
                if (!response.IsSuccessStatusCode) return null;
                using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
                return GetDouble(document.RootElement, "adjusted_age")
                    ?? GetDouble(document.RootElement, "precomputed_age");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogDebug(ex, "TimeTree MRCA age was unavailable for {TaxonCount} selected taxa.", ids.Count);
                return null;
            }
        }

        private static NewickNode ParseNewickNode(string text, ref int position)
        {
            var node = new NewickNode();
            if (position < text.Length && text[position] == '(')
            {
                position++;
                do
                {
                    node.Children.Add(ParseNewickNode(text, ref position));
                    if (position < text.Length && text[position] == ',') position++;
                    else break;
                } while (position < text.Length);
                if (position < text.Length && text[position] == ')') position++;
                while (position < text.Length && text[position] is not ',' and not ')' and not ';') position++;
                return node;
            }

            var start = position;
            while (position < text.Length && text[position] is not ',' and not ')' and not ';' and not ':') position++;
            var label = text[start..position];
            while (position < text.Length && text[position] is not ',' and not ')' and not ';') position++;
            return new NewickNode { Label = label };
        }

        private static string SerializeCollapsedNewick(NewickNode node)
        {
            if (node.Children.Count == 0) return node.Label ?? string.Empty;
            if (node.Children.Count == 1) return SerializeCollapsedNewick(node.Children[0]);
            return $"({string.Join(',', node.Children.Select(SerializeCollapsedNewick))})";
        }

        public static ReferenceSpeciesTreeResult BuildReferenceTreeFromLineages(
            IReadOnlyCollection<TaxonomyPath> lineages,
            IReadOnlyList<string>? missingSpecies = null)
        {
            var uniqueLineages = lineages.GroupBy(value => value.TaxId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            if (uniqueLineages.Count < 2)
                return new ReferenceSpeciesTreeResult(string.Empty, uniqueLineages.Select(value => value.ScientificName).ToList(), missingSpecies ?? Array.Empty<string>(), "Rooted minimum induced subtree", "NCBI Taxonomy parent relationships", string.Empty, new Dictionary<string, string>(), Array.Empty<string>());

            var paths = uniqueLineages.Select(value => value.Path).ToList();
            var commonLength = paths.Min(path => path.Count);
            var mrcaIndex = 0;
            while (mrcaIndex < commonLength && paths.All(path => path[mrcaIndex] == paths[0][mrcaIndex])) mrcaIndex++;
            mrcaIndex = Math.Max(0, mrcaIndex - 1);
            var root = new TaxonomyTreeNode(paths[0][mrcaIndex]);

            foreach (var lineage in uniqueLineages)
            {
                var node = root;
                foreach (var taxId in lineage.Path.Skip(mrcaIndex + 1))
                {
                    if (!node.Children.TryGetValue(taxId, out var child))
                    {
                        child = new TaxonomyTreeNode(taxId);
                        node.Children[taxId] = child;
                    }
                    node = child;
                }
                node.SelectedLabels.Add(lineage.ScientificName);
            }

            var internalIds = new HashSet<string>(StringComparer.Ordinal);
            var newick = SerializeReferenceTree(root, internalIds, isRoot: true) + ";";
            return new ReferenceSpeciesTreeResult(
                newick,
                uniqueLineages.Select(value => value.ScientificName).OrderBy(value => value).ToList(),
                missingSpecies ?? Array.Empty<string>(),
                "Rooted minimum induced subtree",
                "NCBI Taxonomy parent relationships",
                root.TaxId,
                uniqueLineages.ToDictionary(value => value.ScientificName, value => value.TaxId),
                internalIds.OrderBy(value => value, StringComparer.Ordinal).ToList());
        }

        private static string SerializeReferenceTree(TaxonomyTreeNode node, ISet<string> internalIds, bool isRoot = false)
        {
            var childParts = node.Children.Values.Select(child => SerializeReferenceTree(child, internalIds)).ToList();
            childParts.AddRange(node.SelectedLabels.Select(label => label.Replace(' ', '_')));
            if (!isRoot && node.SelectedLabels.Count == 0 && childParts.Count == 1)
                return childParts[0];
            if (childParts.Count == 1) return childParts[0];
            internalIds.Add(node.TaxId);
            return $"({string.Join(',', childParts)})";
        }

        private async Task<HashSet<string>> GetSupportedAnalysisTaxIdsAsync(
            IEnumerable<string> taxIds,
            string? apiKey,
            CancellationToken ct)
        {
            var ids = taxIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ids.Count == 0) return supported;

            var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? _defaultApiKey : apiKey.Trim();
            var throttle = string.IsNullOrWhiteSpace(effectiveApiKey);
            var url = $"{Base}/esummary.fcgi?db=taxonomy&retmode=json&id={Uri.EscapeDataString(string.Join(',', ids))}" +
                      (!string.IsNullOrWhiteSpace(effectiveApiKey) ? $"&api_key={effectiveApiKey}" : string.Empty);

            using var response = await GetAsyncWithThrottleAsync(url, throttle, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("Unable to classify suggested taxa for analysis eligibility: {Status}", response.StatusCode);
                return supported;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                return supported;

            foreach (var taxId in ids)
            {
                if (!result.TryGetProperty(taxId, out var summary) || summary.ValueKind != JsonValueKind.Object)
                    continue;

                var division = GetString(summary, "division") ?? string.Empty;
                if (!division.Equals("Viruses", StringComparison.OrdinalIgnoreCase))
                    supported.Add(taxId);
            }

            return supported;
        }

        private async Task<string> FetchGenomeDatasetReportAsync(IEnumerable<string> taxonQueries, string? apiKey, int limit, CancellationToken ct)
        {
            var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? _defaultApiKey : apiKey.Trim();
            var throttle = string.IsNullOrWhiteSpace(effectiveApiKey);
            var reportUrl = "https://api.ncbi.nlm.nih.gov/datasets/v2alpha/genome/dataset_report";
            if (!string.IsNullOrWhiteSpace(effectiveApiKey))
                reportUrl += $"?api_key={effectiveApiKey}";

            var taxons = taxonQueries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var reports = new List<JsonElement>();
            string? pageToken = null;
            var requestedReports = Math.Clamp(limit * 4, 100, 1000);

            do
            {
                var requestBody = new
                {
                    taxons,
                    filters = new { assembly_source = "refseq" },
                    page_size = 100,
                    page_token = pageToken
                };
                var jsonContent = JsonSerializer.Serialize(requestBody, JsonOpts);
                using var httpContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
                using var reportResp = await PostAsyncWithThrottleAsync(reportUrl, httpContent, throttle, ct);
                if (!reportResp.IsSuccessStatusCode)
                {
                    _log.LogWarning("NCBI species suggestion request failed: {Status}", reportResp.StatusCode);
                    break;
                }

                using var document = JsonDocument.Parse(await reportResp.Content.ReadAsStreamAsync(ct));
                if (document.RootElement.TryGetProperty("reports", out var pageReports) && pageReports.ValueKind == JsonValueKind.Array)
                    reports.AddRange(pageReports.EnumerateArray().Select(report => report.Clone()));
                pageToken = document.RootElement.TryGetProperty("next_page_token", out var nextToken)
                    ? nextToken.GetString()
                    : null;
            } while (!string.IsNullOrWhiteSpace(pageToken) && reports.Count < requestedReports);

            return JsonSerializer.Serialize(new { reports }, JsonOpts);
        }

        private async Task<List<string>> ResolveSuggestionTaxaAsync(string query, string? apiKey, CancellationToken ct)
        {
            var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? _defaultApiKey : apiKey.Trim();
            var throttle = string.IsNullOrWhiteSpace(effectiveApiKey);
            var results = new List<string>();

            // Exact matching against every NCBI taxonomy name class catches scientific names,
            // common names, synonyms, and informal group names without a local alias list.
            var term = HttpUtility.UrlEncode($"{query}[All Names]");
            var searchUrl = $"{Base}/esearch.fcgi?db=taxonomy&retmode=json&retmax=10&term={term}" +
                            (!string.IsNullOrWhiteSpace(effectiveApiKey) ? $"&api_key={effectiveApiKey}" : string.Empty);
            using (var response = await GetAsyncWithThrottleAsync(searchUrl, throttle, ct))
            {
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    if (document.RootElement.TryGetProperty("esearchresult", out var searchResult) &&
                        searchResult.TryGetProperty("idlist", out var ids) && ids.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var id in ids.EnumerateArray())
                        {
                            var taxId = id.GetString();
                            if (!string.IsNullOrWhiteSpace(taxId))
                                await AddTaxonAndFamilyAsync(taxId, query, effectiveApiKey, throttle, results, ct);
                        }
                    }
                }
            }

            // NCBI's suggestion service also finds partial common names such as "bay cat".
            var suggestUrl = $"https://api.ncbi.nlm.nih.gov/datasets/v2alpha/taxonomy/taxon_suggest/{Uri.EscapeDataString(query)}?limit=30";
            if (!string.IsNullOrWhiteSpace(effectiveApiKey))
                suggestUrl += $"&api_key={effectiveApiKey}";
            using (var response = await GetAsyncWithThrottleAsync(suggestUrl, throttle, ct))
            {
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    if (document.RootElement.TryGetProperty("sci_name_and_ids", out var matches) && matches.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var match in matches.EnumerateArray())
                        {
                            var common = GetString(match, "common_name") ?? string.Empty;
                            var matched = GetString(match, "matched_term") ?? string.Empty;
                            if (!ContainsWord(common, query) && !string.Equals(matched, query, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var taxId = GetString(match, "tax_id");
                            if (!string.IsNullOrWhiteSpace(taxId))
                                results.Add(taxId);
                        }
                    }
                }
            }

            return results;
        }

        private async Task AddTaxonAndFamilyAsync(
            string taxId, string query, string? apiKey, bool throttle, List<string> results, CancellationToken ct)
        {
            results.Add(taxId);
            var url = $"https://api.ncbi.nlm.nih.gov/datasets/v2alpha/taxonomy/taxon/{taxId}/dataset_report";
            if (!string.IsNullOrWhiteSpace(apiKey)) url += $"?api_key={apiKey}";

            using var response = await GetAsyncWithThrottleAsync(url, throttle, ct);
            if (!response.IsSuccessStatusCode) return;
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("reports", out var reports) || reports.GetArrayLength() == 0) return;

            var taxonomy = GetObject(reports[0], "taxonomy");
            if (!taxonomy.HasValue) return;
            var rank = GetString(taxonomy.Value, "rank") ?? string.Empty;
            var common = GetString(taxonomy.Value, "curator_common_name") ?? string.Empty;

            // A plain-language species query such as "cat" should also discover its close relatives.
            // Scientific-name searches remain scoped to the requested taxon.
            if (!rank.Equals("SPECIES", StringComparison.OrdinalIgnoreCase) || !ContainsWord(common, query)) return;
            var classification = GetObject(taxonomy.Value, "classification");
            var family = classification.HasValue ? GetObject(classification.Value, "family") : null;
            var familyId = family.HasValue ? GetString(family.Value, "id") : null;
            if (!string.IsNullOrWhiteSpace(familyId)) results.Add(familyId);
        }

        private static bool ContainsWord(string value, string query)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(query)) return false;
            return value.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private List<SpeciesSuggestion> ParseSpeciesSuggestions(string json, int limit)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("reports", out var reports) || reports.ValueKind != JsonValueKind.Array)
                return new List<SpeciesSuggestion>();

            var bestBySpecies = new Dictionary<string, SpeciesSuggestion>(StringComparer.OrdinalIgnoreCase);
            foreach (var report in reports.EnumerateArray())
            {
                var accession = GetString(report, "accession");
                var sourceDatabase = GetString(report, "source_database");
                if (string.IsNullOrWhiteSpace(accession) || !accession.StartsWith("GCF_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrWhiteSpace(sourceDatabase) && !sourceDatabase.Contains("REFSEQ", StringComparison.OrdinalIgnoreCase))
                    continue;

                var organism = GetObject(report, "organism");
                var scientific = organism.HasValue ? GetString(organism.Value, "organism_name") : null;
                if (string.IsNullOrWhiteSpace(scientific))
                    continue;

                var assemblyInfo = GetObject(report, "assembly_info");
                var annotationInfo = GetObject(report, "annotation_info");
                var annotationName =
                    (annotationInfo.HasValue ? GetString(annotationInfo.Value, "name") : null)
                    ?? (annotationInfo.HasValue ? GetString(annotationInfo.Value, "annotation_name") : null)
                    ?? (assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "annotation_name") : null);

                if (assemblyInfo.HasValue)
                {
                    var pairedAssembly = GetObject(assemblyInfo.Value, "paired_assembly");
                    annotationName ??= pairedAssembly.HasValue ? GetString(pairedAssembly.Value, "annotation_name") : null;
                }

                var common = organism.HasValue ? GetString(organism.Value, "common_name") ?? string.Empty : string.Empty;
                var hasAnnotation = !string.IsNullOrWhiteSpace(annotationName);
                if (!hasAnnotation)
                    continue;

                var assemblyStats = GetObject(report, "assembly_stats");
                var busco = annotationInfo.HasValue ? GetObject(annotationInfo.Value, "busco") : null;
                var bioProjectTitle = assemblyInfo.HasValue
                    ? GetBioProjectTitle(assemblyInfo.Value)
                    : null;

                var suggestion = new SpeciesSuggestion(
                    Scientific: scientific,
                    Common: common,
                    TaxId: organism.HasValue ? GetString(organism.Value, "tax_id") ?? string.Empty : string.Empty,
                    Accession: accession,
                    AssemblyName: assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "assembly_name") ?? string.Empty : string.Empty,
                    AssemblyLevel: assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "assembly_level") ?? string.Empty : string.Empty,
                    IsReference: string.Equals(assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "refseq_category") : null, "reference genome", StringComparison.OrdinalIgnoreCase),
                    HasAnnotation: hasAnnotation,
                    AnnotationName: annotationName,
                    ScaffoldN50: assemblyStats.HasValue ? GetLong(assemblyStats.Value, "scaffold_n50") : null,
                    ContigN50: assemblyStats.HasValue ? GetLong(assemblyStats.Value, "contig_n50") : null,
                    BuscoComplete: busco.HasValue ? GetDouble(busco.Value, "complete") : null,
                    Submitter: assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "submitter") : null,
                    SubmissionDate: assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "submission_date") : null,
                    ReleaseDate: assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "release_date") : null,
                    BioProjectAccession: assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "bioproject_accession") : null,
                    BioProjectTitle: bioProjectTitle,
                    Description: assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "description") : null);

                if (!bestBySpecies.TryGetValue(scientific, out var existing) || RankSuggestion(suggestion) > RankSuggestion(existing))
                    bestBySpecies[scientific] = suggestion;
            }

            return bestBySpecies.Values.ToList();
        }

        public async Task<AssemblyPublicationDetails?> GetAssemblyPublicationDetailsAsync(
            string accession,
            string? apiKey = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(accession)) return null;
            var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? _defaultApiKey : apiKey.Trim();
            var throttle = string.IsNullOrWhiteSpace(effectiveApiKey);
            var reportUrl = $"https://api.ncbi.nlm.nih.gov/datasets/v2/genome/accession/{Uri.EscapeDataString(accession.Trim())}/dataset_report";
            if (!string.IsNullOrWhiteSpace(effectiveApiKey)) reportUrl += $"?api_key={Uri.EscapeDataString(effectiveApiKey)}";

            using var response = await GetAsyncWithThrottleAsync(reportUrl, throttle, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!document.RootElement.TryGetProperty("reports", out var reports) || reports.GetArrayLength() == 0) return null;

            var assemblyInfo = GetObject(reports[0], "assembly_info");
            var bioProject = assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "bioproject_accession") : null;
            var title = assemblyInfo.HasValue ? GetBioProjectTitle(assemblyInfo.Value) : null;
            string? institution = null;
            var contributors = new List<string>();

            if (!string.IsNullOrWhiteSpace(bioProject))
            {
                try
                {
                    var term = HttpUtility.UrlEncode($"{bioProject}[Project Accession]");
                    var keySuffix = string.IsNullOrWhiteSpace(effectiveApiKey) ? string.Empty : $"&api_key={Uri.EscapeDataString(effectiveApiKey)}";
                    var searchUrl = $"{Base}/esearch.fcgi?db=bioproject&retmode=json&retmax=1&term={term}{keySuffix}";
                    using var searchResponse = await GetAsyncWithThrottleAsync(searchUrl, throttle, ct);
                    if (searchResponse.IsSuccessStatusCode)
                    {
                        using var searchStream = await searchResponse.Content.ReadAsStreamAsync(ct);
                        using var searchDoc = await JsonDocument.ParseAsync(searchStream, cancellationToken: ct);
                        var idList = searchDoc.RootElement.GetProperty("esearchresult").GetProperty("idlist");
                        var id = idList.ValueKind == JsonValueKind.Array && idList.GetArrayLength() > 0
                            ? idList[0].GetString()
                            : null;
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            var fetchUrl = $"{Base}/efetch.fcgi?db=bioproject&id={Uri.EscapeDataString(id)}{keySuffix}";
                            using var fetchResponse = await GetAsyncWithThrottleAsync(fetchUrl, throttle, ct);
                            if (fetchResponse.IsSuccessStatusCode)
                            {
                                var xml = XDocument.Parse(await fetchResponse.Content.ReadAsStringAsync(ct));
                                title ??= FirstXmlValue(xml, "Title", "Name");
                                institution = FirstXmlValue(xml, "Organization", "Institution", "Consortium");
                                contributors = xml.Descendants()
                                    .Where(e => e.Name.LocalName is "Contact" or "Author" or "Contributor")
                                    .Select(FormatContributor)
                                    .Where(value => !string.IsNullOrWhiteSpace(value))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .Take(20)
                                    .ToList()!;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "BioProject contributor metadata was unavailable for {Accession}", bioProject);
                }
            }

            return new AssemblyPublicationDetails(
                accession.Trim(),
                assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "submitter") : null,
                assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "submission_date") : null,
                assemblyInfo.HasValue ? GetString(assemblyInfo.Value, "release_date") : null,
                bioProject,
                title,
                institution,
                contributors);
        }

        private static string? GetBioProjectTitle(JsonElement assemblyInfo)
        {
            if (!assemblyInfo.TryGetProperty("bioproject_lineage", out var lineage) || lineage.ValueKind != JsonValueKind.Array) return null;
            foreach (var line in lineage.EnumerateArray())
            {
                if (!line.TryGetProperty("bioprojects", out var projects) || projects.ValueKind != JsonValueKind.Array) continue;
                foreach (var project in projects.EnumerateArray())
                {
                    var title = GetString(project, "title");
                    if (!string.IsNullOrWhiteSpace(title)) return title;
                }
            }
            return null;
        }

        private static string? FirstXmlValue(XDocument document, params string[] names) => document.Descendants()
            .FirstOrDefault(element => names.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(element.Value))
            ?.Value.Trim();

        private static string FormatContributor(XElement element)
        {
            var name = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "Name");
            if (name is null) return element.Value.Trim();
            var parts = name.Descendants().Where(e => e.Name.LocalName is "First" or "Middle" or "Last").Select(e => e.Value.Trim()).Where(v => v.Length > 0);
            var formatted = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(formatted) ? name.Value.Trim() : formatted;
        }

        private static int RankSuggestion(SpeciesSuggestion suggestion)
        {
            var score = 0;
            if (suggestion.IsReference) score += 100;
            if (suggestion.HasAnnotation) score += 25;
            if (!string.IsNullOrWhiteSpace(suggestion.Common)) score += 5;
            if (suggestion.BuscoComplete.HasValue) score += (int)Math.Round(suggestion.BuscoComplete.Value * 20);
            if (suggestion.ScaffoldN50.HasValue) score += (int)Math.Min(20, suggestion.ScaffoldN50.Value / 10_000_000);

            score += suggestion.AssemblyLevel?.ToLowerInvariant() switch
            {
                "chromosome" => 40,
                "complete genome" => 35,
                "scaffold" => 20,
                "contig" => 10,
                _ => 0
            };

            return score;
        }

        private static int RankSearchRelevance(SpeciesSuggestion suggestion, string query)
        {
            var normalizedQuery = query.Trim();
            if (normalizedQuery.Length == 0) return 0;

            if (string.Equals(suggestion.Common, normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(suggestion.Scientific, normalizedQuery, StringComparison.OrdinalIgnoreCase))
                return 1000;

            if (suggestion.Common.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                suggestion.Scientific.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                return 750;

            if (ContainsWholeWord(suggestion.Common, normalizedQuery) ||
                ContainsWholeWord(suggestion.Scientific, normalizedQuery))
                return 600;

            if (suggestion.Common.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                suggestion.Scientific.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                return 400;

            return 0;
        }

        private static bool ContainsWholeWord(string value, string query)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.Split(new[] { ' ', '-', '_', '/', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(part => string.Equals(part, query, StringComparison.OrdinalIgnoreCase));
        }

        private static JsonElement? GetObject(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Object
                ? property
                : null;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                _ => null
            };
        }

        private static long? GetLong(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property)) return null;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number)) return number;
            return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out number) ? number : null;
        }

        private static double? GetDouble(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property)) return null;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number)) return number;
            return property.ValueKind == JsonValueKind.String && double.TryParse(property.GetString(), out number) ? number : null;
        }

        public async Task<string?> GetGenomeCdsDownloadUrlAsync(string species, string? apiKey = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(species)) return null;

            var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? _defaultApiKey : apiKey?.Trim();
            var throttle = string.IsNullOrWhiteSpace(effectiveApiKey);

            var taxId = await ResolveTaxIdAsync(species, effectiveApiKey, throttle, ct);
            if (string.IsNullOrWhiteSpace(taxId))
                return null;

            var datasetsUrl = $"https://api.ncbi.nlm.nih.gov/datasets/v2alpha/genome/taxon/{taxId}/download?include_annotation_type=CDS_FASTA";

            using var resp = await GetAsyncWithThrottleAsync(datasetsUrl, throttle, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            if (resp.Headers.Location != null)
            {
                return resp.Headers.Location.ToString();
            }

            return null;
        }

        private async Task<HttpResponseMessage> GetAsyncWithThrottleAsync(string url, bool throttle, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (throttle)
                {
                    await NoKeyRateGate.WaitAsync(ct);
                    try
                    {
                        var now = DateTimeOffset.UtcNow;
                        var elapsed = now - _lastNoKeyRequestUtc;
                        if (elapsed < NoKeyMinInterval)
                        {
                            var delay = NoKeyMinInterval - elapsed;
                            if (delay > TimeSpan.Zero)
                                await Task.Delay(delay, ct);
                        }
                        _lastNoKeyRequestUtc = DateTimeOffset.UtcNow;
                    }
                    finally
                    {
                        NoKeyRateGate.Release();
                    }
                }

                var resp = await _http.GetAsync(url, ct);
                if (resp.StatusCode == (HttpStatusCode)429 || resp.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    var retryDelay = GetRetryDelay(resp);
                    resp.Dispose();
                    if (attempt == 2)
                        throw new HttpRequestException("NCBI rate limit exceeded.", null, resp.StatusCode);

                    await Task.Delay(retryDelay, ct);
                    continue;
                }

                return resp;
            }

            throw new InvalidOperationException("Failed to contact NCBI after retries.");
        }

        private async Task<HttpResponseMessage> PostAsyncWithThrottleAsync(string url, HttpContent content, bool throttle, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (throttle)
                {
                    await NoKeyRateGate.WaitAsync(ct);
                    try
                    {
                        var now = DateTimeOffset.UtcNow;
                        var elapsed = now - _lastNoKeyRequestUtc;
                        if (elapsed < NoKeyMinInterval)
                        {
                            var delay = NoKeyMinInterval - elapsed;
                            if (delay > TimeSpan.Zero)
                                await Task.Delay(delay, ct);
                        }
                        _lastNoKeyRequestUtc = DateTimeOffset.UtcNow;
                    }
                    finally
                    {
                        NoKeyRateGate.Release();
                    }
                }

                var resp = await _http.PostAsync(url, content, ct);
                if (resp.StatusCode == (HttpStatusCode)429 || resp.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    var retryDelay = GetRetryDelay(resp);
                    resp.Dispose();
                    if (attempt == 2)
                        throw new HttpRequestException("NCBI rate limit exceeded.", null, resp.StatusCode);

                    await Task.Delay(retryDelay, ct);
                    continue;
                }

                return resp;
            }

            throw new InvalidOperationException("Failed to contact NCBI after retries.");
        }

        private static TimeSpan GetRetryDelay(HttpResponseMessage resp)
        {
            if (resp.Headers.RetryAfter is { } retryAfter)
            {
                if (retryAfter.Delta.HasValue)
                    return retryAfter.Delta.Value;
                if (retryAfter.Date.HasValue)
                {
                    var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                    if (delta > TimeSpan.Zero)
                        return delta;
                }
            }
            return TimeSpan.FromSeconds(1);
        }
    }
}
