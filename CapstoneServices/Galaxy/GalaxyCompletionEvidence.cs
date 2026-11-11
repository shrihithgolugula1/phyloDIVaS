namespace CapstoneServices.Galaxy;

public static class GalaxyCompletionEvidence
{
    public static string SelectionOutputKey(string name)
    {
        var method = name.Contains("absrel", StringComparison.OrdinalIgnoreCase) ? "absrel"
            : name.Contains("busted", StringComparison.OrdinalIgnoreCase) ? "busted"
            : name.Contains("meme", StringComparison.OrdinalIgnoreCase) ? "meme" : "other";
        return method + (name.Contains("markdown", StringComparison.OrdinalIgnoreCase) || name.Contains("report", StringComparison.OrdinalIgnoreCase) ? ":markdown" : ":json");
    }
    public static string RecordState(System.Text.Json.JsonElement record)
    {
        if (record.TryGetProperty("elements_states", out var states) && states.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var state in new[] { "running", "upload", "setting_metadata", "queued", "new", "waiting", "error", "failed", "discarded", "paused", "failed_metadata" })
                if (states.TryGetProperty(state, out var count) && count.TryGetInt32(out var n) && n > 0) return state;
            var counts = states.EnumerateObject().ToList();
            if (counts.Count > 0 && counts.All(entry => entry.Name == "ok" || (entry.Value.TryGetInt32(out var n) && n == 0)) &&
                states.TryGetProperty("ok", out var ok) && ok.TryGetInt32(out var total) && total > 0) return "ok";
            return "unknown";
        }
        return record.TryGetProperty("state", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "unknown" : "unknown";
    }
    // Record-level evidence only; the viewer separately checks collection coverage.
    public static bool AllMethodsSettled(IEnumerable<(string Name, string State)> records)
    {
        var items = records.ToList();
        var terminal = new[] { "ok", "error", "failed", "discarded", "paused", "failed_metadata", "skipped" };
        return items.Count > 0 && items.All(item => terminal.Contains(item.State, StringComparer.OrdinalIgnoreCase)) &&
            new[] { "busted", "absrel", "meme" }.All(method => items.Any(item => item.Name.Contains(method, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool AllMethodsComplete(IEnumerable<(string Name, string State)> records)
    {
        var items = records.ToList();
        return items.Count > 0 && items.All(item => item.State.Equals("ok", StringComparison.OrdinalIgnoreCase)) &&
            new[] { "busted", "absrel", "meme" }.All(method =>
                items.Any(item => item.Name.Contains(method, StringComparison.OrdinalIgnoreCase)));
    }
}
