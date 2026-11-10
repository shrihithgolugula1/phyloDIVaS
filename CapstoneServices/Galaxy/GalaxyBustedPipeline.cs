using System.Text.Json;
using System.Linq;
using CapstoneServices.NCBI;
using Microsoft.Extensions.Logging;

namespace CapstoneServices.Galaxy
{
    public sealed class GalaxyBustedPipeline
    {
        private readonly GalaxyClient _galaxy;
        private readonly NcbiClient _ncbi;
        private readonly GalaxyOptions _opts;
        private readonly ILogger<GalaxyBustedPipeline> _log;
        public GalaxyBustedPipeline(GalaxyClient galaxy, NcbiClient ncbi, Microsoft.Extensions.Options.IOptions<GalaxyOptions> opts, ILogger<GalaxyBustedPipeline> log)
        { _galaxy = galaxy; _ncbi = ncbi; _opts = opts.Value; _log = log; }

        public sealed record StartResult(string HistoryId, string CodonAlignedDatasetId, string BustedJobId, JsonElement InvocationRaw, Dictionary<string,string> StepDatasetIds);

        public async Task<StartResult> StartAsync(string gene, IReadOnlyList<string> species, string? ncbiApiKey, CancellationToken ct, string alignment = "mafft", IReadOnlyDictionary<string, string>? selectedNuccoreIds = null)
        {
            // The CDS candidate-based gene workflow has been removed.
            throw new NotSupportedException("CDS candidate workflow has been removed.");
        }

        private async Task<string> MergeFastaAsync(string historyId, List<string> datasetIds, CancellationToken ct)
        {
            var mergeToolId = _opts.MergeFastaToolId
                ?? await ResolveToolIdAsync(new[] { "fasta", "merge" }, ct)
                ?? await ResolveToolIdAsync(new[] { "concatenate" }, ct);
            if (mergeToolId is null)
                throw new InvalidOperationException("Could not find a FASTA merge/concatenate tool on Galaxy");

            // De-dup in case upstream added duplicates
            datasetIds = datasetIds.Distinct().ToList();

            var build = await _galaxy.GetToolBuildAsync(mergeToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, mergeToolId);

            // Discover a suitable input path that accepts multiple datasets without using collections.
            var dataParam = FindFirstDataParamWithSelectors(inputsNode);
            var repeatParam = FindRepeatWithDataAndSelectors(inputsNode);

            object inputs;
            var dict = new Dictionary<string, object>();

            if (dataParam is not null && dataParam.Value.Multiple)
            {
                foreach (var kv in dataParam.Value.Selectors) dict[kv.Key] = kv.Value; // set conditional selectors like global_condition|input_type
                var list = datasetIds.Select(id => new { src = "hda", id }).ToArray();
                dict[dataParam.Value.Key] = list;
                inputs = dict;
            }
            else if (repeatParam is not null)
            {
                foreach (var kv in repeatParam.Value.Selectors) dict[kv.Key] = kv.Value; // set conditional selectors like global_condition|input_type
                var repItems = datasetIds.Select(id => new Dictionary<string, object> { [repeatParam.Value.ChildDataName] = new { src = "hda", id } }).ToArray();
                dict[repeatParam.Value.RepeatKey] = repItems;
                inputs = dict;
            }
            else
            {
                throw new InvalidOperationException("Configured merge tool does not accept multiple datasets (no multiple data param or repeat found). Configure 'MergeFastaToolId' to a tool like 'concatenate_multiple_datasets' (cat_multi_datasets).");
            }

            var (_, raw) = await _galaxy.InvokeToolAsync(historyId, mergeToolId, inputs, ct);
            return await ResolveOutputDatasetId(historyId, raw, ct);
        }

        private static (string Key, bool Multiple, Dictionary<string,string> Selectors)? FindFirstDataParamWithSelectors(JsonElement inputsNode)
        {
            foreach (var found in EnumerateDataParams(inputsNode, prefix: string.Empty, selectors: new()))
            {
                return found; // first match
            }
            return null;
        }

        private static (string RepeatKey, string ChildDataName, Dictionary<string,string> Selectors)? FindRepeatWithDataAndSelectors(JsonElement inputsNode)
        {
            foreach (var found in EnumerateRepeatParams(inputsNode, prefix: string.Empty, selectors: new()))
            {
                return found; // first match
            }
            return null;
        }

