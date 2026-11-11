using System.Net;
using CapstoneServices.Galaxy;
using Microsoft.Extensions.Options;

internal static class GalaxyCredentialTests
{
    public static async Task RunAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var client = new GalaxyClient(http, Options.Create(new GalaxyOptions
        {
            BaseUrl = "https://galaxy.invalid", ApiKey = "dummy-config-must-not-be-used"
        }));
        async Task MissingScope()
        {
            var count = handler.Keys.Count;
            try { await client.ListHistoryContentsAsync("same-history"); }
            catch (InvalidOperationException) when (handler.Keys.Count == count) { return; }
            throw new Exception("Missing request credential was not rejected before transport/cache.");
        }
        await MissingScope();
        using (client.UseApiKey("dummy-route"))
        {
            await client.GetCollectionDetailsAsync("history", "collection");
            if (handler.Paths.Last() != "/api/histories/history/contents/dataset_collections/collection")
                throw new Exception("Collection lookup must use its history-scoped route.");
            await client.GetCollectionDetailsAsync(null!, "collection");
            if (handler.Paths.Last() != "/api/dataset_collections/collection")
                throw new Exception("Collection lookup without a history must preserve the unscoped route.");
            Console.WriteLine("PASS: history-scoped and standalone collection routes");
        }
        using (client.UseApiKey("dummy-poll"))
        using (client.UseHistoryReadCache())
        {
            await client.ListHistoryContentsAsync("stale-history");
            var pipeline = new GalaxyOrthoFinderPipeline(client, null!, Options.Create(new GalaxyOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<GalaxyOrthoFinderPipeline>.Instance);
            var method = typeof(GalaxyOrthoFinderPipeline).GetMethod("WaitForDatasetReadyAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new[] { typeof(string), typeof(string), typeof(CancellationToken) }, null)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await (Task)method.Invoke(pipeline, new object[] { "stale-history", "fresh-dataset", timeout.Token })!;
            if (handler.DatasetReads == 0) throw new Exception("Readiness polling reused the stale history snapshot.");
            Console.WriteLine("PASS: readiness polling bypasses cached history snapshots");
        }
        handler.Keys.Clear();
        using (client.UseApiKey("dummy-a"))
        using (client.UseHistoryReadCache())
        {
            await client.ListHistoryContentsAsync("same-history");
            await client.ListHistoryContentsAsync("same-history");
            if (handler.Keys.Count != 1) throw new Exception("Same-scope cache missed.");
            using (client.UseApiKey("dummy-b"))
                await client.ListHistoryContentsAsync("same-history");
            using (client.UseApiKey(" ")) await MissingScope();
            await client.ListHistoryContentsAsync("same-history");
            if (!handler.Keys.SequenceEqual(new[] { "dummy-a", "dummy-b" }))
                throw new Exception("Credential/cache scope was not isolated/restored.");
        }
        await MissingScope();
        Console.WriteLine("PASS: Galaxy request credentials and nested account-cache isolation (fixture transport)");
    }
    private sealed class FixtureHandler : HttpMessageHandler
    {
        public List<string> Keys { get; } = new();
        public List<string> Paths { get; } = new();
        public int DatasetReads { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Keys.Add(request.Headers.GetValues("x-api-key").Single());
            Paths.Add(request.RequestUri!.AbsolutePath);
            var dataset = request.RequestUri!.AbsolutePath.Contains("/api/datasets/");
            if (dataset) DatasetReads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(dataset ? "{\"state\":\"ok\"}" : "[]") });
        }
    }
}
