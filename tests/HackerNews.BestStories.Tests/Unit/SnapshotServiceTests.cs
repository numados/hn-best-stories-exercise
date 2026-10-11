using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using HackerNews.BestStories.Api.Configuration;
using HackerNews.BestStories.Api.Services;
using HackerNews.BestStories.Api.Services.Caching;
using HackerNews.BestStories.Client;
using HackerNews.BestStories.Client.Models;
using HackerNews.BestStories.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HackerNews.BestStories.Tests.Unit;

/// <summary>
/// Deterministic tests for the snapshot refresher's failure semantics, driven by a
/// per-id scriptable cache (simulating expiry by answering Miss) and virtual time.
/// The HTTP-level behavior is covered by the integration suite; here we prove the
/// retain-previous-value rule that a warm cache in the hosted app cannot reach.
/// </summary>
public sealed class SnapshotServiceTests : IDisposable
{
    private readonly FakeHackerNewsHandler _hn = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private readonly ScriptedStoryCache _cache = new();
    private readonly SnapshotStore _store;
    private readonly HttpClient _httpClient;

    public SnapshotServiceTests()
    {
        _store = new SnapshotStore(_time);
        _httpClient = new HttpClient(_hn) { BaseAddress = HackerNewsClient.DefaultBaseAddress };
    }

    public void Dispose() => _httpClient.Dispose();

    private static string ItemJson(int id, string title, int score) => $$"""
        {"id":{{id}},"by":"user","time":1791400000,"type":"story","title":"{{title}}","score":{{score}},"descendants":0}
        """;

    private async Task<BestStoriesSnapshotService> StartServiceAsync()
    {
        BestStoriesSnapshotService service = new(
            new HackerNewsClient(_httpClient),
            _cache,
            _store,
            _time,
            Options.Create(new SnapshotOptions
            {
                RefreshInterval = TimeSpan.FromSeconds(1),
                InitialFetchAwaitTimeout = TimeSpan.FromSeconds(2),
                FetchParallelism = 4,
            }),
            NullLogger<BestStoriesSnapshotService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await _store.InitialBuild.WaitAsync(TimeSpan.FromSeconds(10));
        return service;
    }

    private async Task RunCyclesAsync(BestStoriesSnapshotService service, Func<bool> cycleCompleted)
    {
        _time.Advance(TimeSpan.FromMilliseconds(1_100));
        await TestWait.UntilAsync(cycleCompleted, TimeSpan.FromSeconds(30));
    }

    private string[] CurrentTitles() =>
        _store.Current!.Stories.Select(story => story.Title).ToArray();

    [Fact]
    public async Task Failed_item_fetch_retains_previous_value()
    {
        _hn.SetIds(1, 2);
        _hn.SetItemJson(1, ItemJson(1, "one", 100));
        _hn.SetItemJson(2, ItemJson(2, "two", 200));
        using BestStoriesSnapshotService service = await StartServiceAsync();
        Assert.Equal(["two", "one"], CurrentTitles());

        // Simulate cache expiry for item 1 (cache answers Miss) and an upstream failure.
        _cache.Program(1, StoryCacheEntry.Miss);
        _hn.SetItemError(1, HttpStatusCode.BadRequest);
        await RunCyclesAsync(service, () => _hn.ItemCalls(1) == 2);

        // Item 1 keeps its previous value instead of disappearing from the snapshot.
        Assert.Equal(["two", "one"], CurrentTitles());
    }

    [Fact]
    public async Task New_item_failing_first_time_is_excluded()
    {
        _hn.SetIds(1);
        _hn.SetItemJson(1, ItemJson(1, "one", 100));
        using BestStoriesSnapshotService service = await StartServiceAsync();

        _hn.SetIds(1, 3);
        _hn.SetItemError(3, HttpStatusCode.BadRequest);
        await RunCyclesAsync(service, () => _hn.ItemCalls(3) == 1);

        Assert.Equal(["one"], CurrentTitles());
    }

    [Fact]
    public async Task Null_items_are_negatively_cached()
    {
        _hn.SetIds(1, 5);
        _hn.SetItemJson(1, ItemJson(1, "one", 100));
        _hn.SetItemNull(5); // HN null body
        using BestStoriesSnapshotService service = await StartServiceAsync();

        Assert.Equal(["one"], CurrentTitles());
        Assert.Contains(5, _cache.UnavailableIds);

        // While the negative entry stands, the item is not fetched again.
        await RunCyclesAsync(service, () => _hn.IdListCalls >= 2);
        Assert.Equal(1, _hn.ItemCalls(5));
    }

    [Fact]
    public async Task Id_list_failure_keeps_last_snapshot()
    {
        _hn.SetIds(1);
        _hn.SetItemJson(1, ItemJson(1, "one", 100));
        using BestStoriesSnapshotService service = await StartServiceAsync();

        _hn.SetIdsError(HttpStatusCode.BadRequest);
        await RunCyclesAsync(service, () => _hn.IdListCalls >= 2);

        // Failing to refresh the id list must not publish a new (empty) snapshot.
        Assert.Equal(["one"], CurrentTitles());
    }

    /// <summary>Per-id scriptable cache: answers programmed entries, otherwise Miss.
    /// Records writes — including negative entries.</summary>
    private sealed class ScriptedStoryCache : IStoryCache
    {
        private readonly ConcurrentDictionary<int, StoryCacheEntry> _programmed = new();

        public List<int> UnavailableIds { get; } = [];

        public void Program(int id, StoryCacheEntry entry) => _programmed[id] = entry;

        public Task<StoryCacheEntry> GetAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(_programmed.TryGetValue(id, out StoryCacheEntry entry)
                ? entry
                : StoryCacheEntry.Miss);

        public Task SetStoryAsync(HnItem item, CancellationToken cancellationToken)
        {
            _programmed[item.Id] = StoryCacheEntry.FromStory(item);
            return Task.CompletedTask;
        }

        public Task SetUnavailableAsync(int id, CancellationToken cancellationToken)
        {
            UnavailableIds.Add(id);
            _programmed[id] = StoryCacheEntry.Unavailable;
            return Task.CompletedTask;
        }
    }
}
