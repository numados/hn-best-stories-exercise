using System.Net;
using System.Text.Json;
using HackerNews.BestStories.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HackerNews.BestStories.Tests.Integration;

/// <summary>Cache:Provider=Distributed wiring: the story cache must round-trip through
/// the registered IDistributedCache backend (the same contract the Redis backend uses
/// in docker compose), keeping warm cycles free of upstream item calls.</summary>
public sealed class DistributedCacheTests : IAsyncDisposable
{
    private readonly DistributedApiFactory _factory = new();

    private async Task<JsonElement[]> GetStoriesAsync(HttpClient client, string query)
    {
        using HttpResponseMessage response = await client.GetAsync($"/api/beststories?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.EnumerateArray().ToArray();
    }

    [Fact]
    public async Task Distributed_provider_serves_stories_and_reuses_backend_entries()
    {
        Fixtures.SeedLiveFixtures(_factory.HackerNews);
        using HttpClient client = _factory.CreateClient();
        await _factory.AwaitInitialSnapshotAsync();

        JsonElement[] stories = await GetStoriesAsync(client, "n=6");
        Assert.Equal(Fixtures.LiveStoryIds.Length, stories.Length);
        Assert.Equal(Fixtures.LiveStoryIds.Length, _factory.Cache.SetCount);

        await _factory.AdvanceOneRefreshCycleAsync(() => _factory.HackerNews.IdListCalls >= 2);

        Assert.Equal(2, _factory.HackerNews.IdListCalls);
        // Every entry was served by the distributed backend: no new upstream item calls.
        Assert.Equal(Fixtures.LiveStoryIds.Length, _factory.HackerNews.TotalItemCalls);
        Assert.True(_factory.Cache.GetCount > 0);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>Runs the host in Distributed mode with the Redis backend replaced by a
    /// recording in-memory IDistributedCache; the story cache sees only the backend contract.</summary>
    private sealed class DistributedApiFactory : ApiFactory
    {
        public RecordingDistributedCache Cache { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Cache:Provider", "Distributed");
            builder.UseSetting("ConnectionStrings:Redis", "localhost:6379");
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<IDistributedCache>(Cache));
        }
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));

        public int GetCount { get; private set; }
        public int SetCount { get; private set; }

        public byte[]? Get(string key)
        {
            GetCount++;
            return _inner.Get(key);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            SetCount++;
            _inner.Set(key, value, options);
        }

        public void Remove(string key) => _inner.Remove(key);

        public void Refresh(string key) => _inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            GetCount++;
            return _inner.GetAsync(key, token);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            SetCount++;
            return _inner.SetAsync(key, value, options, token);
        }

        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
    }
}
