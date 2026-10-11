using HackerNews.BestStories.Api.Configuration;
using HackerNews.BestStories.Api.Services.Caching;
using HackerNews.BestStories.Client.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace HackerNews.BestStories.Tests.Unit;

/// <summary>
/// Provider parity tests: every IStoryCache implementation must show the same behavior
/// (miss → story roundtrip, unavailable entries, key isolation). The InMemory variant is
/// exercised through the same assertions as the Distributed one (on the built-in
/// distributed-memory backend), proving the DI swap is behavior-neutral.
/// Expiry timing itself is intentionally not asserted here: IMemoryCache and the
/// distributed backends run on the system clock, and the refresher honors whatever the
/// cache returns — which the integration suite proves with virtual time.
/// </summary>
public sealed class StoryCacheTests
{
    private static CacheOptions TestOptions => new()
    {
        Provider = CacheOptions.InMemoryProvider,
        ItemTtl = TimeSpan.FromMinutes(2),
        NegativeTtl = TimeSpan.FromSeconds(30),
    };

    public static IEnumerable<object[]> Caches()
    {
        yield return [new InMemoryStoryCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(TestOptions))];

        yield return [new DistributedStoryCache(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(TestOptions))];
    }

    private static HnItem Story(int id) => new()
    {
        Id = id,
        Title = $"title {id}",
        By = "user",
        Time = 1_791_400_000,
        Score = id * 10,
        Descendants = id,
    };

    [Theory]
    [MemberData(nameof(Caches))]
    public async Task Miss_then_roundtrip_story(IStoryCache cache)
    {
        HnItem story = Story(1);

        Assert.Equal(StoryCacheEntryKind.Miss, (await cache.GetAsync(1, CancellationToken.None)).Kind);

        await cache.SetStoryAsync(story, CancellationToken.None);

        StoryCacheEntry entry = await cache.GetAsync(1, CancellationToken.None);
        Assert.Equal(StoryCacheEntryKind.Story, entry.Kind);
        Assert.Equal(story.Id, entry.Story!.Id);
        Assert.Equal(story.Title, entry.Story.Title);
        Assert.Equal(story.Score, entry.Story.Score);
        Assert.Equal(story.Time, entry.Story.Time);
        Assert.Equal(story.Descendants, entry.Story.Descendants);
    }

    [Theory]
    [MemberData(nameof(Caches))]
    public async Task Unavailable_entries_are_reported(IStoryCache cache)
    {
        await cache.SetUnavailableAsync(2, CancellationToken.None);

        StoryCacheEntry entry = await cache.GetAsync(2, CancellationToken.None);
        Assert.Equal(StoryCacheEntryKind.Unavailable, entry.Kind);
        Assert.Null(entry.Story);
    }

    [Theory]
    [MemberData(nameof(Caches))]
    public async Task Keys_are_isolated(IStoryCache cache)
    {
        await cache.SetStoryAsync(Story(3), CancellationToken.None);
        await cache.SetUnavailableAsync(4, CancellationToken.None);

        Assert.Equal(StoryCacheEntryKind.Story, (await cache.GetAsync(3, CancellationToken.None)).Kind);
        Assert.Equal(StoryCacheEntryKind.Unavailable, (await cache.GetAsync(4, CancellationToken.None)).Kind);
        Assert.Equal(StoryCacheEntryKind.Miss, (await cache.GetAsync(5, CancellationToken.None)).Kind);
    }
}
