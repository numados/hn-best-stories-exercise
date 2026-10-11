using System.Collections.Concurrent;
using System.Collections.Immutable;
using HackerNews.BestStories.Api.Mapping;
using HackerNews.BestStories.Api.Services.Caching;
using HackerNews.BestStories.Client;
using HackerNews.BestStories.Client.Models;

namespace HackerNews.BestStories.Api.Services;

internal sealed class StoryResolver(
    HackerNewsClient hackerNewsClient,
    IStoryCache storyCache,
    ILogger logger)
{
    public async Task ResolveAsync(
        int id,
        ImmutableDictionary<int, HnItem> previousItems,
        ConcurrentDictionary<int, HnItem> nextItems,
        CancellationToken itemCancellationToken)
    {
        try
        {
            StoryCacheEntry cached = await storyCache.GetAsync(id, itemCancellationToken);
            switch (cached.Kind)
            {
                case StoryCacheEntryKind.Story when cached.Story is not null:
                    await AcceptStoryAsync(
                        cached.Story,
                        nextItems,
                        writeToCache: false,
                        itemCancellationToken);
                    return;

                case StoryCacheEntryKind.Unavailable:
                    return;

                case StoryCacheEntryKind.Miss:
                    HnItem? fetched = await hackerNewsClient.GetItemAsync(id, itemCancellationToken);
                    if (fetched is null)
                    {
                        await MarkUnavailableAsync(id, itemCancellationToken);
                        return;
                    }

                    await AcceptStoryAsync(
                        fetched,
                        nextItems,
                        writeToCache: true,
                        itemCancellationToken);
                    return;

                default:
                    throw new InvalidDataException($"Cache entry for item {id} is invalid.");
            }
        }
        catch (OperationCanceledException) when (itemCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (previousItems.TryGetValue(id, out HnItem? previous))
            {
                nextItems.TryAdd(id, previous);
            }

            logger.LogWarning(
                exception,
                "Failed to resolve Hacker News item {StoryId}; retained its previous value when available.",
                id);
        }
    }

    private async Task AcceptStoryAsync(
        HnItem item,
        ConcurrentDictionary<int, HnItem> target,
        bool writeToCache,
        CancellationToken cancellationToken)
    {
        if (item.IsUnavailable())
        {
            await MarkUnavailableAsync(item.Id, cancellationToken);
            return;
        }

        target[item.Id] = item;

        if (!writeToCache)
        {
            return;
        }

        try
        {
            await storyCache.SetStoryAsync(item, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to cache Hacker News item {StoryId}.", item.Id);
        }
    }

    private async Task MarkUnavailableAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await storyCache.SetUnavailableAsync(id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to negatively cache unavailable Hacker News item {StoryId}.",
                id);
        }
    }
}
