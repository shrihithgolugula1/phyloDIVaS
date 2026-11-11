namespace CapstoneServices.Galaxy;

public static class GalaxyCompletionEvidence
{
    // Record-level evidence only; the viewer separately checks collection coverage.
    public static bool AllMethodsComplete(IEnumerable<(string Name, string State)> records)
    {
        var items = records.ToList();
        return items.Count > 0 && items.All(item => item.State.Equals("ok", StringComparison.OrdinalIgnoreCase)) &&
            new[] { "busted", "absrel", "meme" }.All(method =>
                items.Any(item => item.Name.Contains(method, StringComparison.OrdinalIgnoreCase)));
    }
}
