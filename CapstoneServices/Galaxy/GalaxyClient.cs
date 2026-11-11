using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Options;

namespace CapstoneServices.Galaxy
{
    public sealed class GalaxyClient
    {
        private readonly HttpClient _http;
        private readonly GalaxyOptions _opts;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private static readonly AsyncLocal<string?> ApiKeyOverride = new();
        private static readonly AsyncLocal<Dictionary<string, JsonElement>?> HistoryReadCache = new();

        public GalaxyClient(HttpClient http, IOptions<GalaxyOptions> opts)
        {
            _http = http;
            _opts = opts.Value;
            if (!string.IsNullOrWhiteSpace(_opts.BaseUrl))
            {
                var baseUrl = _opts.BaseUrl!.TrimEnd('/');
                // If user configured with "/api" suffix, strip it because we prefix "/api" in requests
                if (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
                    baseUrl = baseUrl[..^4];
                _http.BaseAddress = new Uri(baseUrl + "/");
            }
            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private string GetEffectiveApiKey() => ApiKeyOverride.Value
            ?? throw new InvalidOperationException("A request-scoped Galaxy API key is required.");

        private sealed class ApiKeyScope : IDisposable
        {
            private readonly string? _previous;
            private readonly Dictionary<string, JsonElement>? _previousCache;
            public ApiKeyScope(string? previous, Dictionary<string, JsonElement>? previousCache)
            {
                _previous = previous;
                _previousCache = previousCache;
            }
            public void Dispose()
            {
                ApiKeyOverride.Value = _previous;
                HistoryReadCache.Value = _previousCache;
            }
        }

        private sealed class HistoryReadCacheScope : IDisposable
        {
            private readonly Dictionary<string, JsonElement>? _previous;
            public HistoryReadCacheScope(Dictionary<string, JsonElement>? previous) => _previous = previous;
            public void Dispose() => HistoryReadCache.Value = _previous;
        }

        public IDisposable UseApiKey(string? apiKey)
        {
            var prior = ApiKeyOverride.Value;
            var priorCache = HistoryReadCache.Value;
            HistoryReadCache.Value = priorCache is null ? null : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            ApiKeyOverride.Value = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            return new ApiKeyScope(prior, priorCache);
        }

        public IDisposable UseHistoryReadCache()
        {
            var prior = HistoryReadCache.Value;
            HistoryReadCache.Value = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            return new HistoryReadCacheScope(prior);
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string url, HttpContent? content)
        {
            var request = new HttpRequestMessage(method, url)
            {
                Content = content
            };

            var apiKey = GetEffectiveApiKey();
            request.Headers.Remove("x-api-key");
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey);

            return request;
        }

        private Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string url,
            HttpContent? content,
            CancellationToken ct,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
        {
            if (method != HttpMethod.Get)
                HistoryReadCache.Value?.Clear();
            var request = BuildRequest(method, url, content);
            return _http.SendAsync(request, completionOption, ct);
        }

        private static StringContent JsonBody(object payload) => new(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

        public async Task ValidateApiKeyAsync(string apiKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("A Galaxy API key is required.", nameof(apiKey));

            using var scope = UseApiKey(apiKey);
            // Galaxy Europe's users/current route may return a gateway error even while
            // authenticated history operations are healthy. Validate against the exact,
            // read-only capability phyloDIVaS needs instead.
            var resp = await SendAsync(HttpMethod.Get, "/api/histories?limit=1", null, ct);
            await EnsureSuccessAsync(resp, "validate API key", ct);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new GalaxyHttpException("validate API key", 401, "Unauthorized", string.Empty);
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage resp, string op, CancellationToken ct)
        {
            if (resp.IsSuccessStatusCode) return;
            string body = string.Empty;
            try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { }
            throw new GalaxyHttpException(op, (int)resp.StatusCode, resp.ReasonPhrase ?? "", body);
        }

        public async Task<string> CreateHistoryAsync(string? name = null, CancellationToken ct = default)
        {
            var resp = await SendAsync(HttpMethod.Post, "/api/histories", JsonBody(new { name = name ?? $"dnds-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}" }), ct);
            await EnsureSuccessAsync(resp, "create history", ct);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var idProp))
                    throw new InvalidOperationException($"Unexpected response creating history. Body: {Trim(text)}");
                return idProp.GetString()!;
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"Non-JSON response creating history. Body: {Trim(text)}");
            }
        }

        public async Task DeleteHistoryAsync(string historyId, bool purge = false, CancellationToken ct = default)
        {
            var url = $"/api/histories/{Uri.EscapeDataString(historyId)}" + (purge ? "?purge=true" : string.Empty);
            var resp = await SendAsync(HttpMethod.Delete, url, null, ct);
            await EnsureSuccessAsync(resp, "delete history", ct);
        }

        // Uses Galaxy fetch API to import remote file by URL directly into history (no server-side buffering)
        public async Task<string> UploadFromUrlAsync(string historyId, string url, string? name = null, string? fileType = null, CancellationToken ct = default)
        {
            var ext = fileType ?? "fasta"; // Default to fasta for backwards compatibility
            var fileName = name ?? "sequence.fasta";
            
            var payload = new
            {
                history_id = historyId,
                targets = new[]
                {
                    new
                    {
                        destination = new { type = "hdas" },
                        elements = new[] { new { src = "url", url, name = fileName, ext = ext } }
                    }
                },
                tool_id = "upload1"
            };

            var resp = await SendAsync(HttpMethod.Post, "/api/tools/fetch", JsonBody(payload), ct);
            await EnsureSuccessAsync(resp, "upload from url", ct);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"Unexpected response for upload from url. Body: {Trim(text)}");
                if (!root.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Array || outputs.GetArrayLength() == 0)
                    throw new GalaxyHttpException("upload from url", 200, "NoOutputs", text);
                return outputs[0].GetProperty("id").GetString()!;
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"Non-JSON response for upload from url. Body: {Trim(text)}");
            }
        }

        // Upload text content directly as a new dataset
        public async Task<string> UploadTextContentAsync(string historyId, string content, string name, string fileType = "fasta", CancellationToken ct = default)
        {
            var payload = new
            {
                history_id = historyId,
                targets = new[]
                {
                    new
                    {
                        destination = new { type = "hdas" },
                        elements = new[] { new { src = "pasted", paste_content = content, name, ext = fileType, to_posix_lines = true, space_to_tab = false } }
                    }
                },
                tool_id = "upload1"
            };

            var resp = await SendAsync(HttpMethod.Post, "/api/tools/fetch", JsonBody(payload), ct);
            await EnsureSuccessAsync(resp, "upload text content", ct);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"Unexpected response for upload text content. Body: {Trim(text)}");
                if (!root.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Array || outputs.GetArrayLength() == 0)
                    throw new GalaxyHttpException("upload text content", 200, "NoOutputs", text);
                return outputs[0].GetProperty("id").GetString()!;
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"Non-JSON response for upload text content. Body: {Trim(text)}");
            }
        }

        // Upload binary content (e.g., zip) as a new dataset
        public async Task<string> UploadBinaryContentAsync(string historyId, byte[] content, string name, string fileType = "auto", CancellationToken ct = default)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(historyId), "history_id");
            form.Add(new StringContent("upload1"), "tool_id");
            form.Add(new StringContent(fileType), "file_type");
            form.Add(new StringContent("True"), "dbkey");
            form.Add(new ByteArrayContent(content), "files_0|file_data", name);

            var resp = await SendAsync(HttpMethod.Post, "/api/tools", form, ct);
            await EnsureSuccessAsync(resp, "upload binary content", ct);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"Unexpected response for upload binary content. Body: {Trim(text)}");
                if (!root.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Array || outputs.GetArrayLength() == 0)
                    throw new GalaxyHttpException("upload binary content", 200, "NoOutputs", text);
                return outputs[0].GetProperty("id").GetString()!;
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"Non-JSON response for upload binary content. Body: {Trim(text)}");
            }
        }

        public async Task<(string jobId, JsonElement root)> InvokeToolAsync(string historyId, string toolId, object inputs, CancellationToken ct = default)
        {
            var payload = new { tool_id = toolId, history_id = historyId, inputs };
            var resp = await SendAsync(HttpMethod.Post, "/api/tools", JsonBody(payload), ct);
            await EnsureSuccessAsync(resp, "invoke tool", ct);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            JsonElement root;
            try
            {
                root = JsonSerializer.Deserialize<JsonElement>(text, JsonOpts);
            }
            catch (JsonException)
            {
                // Return raw text wrapped as a JSON string element
                var quoted = JsonSerializer.Serialize(text, JsonOpts);
                root = JsonSerializer.Deserialize<JsonElement>(quoted, JsonOpts);
                return (string.Empty, root);
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                // Some Galaxy instances/tools can return non-object payloads; return raw without a job id
                return (string.Empty, root);
            }

            string jobId = string.Empty;
            if (root.TryGetProperty("jobs", out var jobs) && jobs.ValueKind == JsonValueKind.Array && jobs.GetArrayLength() > 0)
            {
                var first = jobs[0];
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    jobId = idProp.GetString() ?? string.Empty;
            }
            else if (root.TryGetProperty("job_id", out var jidProp) && jidProp.ValueKind == JsonValueKind.String)
            {
                jobId = jidProp.GetString() ?? string.Empty;
            }

            return (jobId, root);
        }

        public async Task<JsonElement> GetJobAsync(string jobId, CancellationToken ct = default)
        {
            var resp = await SendAsync(HttpMethod.Get, $"/api/jobs/{Uri.EscapeDataString(jobId)}", null, ct);
            await EnsureSuccessAsync(resp, "get job", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            return (await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct));
        }

        public async Task<JsonElement> GetFullJobAsync(string jobId, CancellationToken ct = default)
        {
            var resp = await SendAsync(
                HttpMethod.Get,
                $"/api/jobs/{Uri.EscapeDataString(jobId)}?full=true",
                null,
                ct);
            await EnsureSuccessAsync(resp, "get full job details", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct);
        }

        public async Task<JsonElement> GetJobConsoleOutputAsync(string jobId, CancellationToken ct = default)
        {
            const int maxCharacters = 100_000;
            var url = $"/api/jobs/{Uri.EscapeDataString(jobId)}/console_output" +
                $"?stdout_position=0&stdout_length={maxCharacters}" +
                $"&stderr_position=0&stderr_length={maxCharacters}";
            var resp = await SendAsync(HttpMethod.Get, url, null, ct);
            await EnsureSuccessAsync(resp, "get job console output", ct);
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOpts, ct);
        }

        public async Task CancelJobAsync(string jobId, CancellationToken ct = default)
        {
            var resp = await SendAsync(HttpMethod.Delete, $"/api/jobs/{Uri.EscapeDataString(jobId)}", null, ct);
            await EnsureSuccessAsync(resp, "cancel job", ct);
        }

        public async Task<JsonElement> ListHistoryContentsAsync(
            string historyId,
            CancellationToken ct = default,
            bool includeHidden = false,
            bool includeDeleted = false,
            int? limit = null,
            string? order = null)
        {
            _ = GetEffectiveApiKey(); // Require credentials before consulting cached data.
            var query = new List<string>();
            if (includeHidden) query.Add("include_hidden=true");
            if (includeDeleted) query.Add("include_deleted=true");
            if (limit is > 0) query.Add($"limit={limit.Value}");
            if (!string.IsNullOrWhiteSpace(order)) query.Add($"order={Uri.EscapeDataString(order)}");
            var suffix = query.Count > 0 ? "?" + string.Join("&", query) : string.Empty;
            var url = $"/api/histories/{Uri.EscapeDataString(historyId)}/contents{suffix}";

            if (HistoryReadCache.Value?.TryGetValue(url, out var cached) == true)
                return cached.Clone();

            var resp = await SendAsync(HttpMethod.Get, url, null, ct);
            await EnsureSuccessAsync(resp, "list history contents", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct);
            if (HistoryReadCache.Value is not null)
                HistoryReadCache.Value[url] = result.Clone();
            return result;
        }

        public async Task PrimeHistoryContentsCacheAsync(string historyId, CancellationToken ct = default)
        {
            if (HistoryReadCache.Value is null)
                throw new InvalidOperationException("A history read cache scope is required.");

            var escapedHistoryId = Uri.EscapeDataString(historyId);
            const string keys = "id,name,state,hid,history_content_type,extension,deleted,visible,elements_datatypes,element_identifier,elements_states,element_count,populated_state";
            var baseUrl = $"/api/histories/{escapedHistoryId}/contents?v=dev&include_hidden=true&keys={keys}";

            // Galaxy's unfiltered history-content endpoint becomes very slow for
            // large workflow histories. The v=dev flag is required for q/qv name
            // filters; without it Galaxy silently enumerates the complete history.
            var queries = new[]
            {
                $"{baseUrl}&q=history_content_type-eq&qv=dataset_collection&limit=30&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("phyloDIVaS")}&limit=100&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("Orthogroup")}&limit=100&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("OrthoFinder")}&limit=50&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("MAFFT")}&limit=50&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("Back")}&limit=50&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("tranalign")}&limit=50&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("IQ-TREE")}&limit=50&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("HyPhy")}&limit=100&order=hid-dsc",
                // Initial genome preparation is intentionally performed before
                // OrthoFinder is submitted. Include those datasets in the compact
                // snapshot so the UI does not report an idle history while NCBI
                // archives are being fetched, unzipped, and isoform-filtered.
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("proteome")}&limit=100&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("_cds")}&limit=100&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("Unzip on")}&limit=100&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("longest protein isoform")}&limit=100&order=hid-dsc",
                $"{baseUrl}&q=name-contains&qv={Uri.EscapeDataString("matching CDS isoforms")}&limit=100&order=hid-dsc",
                $"{baseUrl}&q=name-eq&qv={Uri.EscapeDataString("species_mapping.tabular")}&limit=10&order=hid-dsc",
                $"{baseUrl}&q=name-eq&qv={Uri.EscapeDataString("orthogroup_workflow_invoked.txt")}&limit=10&order=hid-dsc"
            };

            using var snapshotConcurrency = new SemaphoreSlim(4);
            var queryTasks = queries.Select(async query =>
            {
                await snapshotConcurrency.WaitAsync(ct);
                try
                {
                    using var queryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    queryCts.CancelAfter(TimeSpan.FromSeconds(30));
                    return await GetHistoryContentsPageAsync(query, queryCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    if (query == queries[0]) throw new TimeoutException("Galaxy collection inventory timed out. No pipeline stage can be inferred from an incomplete inventory; retry the connection.");
                    return default;
                }
                catch (GalaxyHttpException)
                {
                    if (query == queries[0]) throw;
                    return default;
                }
                finally { snapshotConcurrency.Release(); }
            });
            var snapshotParts = (await Task.WhenAll(queryTasks))
                .Where(part => part.ValueKind == JsonValueKind.Array)
                .ToList();

            if (snapshotParts.Count == 0)
                throw new TimeoutException("Galaxy did not return the targeted history snapshot within 30 seconds.");

            var merged = snapshotParts
                .Where(part => part.ValueKind == JsonValueKind.Array)
                .SelectMany(part => part.EnumerateArray().Select(item => item.Clone()))
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .GroupBy(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Select(group => group.First())
                .OrderByDescending(item => item.TryGetProperty("hid", out var hid) && hid.TryGetInt32(out var value) ? value : 0)
                .ToList();

            var snapshot = JsonSerializer.SerializeToElement(merged, JsonOpts);
            var historyContentsUrl = $"/api/histories/{escapedHistoryId}/contents";
            // Older recovery helpers use both URL forms. Seed both so a large
            // collection-heavy history never falls back to full enumeration.
            HistoryReadCache.Value[historyContentsUrl] = snapshot;
            HistoryReadCache.Value[$"{historyContentsUrl}?include_hidden=true"] = snapshot;
        }

        private async Task<JsonElement> GetHistoryContentsPageAsync(string url, CancellationToken ct)
        {
            var resp = await SendAsync(HttpMethod.Get, url, null, ct);
            await EnsureSuccessAsync(resp, "list history contents page", ct);
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOpts, ct);
        }

        public async Task<JsonElement> GetDatasetDetailsAsync(string datasetId, CancellationToken ct = default)
        {
            var resp = await SendAsync(HttpMethod.Get, $"/api/datasets/{Uri.EscapeDataString(datasetId)}", null, ct);
            await EnsureSuccessAsync(resp, "get dataset", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            return (await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct));
        }

        // Get the tool form/build to discover input parameter ids/types
        public async Task<JsonElement> GetToolBuildAsync(string toolId, string? historyId = null, CancellationToken ct = default)
        {
            var url = $"/api/tools/{Uri.EscapeDataString(toolId)}/build" + (string.IsNullOrWhiteSpace(historyId) ? string.Empty : $"?history_id={Uri.EscapeDataString(historyId)}");
            var resp = await SendAsync(HttpMethod.Get, url, null, ct);
            await EnsureSuccessAsync(resp, "get tool build", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            return (await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct));
        }

        public async Task<IReadOnlyList<(string Id, string Name)>> SearchToolsAsync(string q, CancellationToken ct = default)
        {
            var resp = await SendAsync(HttpMethod.Get, $"/api/tools?q={Uri.EscapeDataString(q)}", null, ct);
            await EnsureSuccessAsync(resp, "search tools", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            var doc = await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct);
            var list = new List<(string, string)>();

            if (doc.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in doc.EnumerateArray())
                {
                    // Current Galaxy releases return a plain array of tool IDs for
                    // /api/tools?q=..., while older variants return tool objects.
                    if (e.ValueKind == JsonValueKind.String)
                    {
                        var toolId = e.GetString();
                        if (!string.IsNullOrWhiteSpace(toolId)) list.Add((toolId!, toolId!));
                        continue;
                    }
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var id = e.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
                    var name = e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : id;
                    if (!string.IsNullOrEmpty(id)) list.Add((id!, name!));
                }
                return list;
            }

            if (doc.ValueKind == JsonValueKind.Object)
            {
                // Fallbacks for some Galaxy variants
                if (doc.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in hits.EnumerateArray())
                    {
                        if (e.ValueKind != JsonValueKind.Object) continue;
                        var id = e.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
                        var name = e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : id;
                        if (!string.IsNullOrEmpty(id)) list.Add((id!, name!));
                    }
                    return list;
                }
                if (doc.TryGetProperty("id", out var iid) && iid.ValueKind == JsonValueKind.String)
                {
                    var id = iid.GetString()!;
                    var name = doc.TryGetProperty("name", out var nn) && nn.ValueKind == JsonValueKind.String ? nn.GetString()! : id;
                    list.Add((id, name));
                }
            }

            return list;
        }

        public async Task<string> CreateListCollectionAsync(string historyId, string name, List<(string name, string datasetId)> elements, CancellationToken ct = default)
        {
            // https://docs.galaxyproject.org/en/latest/api/2/#operation.CreateDatasetCollection
            var items = elements.Select(e => new { name = e.name, src = "hda", id = e.datasetId }).ToArray();
            var payload = new
            {
                collection_type = "list",
                element_identifiers = items,
                name = name
            };
            var resp = await SendAsync(HttpMethod.Post, $"/api/histories/{Uri.EscapeDataString(historyId)}/contents/dataset_collections", JsonBody(payload), ct);
            await EnsureSuccessAsync(resp, "create list collection", ct);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var idProp))
                {
                    return idProp.GetString()!;
                }
                // Fallback: scan history for latest dataset collection with matching name
                var hist = await ListHistoryContentsAsync(historyId, ct);
                if (hist.ValueKind == JsonValueKind.Array)
                {
                    var latest = hist.EnumerateArray()
                        .Where(e => e.TryGetProperty("history_content_type", out var hct) && hct.GetString() == "dataset_collection")
                        .OrderByDescending(e => e.TryGetProperty("hid", out var hid) ? hid.GetInt32() : -1)
                        .FirstOrDefault();
                    if (latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty("id", out var id2))
                        return id2.GetString()!;
                }
                throw new InvalidOperationException($"Unexpected response creating collection. Body: {Trim(text)}");
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"Non-JSON response creating collection. Body: {Trim(text)}");
            }
        }

        public async Task<string> CreateDatasetCollectionAsync(string historyId, string name, Dictionary<string, string> elements, CancellationToken ct = default)
        {
            var elementList = elements.Select(kv => (kv.Key, kv.Value)).ToList();
            return await CreateListCollectionAsync(historyId, name, elementList, ct);
        }

        public async Task<JsonElement> GetJobOutputsAsync(string jobId, CancellationToken ct = default)
        {
            var resp = await SendAsync(HttpMethod.Get, $"/api/jobs/{Uri.EscapeDataString(jobId)}/outputs", null, ct);
            await EnsureSuccessAsync(resp, "get job outputs", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            return (await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct));
        }

        public async Task<JsonElement> GetJobInputsAsync(string jobId, CancellationToken ct = default)
        {
            var resp = await SendAsync(HttpMethod.Get, $"/api/jobs/{Uri.EscapeDataString(jobId)}/inputs", null, ct);
            await EnsureSuccessAsync(resp, "get job inputs", ct);
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOpts, ct);
        }

        public async Task<JsonElement> GetJobParametersDisplayAsync(string jobId, CancellationToken ct = default)
        {
            var resp = await SendAsync(
                HttpMethod.Get,
                $"/api/jobs/{Uri.EscapeDataString(jobId)}/parameters_display",
                null,
                ct);
            await EnsureSuccessAsync(resp, "get job parameters", ct);
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOpts, ct);
        }

        public async Task<(string invocationId, JsonElement raw)> InvokeWorkflowAsync(
            string workflowId,
            string historyId,
            Dictionary<string, object> inputs,
            CancellationToken ct = default)
        {
            var payload = new
            {
                history_id = historyId,
                inputs = inputs
            };

            var resp = await SendAsync(HttpMethod.Post, $"/api/workflows/{Uri.EscapeDataString(workflowId)}/invocations", JsonBody(payload), ct);
            await EnsureSuccessAsync(resp, "invoke workflow", ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            JsonElement root;
            try
            {
                root = JsonSerializer.Deserialize<JsonElement>(text, JsonOpts);
            }
            catch (JsonException)
            {
                var quoted = JsonSerializer.Serialize(text, JsonOpts);
                root = JsonSerializer.Deserialize<JsonElement>(quoted, JsonOpts);
                return (string.Empty, root);
            }

            string invocationId = string.Empty;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    invocationId = idProp.GetString() ?? string.Empty;
                else if (root.TryGetProperty("invocation_id", out var invProp) && invProp.ValueKind == JsonValueKind.String)
                    invocationId = invProp.GetString() ?? string.Empty;
            }

            return (invocationId, root);
        }

        public async Task<string> EnsureWorkflowImportedAsync(
            string workflowName,
            string workflowJson,
            CancellationToken ct = default)
        {
            var listResp = await SendAsync(
                HttpMethod.Get,
                "/api/workflows?show_published=false&show_shared=false&show_deleted=false&limit=1000",
                null,
                ct);
            await EnsureSuccessAsync(listResp, "list workflows", ct);
            var listText = await listResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using (var listDoc = JsonDocument.Parse(listText))
            {
                if (listDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var workflow in listDoc.RootElement.EnumerateArray())
                    {
                        var name = workflow.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                        var id = workflow.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        if (string.Equals(name, workflowName, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(id))
                            return id!;
                    }
                }
            }

            var definition = JsonSerializer.Deserialize<JsonElement>(workflowJson, JsonOpts);
            var importResp = await SendAsync(
                HttpMethod.Post,
                "/api/workflows",
                JsonBody(new { workflow = definition }),
                ct);
            await EnsureSuccessAsync(importResp, "import workflow", ct);
            var importText = await importResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var importDoc = JsonDocument.Parse(importText);
            if (importDoc.RootElement.ValueKind == JsonValueKind.Object &&
                importDoc.RootElement.TryGetProperty("id", out var importedId) &&
                !string.IsNullOrWhiteSpace(importedId.GetString()))
            {
                return importedId.GetString()!;
            }

            throw new InvalidOperationException($"Galaxy imported workflow '{workflowName}' but did not return an id. Body: {Trim(importText)}");
        }

        public async Task<string> DownloadDatasetAsync(string historyId, string datasetId, CancellationToken ct = default)
        {
            var resp = await SendAsync(
                HttpMethod.Get,
                $"/api/datasets/{Uri.EscapeDataString(datasetId)}/display",
                null,
                ct,
                HttpCompletionOption.ResponseHeadersRead);
            await EnsureSuccessAsync(resp, "download dataset", ct);
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        public async Task<(byte[] Content, string ContentType)> DownloadDatasetBytesAsync(string datasetId, CancellationToken ct = default)
        {
            var resp = await SendAsync(
                HttpMethod.Get,
                $"/api/datasets/{Uri.EscapeDataString(datasetId)}/display",
                null,
                ct,
                HttpCompletionOption.ResponseHeadersRead);
            await EnsureSuccessAsync(resp, "download dataset bytes", ct);
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return (bytes, contentType);
        }

        public async Task SetHistoryContentVisibilityAsync(string historyId, string contentId, bool visible, CancellationToken ct = default)
        {
            var payload = new { visible };
            var resp = await SendAsync(HttpMethod.Put, $"/api/histories/{Uri.EscapeDataString(historyId)}/contents/{Uri.EscapeDataString(contentId)}", JsonBody(payload), ct);
            await EnsureSuccessAsync(resp, "set history content visibility", ct);
        }

        public async Task<JsonElement> GetCollectionDetailsAsync(string historyId, string collectionId, CancellationToken ct = default)
        {
            var path = string.IsNullOrWhiteSpace(historyId)
                ? $"/api/dataset_collections/{Uri.EscapeDataString(collectionId)}"
                : $"/api/histories/{Uri.EscapeDataString(historyId)}/contents/dataset_collections/{Uri.EscapeDataString(collectionId)}";
            var resp = await SendAsync(HttpMethod.Get, path, null, ct);
            await EnsureSuccessAsync(resp, "get collection details", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            return (await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct));
        }

        public async Task<JsonElement> GetInvocationAsync(string invocationId, CancellationToken ct = default)
        {
            var resp = await SendAsync(HttpMethod.Get, $"/api/invocations/{Uri.EscapeDataString(invocationId)}", null, ct);
            await EnsureSuccessAsync(resp, "get invocation", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            return (await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct));
        }

        public async Task<JsonElement> GetCollectionContentsAsync(string contentsUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(contentsUrl))
                throw new ArgumentException("contentsUrl is required", nameof(contentsUrl));

            var resp = await SendAsync(HttpMethod.Get, contentsUrl, null, ct);
            await EnsureSuccessAsync(resp, "get collection contents", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            return (await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct));
        }

        public async Task<JsonElement> GetCollectionElementsAsync(string contentsUrl, int count, CancellationToken ct = default)
        {
            if (count <= 0) return await GetCollectionContentsAsync(contentsUrl, ct);
            const int pageSize = 2000;
            using var concurrency = new SemaphoreSlim(3);
            var pages = await Task.WhenAll(Enumerable.Range(0, (count + pageSize - 1) / pageSize).Select(async page =>
            {
                await concurrency.WaitAsync(ct);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    var separator = contentsUrl.Contains('?') ? "&" : "?";
                    var result = await GetCollectionContentsAsync($"{contentsUrl}{separator}limit={pageSize}&offset={page * pageSize}", timeout.Token);
                    if (result.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Galaxy collection page is not an array.");
                    return result.EnumerateArray().Select(item => item.Clone()).ToArray();
                }
                finally { concurrency.Release(); }
            }));
            var elements = pages.SelectMany(page => page).ToArray();
            if (elements.Length != count) throw new InvalidOperationException($"Galaxy returned {elements.Length} collection elements; expected {count}. Retry after the collection finishes populating.");
            return JsonSerializer.SerializeToElement(elements, JsonOpts);
        }

        public async Task<JsonElement> GetHistoryDetailsAsync(string historyId, CancellationToken ct = default)
        {
            var resp = await SendAsync(HttpMethod.Get, $"/api/histories/{Uri.EscapeDataString(historyId)}", null, ct);
            await EnsureSuccessAsync(resp, "get history details", ct);
            using var s = await resp.Content.ReadAsStreamAsync(ct);
            return (await JsonSerializer.DeserializeAsync<JsonElement>(s, JsonOpts, ct));
        }

        public async Task DeleteHistoryDatasetAsync(string historyId, string datasetId, CancellationToken ct = default)
        {
            var resp = await SendAsync(HttpMethod.Delete, $"/api/histories/{Uri.EscapeDataString(historyId)}/contents/{Uri.EscapeDataString(datasetId)}?purge=true", null, ct);
            await EnsureSuccessAsync(resp, "delete history dataset", ct);
        }

        public async Task UpdateCollectionDatatypeAsync(string historyId, string collectionId, string datatype, CancellationToken ct = default)
        {
            var payload = new { datatype };
            var content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), System.Text.Encoding.UTF8, "application/json");
            var resp = await SendAsync(HttpMethod.Put, $"/api/dataset_collections/{Uri.EscapeDataString(collectionId)}", content, ct);
            await EnsureSuccessAsync(resp, "update collection datatype", ct);
        }

        public async Task RenameHistoryContentAsync(string historyId, string contentId, string newName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            var payload = new { name = newName };
            var content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), System.Text.Encoding.UTF8, "application/json");
            var resp = await SendAsync(HttpMethod.Put, $"/api/histories/{Uri.EscapeDataString(historyId)}/contents/{Uri.EscapeDataString(contentId)}", content, ct);
            await EnsureSuccessAsync(resp, "rename history content", ct);
        }

        public async Task UpdateDatasetDatatypeAsync(string datasetId, string datatype, CancellationToken ct = default)
        {
            var payload = new { datatype };
            var content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), System.Text.Encoding.UTF8, "application/json");
            var resp = await SendAsync(HttpMethod.Put, $"/api/datasets/{Uri.EscapeDataString(datasetId)}", content, ct);
            await EnsureSuccessAsync(resp, "update dataset datatype", ct);
        }

        private static string Trim(string s) => string.IsNullOrEmpty(s) ? s : (s.Length > 800 ? s.Substring(0, 800) + "…" : s);
    }

    public sealed class GalaxyHttpException : Exception
    {
        public int StatusCode { get; }
        public string Reason { get; }
        public string Body { get; }
        public string Operation { get; }
        public GalaxyHttpException(string operation, int statusCode, string reason, string body)
            : base($"Galaxy {operation} failed: HTTP {statusCode} {reason}. { (body.TrimStart().StartsWith("<") ? "Galaxy returned an unreadable server page. Retry this request; this does not establish a job failure." : $"Body: {Trim(body)}") }")
        { StatusCode = statusCode; Reason = reason; Body = body; Operation = operation; }
        private static string Trim(string s) => string.IsNullOrEmpty(s) ? s : (s.Length > 800 ? s.Substring(0, 800) + "�" : s);
    }
}
