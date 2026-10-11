using System.Text.Json;
using HackerNews.BestStories.Api.Configuration;
using HackerNews.BestStories.Client.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace HackerNews.BestStories.Api.Services.Caching;

/// <summary><see cref="IStoryCache"/> that serializes entries as JSON bytes into a registered
/// <see cref="IDistributedCache"/>, so entries are shared across processes and survive restarts.</summary>
public sealed class DistributedStoryCache(
    IDistributedCache cache,
    IOptions<CacheOptions> options) : IStoryCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    /// <remarks>A key absent from the backend maps to <see cref="StoryCacheEntry.Miss"/>;
    /// lifetimes are delegated to the backend's absolute expiration.</remarks>
    public async Task<StoryCacheEntry> GetAsync(int id, CancellationToken cancellationToken)
    {
        byte[]? value = await cache.GetAsync(StoryCacheKey.For(id), cancellationToken);

        return value is null
            ? StoryCacheEntry.Miss
            : JsonSerializer.Deserialize<StoryCacheEntry>(value, SerializerOptions);
    }

    /// <inheritdoc />
    public Task SetStoryAsync(HnItem item, CancellationToken cancellationToken) =>
        SetAsync(
            item.Id,
            StoryCacheEntry.FromStory(item),
            options.Value.ItemTtl,
            cancellationToken);

    /// <inheritdoc />
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
