using System.Collections.Immutable;
using HackerNews.BestStories.Api.Models;

namespace HackerNews.BestStories.Api.Services;

/// <summary>Publishes immutable snapshots through an atomic reference and signals the first publication.</summary>
public sealed class SnapshotStore(TimeProvider timeProvider)
{
    private readonly TaskCompletionSource _initialBuild = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private SnapshotState? _current;

    /// <summary>Atomically reads the latest complete snapshot; null until the first publication.</summary>
    public SnapshotState? Current => Volatile.Read(ref _current);

    /// <summary>Completes after the first snapshot is visible through Current; continuations run asynchronously.</summary>
    public Task InitialBuild => _initialBuild.Task;

    /// <summary>Atomically replaces Current with a timestamped snapshot before completing InitialBuild.</summary>
    public void Publish(ImmutableArray<BestStoryDto> stories)
    {
        SnapshotState next = new(stories, timeProvider.GetUtcNow());
        Volatile.Write(ref _current, next);
        _initialBuild.TrySetResult();
    }

    /// <summary>An immutable story list and its publication time, safe to share between readers.</summary>
    /// <param name="Stories">The ordered stories from one completed refresh.</param>
    /// <param name="TimestampUtc">The publication time from the store's TimeProvider.</param>
    public sealed record SnapshotState(
        ImmutableArray<BestStoryDto> Stories,
        DateTimeOffset TimestampUtc)
    {
        /// <summary>Calculates elapsed seconds since publication, clamping a backwards clock to zero.</summary>
        /// <returns>The non-negative snapshot age in seconds at the supplied time.</returns>
        public double AgeAt(DateTimeOffset now) =>
            Math.Max(0, (now - TimestampUtc).TotalSeconds);
    }
}
