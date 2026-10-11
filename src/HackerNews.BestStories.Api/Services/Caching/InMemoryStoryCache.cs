using HackerNews.BestStories.Api.Configuration;
using HackerNews.BestStories.Client.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HackerNews.BestStories.Api.Services.Caching;

public sealed class InMemoryStoryCache(
    IMemoryCache cache,
    IOptions<CacheOptions> options) : IStoryCache
{
    public Task<StoryCacheEntry> GetAsync(int id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        StoryCacheEntry entry = cache.TryGetValue(StoryCacheKey.For(id), out StoryCacheEntry cachedEntry)
            ? cachedEntry
            : StoryCacheEntry.Miss;

        return Task.FromResult(entry);
    }

    public Task SetStoryAsync(HnItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        cache.Set(
            StoryCacheKey.For(item.Id),
            StoryCacheEntry.FromStory(item),
            options.Value.ItemTtl);

        return Task.CompletedTask;
    }

    public Task SetUnavailableAsync(int id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        cache.Set(StoryCacheKey.For(id), StoryCacheEntry.Unavailable, options.Value.NegativeTtl);

        return Task.CompletedTask;
    }

}