        private static IEnumerable<(string Key, bool Multiple, Dictionary<string,string> Selectors)> EnumerateDataParams(JsonElement node, string prefix, Dictionary<string,string> selectors)
        {
            if (node.ValueKind != JsonValueKind.Array) yield break;
            foreach (var p in node.EnumerateArray())
            {
                if (!p.TryGetProperty("type", out var t)) continue;
                var type = t.GetString();
                var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;

                if (type == "conditional")
                {
                    var condName = name ?? "condition";
                    string? testParamName = null;
                    if (p.TryGetProperty("test_param", out var tp) && tp.ValueKind == JsonValueKind.Object && tp.TryGetProperty("name", out var tpn))
                        testParamName = tpn.GetString();

                    if (p.TryGetProperty("cases", out var cases) && cases.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cs in cases.EnumerateArray())
                        {
                            var value = cs.TryGetProperty("value", out var v) ? v.GetString() : null;
                            var subSelectors = new Dictionary<string,string>(selectors);
                            if (!string.IsNullOrEmpty(value))
                            {
                                var selKey = string.IsNullOrEmpty(prefix)
                                    ? $"{condName}|{testParamName ?? "case"}"
                                    : $"{prefix}{condName}|{testParamName ?? "case"}";
                                subSelectors[selKey] = value!;
                            }
                            if (cs.TryGetProperty("inputs", out var ci) && ci.ValueKind == JsonValueKind.Array)
                            {
                                var newPrefix = string.IsNullOrEmpty(prefix) ? $"{condName}|" : $"{prefix}{condName}|";
                                foreach (var f in EnumerateDataParams(ci, newPrefix, subSelectors)) yield return f;
                                foreach (var _ in EnumerateRepeatParams(ci, newPrefix, subSelectors)) { /* handled by repeat enumerator */ }
                            }
                        }
                    }
                    continue;
                }

                if (type == "section")
                {
                    if (p.TryGetProperty("inputs", out var ci) && ci.ValueKind == JsonValueKind.Array)
                    {
                        var newPrefix = prefix; // sections do not affect key path in tool API
                        foreach (var f in EnumerateDataParams(ci, newPrefix, selectors)) yield return f;
                        foreach (var _ in EnumerateRepeatParams(ci, newPrefix, selectors)) { }
                    }
                    continue;
                }

                if (type == "data" && name is not null)
                {
                    bool multiple = p.TryGetProperty("multiple", out var m) && m.ValueKind == JsonValueKind.True;
                    var key = string.IsNullOrEmpty(prefix) ? name : prefix + name;
                    yield return (key, multiple, new Dictionary<string,string>(selectors));
                }
            }
        }

