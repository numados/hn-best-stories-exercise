using System.Globalization;
using HackerNews.BestStories.Client.Models;

namespace HackerNews.BestStories.Api.Services.Caching;

/// <summary>Caches story payloads and confirmed unavailability with separate absolute lifetimes.</summary>
public interface IStoryCache
{
    /// <summary>Reads an entry without extending its lifetime.</summary>
    /// <returns>A miss, a story payload, or a confirmed unavailable entry.</returns>
    Task<StoryCacheEntry> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Stores a payload by its item ID for CacheOptions.ItemTtl from this write.</summary>
    /// <returns>A task that completes when the cache write completes.</returns>
    Task SetStoryAsync(HnItem item, CancellationToken cancellationToken);

    /// <summary>Stores confirmed unavailability for CacheOptions.NegativeTtl from this write.</summary>
    /// <returns>A task that completes when the negative cache write completes.</returns>
    Task SetUnavailableAsync(int id, CancellationToken cancellationToken);
}

/// <summary>Distinguishes absent cache entries from payloads and negative cache hits.</summary>
public enum StoryCacheEntryKind
{
    /// <summary>No cached entry exists; the upstream item may be fetched.</summary>
    Miss,
    /// <summary>A cached item payload exists.</summary>
    Story,
    /// <summary>The item was confirmed unavailable; skip fetching until the entry expires.</summary>
    Unavailable,
}

/// <summary>A cache result whose kind distinguishes a miss from confirmed unavailability.</summary>
/// <param name="Kind">The cache result state.</param>
/// <param name="Story">The payload for a Story entry; null for Miss and Unavailable.</param>
public readonly record struct StoryCacheEntry(StoryCacheEntryKind Kind, HnItem? Story)
{
    /// <summary>An absent entry with no payload.</summary>
    public static StoryCacheEntry Miss => new(StoryCacheEntryKind.Miss, null);

    /// <summary>A negative cache hit with no payload.</summary>
    public static StoryCacheEntry Unavailable => new(StoryCacheEntryKind.Unavailable, null);

    /// <summary>Wraps an item payload as a story cache hit.</summary>
    /// <returns>A Story entry containing the supplied payload.</returns>
    public static StoryCacheEntry FromStory(HnItem story) => new(StoryCacheEntryKind.Story, story);
}

internal static class StoryCacheKey
{
    public static string For(int id) =>
        $"hacker-news:story:{id.ToString(CultureInfo.InvariantCulture)}";
}
