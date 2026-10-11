using System.Collections.Concurrent;
using System.Collections.Immutable;
using HackerNews.BestStories.Api.Configuration;
using HackerNews.BestStories.Api.Services.Caching;
using HackerNews.BestStories.Client;
using HackerNews.BestStories.Client.Models;
using Microsoft.Extensions.Options;

namespace HackerNews.BestStories.Api.Services;

/// <summary>
/// Refreshes and publishes snapshots with bounded item concurrency. ID-list failures leave
/// the current snapshot and timestamp unchanged. Resolution failures retain previous items
/// only for IDs still in the list; confirmed unavailability excludes them. Cache-write
/// failures do not discard resolved items, and requested cancellation propagates.
/// </summary>
public sealed class BestStoriesSnapshotService(
    HackerNewsClient hackerNewsClient,
    IStoryCache storyCache,
    SnapshotStore snapshotStore,
    TimeProvider timeProvider,
    IOptions<SnapshotOptions> options,
    ILogger<BestStoriesSnapshotService> logger) : BackgroundService
{
    private readonly StoryResolver _storyResolver = new(hackerNewsClient, storyCache, logger);
    private ImmutableDictionary<int, HnItem> _previousItems =
        ImmutableDictionary<int, HnItem>.Empty;

    /// <summary>Refreshes immediately, then serially on fixed-period timer ticks until cancellation.</summary>
    /// <returns>A task representing the lifetime of the snapshot refresh loop.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);

        using PeriodicTimer timer = new(options.Value.RefreshInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<int> storyIds;

        try
        {
            storyIds = await hackerNewsClient.GetBestStoryIdsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            SnapshotStore.SnapshotState? current = snapshotStore.Current;
            double? ageSeconds = current is null
                ? null
                : current.AgeAt(timeProvider.GetUtcNow());

            logger.LogWarning(
                exception,
                "Failed to fetch the Hacker News best-stories list. Keeping the current snapshot with age {SnapshotAgeSeconds} seconds.",
                ageSeconds);
            return;
        }

        int[] distinctIds = storyIds.Distinct().ToArray();
        ImmutableDictionary<int, HnItem> previousItems = _previousItems;
        ConcurrentDictionary<int, HnItem> nextItems = new();
        ParallelOptions parallelOptions = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = options.Value.FetchParallelism,
        };

        await Parallel.ForEachAsync(
            distinctIds,
            parallelOptions,
            async (id, itemCancellationToken) =>
            {
                await _storyResolver.ResolveAsync(id, previousItems, nextItems, itemCancellationToken);
            });

        ImmutableDictionary<int, HnItem> resolvedItems = nextItems.ToImmutableDictionary();
        _previousItems = resolvedItems;
        snapshotStore.Publish(SnapshotBuilder.Build(resolvedItems.Values));
    }
}
