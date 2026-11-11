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
            var elements = await client.GetCollectionElementsAsync("/fixture-elements", 2001);
            if (elements.GetArrayLength() != 2001 || elements[2000].GetProperty("element_index").GetInt32() != 2000)
                throw new Exception("Collection pagination lost boundary elements or ordering.");
            try
            {
                await client.GetCollectionElementsAsync("/fixture-elements", 2002);
                throw new Exception("Collection count mismatch was accepted.");
            }
            catch (InvalidOperationException) { }
            Console.WriteLine("PASS: collection pagination preserves boundary elements and rejects incomplete inventories");
            using (client.UseHistoryReadCache())
            {
                handler.FailCollectionInventory = true;
                try
                {
                    await client.PrimeHistoryContentsCacheAsync("incomplete-history");
                    throw new Exception("Missing essential collection inventory was silently accepted.");
                }
                catch (GalaxyHttpException) { }
                finally { handler.FailCollectionInventory = false; }
                Console.WriteLine("PASS: failed essential collection inventory cannot become a partial pipeline snapshot");
            }
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
        public bool FailCollectionInventory { get; set; }
        public int DatasetReads { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Keys.Add(request.Headers.GetValues("x-api-key").Single());
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (FailCollectionInventory && request.RequestUri.Query.Contains("history_content_type-eq"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("fixture unavailable") });
            if (request.RequestUri.AbsolutePath == "/fixture-elements")
            {
                var offset = int.Parse(System.Text.RegularExpressions.Regex.Match(request.RequestUri.Query, @"offset=(\d+)").Groups[1].Value);
                var rows = Enumerable.Range(offset, Math.Min(2000, 2001 - offset)).Select(i => new { element_index = i });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(rows)) });
            }
            var dataset = request.RequestUri!.AbsolutePath.Contains("/api/datasets/");
            if (dataset) DatasetReads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(dataset ? "{\"state\":\"ok\"}" : "[]") });
        }
    }
}