        private static IEnumerable<(string RepeatKey, string ChildDataName, Dictionary<string,string> Selectors)> EnumerateRepeatParams(JsonElement node, string prefix, Dictionary<string,string> selectors)
        {
            if (node.ValueKind != JsonValueKind.Array) yield break;
            foreach (var p in node.EnumerateArray())
            {
                if (!p.TryGetProperty("type", out var t)) continue;
                var type = t.GetString();
                var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;

                if (type == "conditional")
                {
                    var condName = name ?? "condition";
                    string? testParamName = null;
                    if (p.TryGetProperty("test_param", out var tp) && tp.ValueKind == JsonValueKind.Object && tp.TryGetProperty("name", out var tpn))
                        testParamName = tpn.GetString();

                    if (p.TryGetProperty("cases", out var cases) && cases.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cs in cases.EnumerateArray())
                        {
                            var value = cs.TryGetProperty("value", out var v) ? v.GetString() : null;
                            var subSelectors = new Dictionary<string,string>(selectors);
                            if (!string.IsNullOrEmpty(value))
                            {
                                var selKey = string.IsNullOrEmpty(prefix)
                                    ? $"{condName}|{testParamName ?? "case"}"
                                    : $"{prefix}{condName}|{testParamName ?? "case"}";
                                subSelectors[selKey] = value!;
                            }
                            if (cs.TryGetProperty("inputs", out var ci) && ci.ValueKind == JsonValueKind.Array)
                            {
                                var newPrefix = string.IsNullOrEmpty(prefix) ? $"{condName}|" : $"{prefix}{condName}|";
                                foreach (var r in EnumerateRepeatParams(ci, newPrefix, subSelectors)) yield return r;
                                foreach (var _ in EnumerateDataParams(ci, newPrefix, subSelectors)) { }
                            }
                        }
                    }
                    continue;
                }

                if (type == "repeat" && name is not null)
                {
                    string? childDataName = null;
                    if (p.TryGetProperty("inputs", out var inputs) && inputs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var child in inputs.EnumerateArray())
                        {
                            if (child.TryGetProperty("type", out var ct2) && ct2.GetString() == "data" && child.TryGetProperty("name", out var cn))
                            {
                                childDataName = cn.GetString();
                                break;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(childDataName))
                    {
                        var key = string.IsNullOrEmpty(prefix) ? name : prefix + name;
                        yield return (key!, childDataName!, new Dictionary<string,string>(selectors));
                    }
                }

                if (type == "section")
                {
                    if (p.TryGetProperty("inputs", out var ci) && ci.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in EnumerateRepeatParams(ci, prefix, selectors)) yield return r;
                    }
                }
            }
        }

        private async Task<string> TranslateAsync(string historyId, string fastaId, CancellationToken ct)
        {
            var translateToolId = _opts.TranslateToolId
                ?? await ResolveToolIdAsync(new[] { "emboss_transeq" }, ct)
                ?? await ResolveToolIdAsync(new[] { "transeq" }, ct)
                ?? await ResolveToolIdAsync(new[] { "translate", "protein" }, ct)
                ?? await ResolveToolIdAsync(new[] { "seqkit", "translate" }, ct);
            if (translateToolId is null) throw new InvalidOperationException("Translate tool not found (transeq)");
            var build = await _galaxy.GetToolBuildAsync(translateToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, translateToolId);
            var paramId = FindSingleDataParamRecursive(inputsNode) ?? "input1";
            var inputs = new Dictionary<string, object> { [paramId] = new { src = "hda", id = fastaId } };
            var (_, raw) = await _galaxy.InvokeToolAsync(historyId, translateToolId, inputs, ct);
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
            var (_, raw) = await _galaxy.InvokeToolAsync(historyId, mafftToolId, inputs, ct);
            return await ResolveOutputDatasetId(historyId, raw, ct);
        }

        private async Task<string> MuscleAsync(string historyId, string aaMultiId, CancellationToken ct)
        {
            var muscleToolId = _opts.MuscleToolId ?? await ResolveToolIdAsync(new[] { "muscle" }, ct);
            if (muscleToolId is null) throw new InvalidOperationException("MUSCLE tool not found");
            var build = await _galaxy.GetToolBuildAsync(muscleToolId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, muscleToolId);
            var paramId = FindSingleDataParamRecursive(inputsNode) ?? "input";
            var inputs = new Dictionary<string, object> { [paramId] = new { src = "hda", id = aaMultiId } };
            var (_, raw) = await _galaxy.InvokeToolAsync(historyId, muscleToolId, inputs, ct);
            return await ResolveOutputDatasetId(historyId, raw, ct);
        }

        private async Task<string> AlignBackTransAsync(string historyId, string aaAlignedId, string ntMultiId, CancellationToken ct)
        {
            var abtId = _opts.AlignBackTransToolId ?? await ResolveToolIdAsync(new[] { "align_back_trans" }, ct);
            if (abtId is null) throw new InvalidOperationException("align_back_trans tool not found");
            var build = await _galaxy.GetToolBuildAsync(abtId, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, abtId);
            // The tool expects AA alignment and corresponding nucleotide multi-fasta.
            var dataParams = FindDataParamsRecursive(inputsNode).Take(2).ToList();
            if (dataParams.Count < 2) throw new InvalidOperationException("align_back_trans expected two data params (AA alignment and nucleotide sequences)");
            var inputs = new Dictionary<string, object>
            {
                [dataParams[0]] = new { src = "hda", id = aaAlignedId },
                [dataParams[1]] = new { src = "hda", id = ntMultiId },
            };
            var (_, raw) = await _galaxy.InvokeToolAsync(historyId, abtId, inputs, ct);
            return await ResolveOutputDatasetId(historyId, raw, ct);
        }

        private async Task<(string jobId, JsonElement raw)> RunBustedAsync(string historyId, string codonAlignedId, CancellationToken ct)
        {
            var build = await _galaxy.GetToolBuildAsync(_opts.DnDsToolId!, historyId, ct);
            var inputsNode = GetInputsArrayOrThrow(build, _opts.DnDsToolId!);
            var param = FindDataParamsRecursive(inputsNode).FirstOrDefault() ?? "input_file";
            var inputs = new Dictionary<string, object> { [param] = new { src = "hda", id = codonAlignedId } };
            return await _galaxy.InvokeToolAsync(historyId, _opts.DnDsToolId!, inputs, ct);
        }

        private async Task<string?> ResolveToolIdAsync(string[] tokens, CancellationToken ct)
        {
            var results = await _galaxy.SearchToolsAsync(string.Join(' ', tokens), ct);
            var first = results.FirstOrDefault();
            return first == default ? null : first.Id;
        }

        // Recursive input discovery helpers
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
                    // conditionals store cases[] -> inputs[]
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

        private static string? FindFirstParamNameRecursive(JsonElement inputsNode, string typeFilter)
        {
            foreach (var p in FlattenInputs(inputsNode))
            {
                if (p.TryGetProperty("type", out var t) && t.GetString() == typeFilter && p.TryGetProperty("name", out var n))
                    return n.GetString();
            }
            return null;
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

            // Fallback: get latest dataset in history by hid
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
    }
}
