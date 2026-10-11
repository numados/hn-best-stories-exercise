using System.Net;
using System.Text.Json;
using HackerNews.BestStories.Tests.Infrastructure;
using Xunit;

namespace HackerNews.BestStories.Tests.Integration;

/// <summary>Snapshot engine behavior: warm cache,
/// failure semantics, virtual-time refresh cycles, negative caching. Time is driven by
/// FakeTimeProvider; the fake upstream's call counters are the assertion surface.</summary>
public sealed class SnapshotBehaviorTests : IAsyncDisposable
{
    private readonly ApiFactory _factory = new();

    private async Task<JsonElement[]> GetStoriesAsync(HttpClient client, string query)
    {
        using HttpResponseMessage response = await client.GetAsync($"/api/beststories?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.EnumerateArray().ToArray();
    }

    [Fact]
    public async Task Repeat_and_parallel_requests_make_zero_upstream_calls()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        int idsBefore = _factory.HackerNews.IdListCalls;
        int itemsBefore = _factory.HackerNews.TotalItemCalls;

        JsonElement[] first = await GetStoriesAsync(client, "n=6");
        JsonElement[] second = await GetStoriesAsync(client, "n=6");
        JsonElement[] burst = (await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => GetStoriesAsync(client, "n=6"))))
            .SelectMany(x => x)
            .ToArray();

        Assert.Equal(second.Length, first.Length);
        Assert.Equal(20 * first.Length, burst.Length);
        Assert.Equal(idsBefore, _factory.HackerNews.IdListCalls);
        Assert.Equal(itemsBefore, _factory.HackerNews.TotalItemCalls);
    }

    [Fact]
    public async Task Confirmed_unavailable_items_are_excluded()
    {
        FakeHackerNewsHandler hn = _factory.HackerNews;
        hn.SetIds(45, 46, 47, 48, 44, 1);
        hn.SetItemJson(45, Fixtures.LoadText("item-deleted.json"));  // {"deleted":true}
        hn.SetItemJson(46, Fixtures.LoadText("item-dead.json"));     // {"dead":true}
        hn.SetItemError(47, HttpStatusCode.NotFound);                // 404
        hn.SetItemError(48, HttpStatusCode.Gone);                    // 410
        hn.SetItemNull(44);                                          // HN-style null body
        hn.SetItemJson(1, """
            {"id":1,"by":"user","time":1791400000,"type":"story","title":"fine","score":100,"descendants":0}
            """);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        JsonElement[] stories = await GetStoriesAsync(client, "n=10");

        Assert.Single(stories);
        Assert.Equal("fine", stories[0].GetProperty("title").GetString());
        Assert.Equal(1, hn.ItemCalls(45));
        Assert.Equal(1, hn.ItemCalls(46));
        Assert.Equal(1, hn.ItemCalls(47));
        Assert.Equal(1, hn.ItemCalls(48));
        Assert.Equal(1, hn.ItemCalls(44));
    }

    [Fact]
    public async Task Cold_start_with_hn_down_returns_503_problem_details()
    {
        _factory.HackerNews.SetIdsError(HttpStatusCode.BadRequest);
        using HttpClient client = _factory.CreateClient();

        Task<HttpResponseMessage> request = client.GetAsync("/api/beststories?n=5");
        // The endpoint awaits the initial build with a 2 s *virtual* timeout. Advancing in
        // small steps lets the request pipeline reach the wait before virtual time passes
        // the timeout; short wall-clock delays coordinate progress between advances.
        for (int i = 0; i < 100 && !request.IsCompleted; i++)
        {
            _factory.Time.Advance(TimeSpan.FromMilliseconds(200));
            await Task.Delay(5); // coordination yield, not a timing assertion
        }

        using HttpResponseMessage response = await request;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(503, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Hn_failure_after_snapshot_serves_stale_data_and_reports_age()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();
        JsonElement[] before = await GetStoriesAsync(client, "n=6");

        _factory.HackerNews.SetIdsError(HttpStatusCode.BadRequest);
        await _factory.AdvanceOneRefreshCycleAsync(
            () => _factory.HackerNews.IdListCalls >= 2);

        JsonElement[] after = await GetStoriesAsync(client, "n=6");
        Assert.Equal(
            before.Select(story => story.GetProperty("title").GetString()),
            after.Select(story => story.GetProperty("title").GetString()));

        _factory.Time.Advance(TimeSpan.FromSeconds(10));
        using HttpResponseMessage health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        double age = body.RootElement.GetProperty("snapshotAgeSeconds").GetDouble();
        Assert.True(age >= 10, $"snapshot age {age} should reflect the virtual clock");
    }

    [Fact]
    public async Task Warm_refresh_refetches_only_ids_and_new_items()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        await _factory.AdvanceOneRefreshCycleAsync(
            () => _factory.HackerNews.IdListCalls >= 2);

        Assert.Equal(2, _factory.HackerNews.IdListCalls);   // ids: fresh every cycle
        Assert.Equal(Fixtures.LiveStoryIds.Length, _factory.HackerNews.TotalItemCalls); // items: all warm, 0 new calls

        int[] ids = Fixtures.LiveStoryIds.ToArray();
        int[] extendedIds = [.. ids, 42];
        _factory.HackerNews.SetIds(extendedIds);
        _factory.HackerNews.SetItemJson(42, """
            {"id":42,"by":"user","time":1791499999,"type":"story","title":"fresh arrival","score":5000,"descendants":1}
            """);

        await _factory.AdvanceOneRefreshCycleAsync(
            () => _factory.HackerNews.ItemCalls(42) == 1);

        Assert.Equal(1, _factory.HackerNews.ItemCalls(42));
        Assert.Equal(Fixtures.LiveStoryIds.Length + 1, _factory.HackerNews.TotalItemCalls);

        JsonElement[] stories = await GetStoriesAsync(client, "n=1");
        Assert.Equal("fresh arrival", stories[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Transient_item_failures_are_not_negatively_cached()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        // New id whose item endpoint fails now but succeeds later: proves transient
        // failures are not negatively cached (the item is retried on the next cycle).
        int[] ids = Fixtures.LiveStoryIds.ToArray();
        _factory.HackerNews.SetIds([.. ids, 43]);
        _factory.HackerNews.SetItemError(43, HttpStatusCode.BadRequest);
        await _factory.AdvanceOneRefreshCycleAsync(
            () => _factory.HackerNews.ItemCalls(43) == 1);

        JsonElement[] withoutFailing = await GetStoriesAsync(client, "n=10");
        Assert.DoesNotContain(withoutFailing, story => story.GetProperty("uri").GetString()
            ?.EndsWith("id=43") == true);

        _factory.HackerNews.SetItemJson(43, """
            {"id":43,"by":"user","time":1791499999,"type":"story","title":"recovered","score":4000,"descendants":0}
            """);
        await _factory.AdvanceOneRefreshCycleAsync(
            () => _factory.HackerNews.ItemCalls(43) == 2);

        JsonElement[] withRecovered = await GetStoriesAsync(client, "n=10");
        Assert.Contains(withRecovered, story => story.GetProperty("uri").GetString()
            ?.EndsWith("id=43") == true);
    }

    [Fact]
    public async Task Negative_entries_prevent_refetch_within_window()
    {
        FakeHackerNewsHandler hn = _factory.HackerNews;
        hn.SetIds(44, 1);
        hn.SetItemNull(44); // confirmed unavailable → negative cache entry
        hn.SetItemJson(1, """
            {"id":1,"by":"user","time":1791400000,"type":"story","title":"fine","score":100,"descendants":0}
            """);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();
        Assert.Equal(1, hn.ItemCalls(44));

        await _factory.AdvanceOneRefreshCycleAsync(
            () => hn.IdListCalls >= 2);
        await _factory.AdvanceOneRefreshCycleAsync(
            () => hn.IdListCalls >= 3);

        // Within the negative TTL window (30 s of virtual time) the unavailable item is
        // not requested again; the healthy item stays warm too.
        Assert.Equal(1, hn.ItemCalls(44));
        Assert.Equal(1, hn.ItemCalls(1));
        Assert.Equal(3, hn.IdListCalls);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
