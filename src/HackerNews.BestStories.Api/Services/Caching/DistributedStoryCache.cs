using System.Text.Json;
using HackerNews.BestStories.Api.Configuration;
using HackerNews.BestStories.Client.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace HackerNews.BestStories.Api.Services.Caching;

public sealed class DistributedStoryCache(
    IDistributedCache cache,
    IOptions<CacheOptions> options) : IStoryCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<StoryCacheEntry> GetAsync(int id, CancellationToken cancellationToken)
    {
        byte[]? value = await cache.GetAsync(StoryCacheKey.For(id), cancellationToken);

        return value is null
            ? StoryCacheEntry.Miss
            : JsonSerializer.Deserialize<StoryCacheEntry>(value, SerializerOptions);
    }

    public Task SetStoryAsync(HnItem item, CancellationToken cancellationToken) =>
        SetAsync(
            item.Id,
            StoryCacheEntry.FromStory(item),
            options.Value.ItemTtl,
            cancellationToken);

    public Task SetUnavailableAsync(int id, CancellationToken cancellationToken) =>
        SetAsync(id, StoryCacheEntry.Unavailable, options.Value.NegativeTtl, cancellationToken);

    private Task SetAsync(
        int id,
        StoryCacheEntry entry,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        byte[] value = JsonSerializer.SerializeToUtf8Bytes(entry, SerializerOptions);
        DistributedCacheEntryOptions cacheOptions = new()
        {
            AbsoluteExpirationRelativeToNow = ttl,
        };

        return cache.SetAsync(StoryCacheKey.For(id), value, cacheOptions, cancellationToken);
    }

}
